using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Infrastructure.Auth;

/// <summary>Konfiguration eines OAuth-2.0-Anbieters (Authorization Code Flow).</summary>
/// <param name="Kind">Anbieter.</param>
/// <param name="AuthorizeEndpoint">Login-Seite.</param>
/// <param name="TokenEndpoint">Token-Endpunkt.</param>
/// <param name="ClientId">Client-ID der App-Registrierung.</param>
/// <param name="ClientSecret">Nur, wenn der Anbieter es auch für Desktop-Apps verlangt (Google Desktop-Client, Strava).</param>
/// <param name="Scope">Angeforderte Berechtigungen.</param>
/// <param name="UsePkce">PKCE verwenden (Google: ja, Strava: nicht unterstützt).</param>
/// <param name="ExtraAuthorizeParameters">Zusätzliche Parameter (z. B. access_type=offline).</param>
public sealed record OAuthSettings(
    ProviderKind Kind,
    Uri AuthorizeEndpoint,
    Uri TokenEndpoint,
    string ClientId,
    string? ClientSecret,
    string Scope,
    bool UsePkce,
    IReadOnlyDictionary<string, string> ExtraAuthorizeParameters);

/// <summary>Gespeicherte Tokens (als JSON im sicheren Speicher).</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresUtc, string? AccountName);

/// <summary>
/// Login, Token-Speicherung und automatische Erneuerung für einen OAuth-Anbieter.
/// Tokens liegen ausschließlich im <see cref="ISecureTokenStore"/> und werden nie geloggt.
/// </summary>
public sealed class OAuthSession(
    OAuthSettings settings,
    HttpClient http,
    LoopbackBrowser browser,
    ISecureTokenStore store,
    IClock clock,
    ILogger<OAuthSession> logger) : IAccessTokenSource
{
    private readonly SemaphoreSlim _lock = new(1, 1);
    private OAuthTokens? _tokens;
    private bool _loaded;

    public ProviderKind Kind => settings.Kind;

    /// <summary>Ermittelt nach dem Login den Kontonamen (z. B. E-Mail). Optional.</summary>
    public Func<string, CancellationToken, Task<string?>>? ResolveAccountName { get; init; }

    /// <summary>Wertet zusätzliche Felder der Token-Antwort aus (z. B. Strava: Athletenname). Optional.</summary>
    public Func<JsonElement, string?>? AccountNameFromTokenResponse { get; init; }

    public CalendarAccount? Account => _tokens is null ? null
        : new CalendarAccount(settings.Kind.ToString().ToLowerInvariant(), settings.Kind, _tokens.AccountName ?? settings.Kind.ToString(), _tokens.AccountName);

    private string StoreKey => $"oauth:{settings.Kind}";

    public async Task<bool> IsConnectedAsync()
    {
        await EnsureLoadedAsync().ConfigureAwait(false);
        return _tokens is not null;
    }

    /// <summary>Interaktiver Login im System-Browser.</summary>
    public async Task<Result<CalendarAccount>> SignInAsync(CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ClientId))
        {
            return Result.Failure<CalendarAccount>(new Error("auth.config", $"{settings.Kind}: Client-ID fehlt (appsettings.Local.json, siehe docs/SETUP.md)."));
        }

        var redirect = LoopbackBrowser.CreateRedirectUri(settings.Kind.ToString().ToLowerInvariant(), out var listener);
        var state = RandomToken(16);
        var verifier = settings.UsePkce ? RandomToken(48) : null;

        var query = new Dictionary<string, string>(settings.ExtraAuthorizeParameters)
        {
            ["client_id"] = settings.ClientId,
            ["redirect_uri"] = redirect.ToString(),
            ["response_type"] = "code",
            ["scope"] = settings.Scope,
            ["state"] = state,
        };
        if (verifier is not null)
        {
            query["code_challenge"] = Challenge(verifier);
            query["code_challenge_method"] = "S256";
        }

        var authorizeUrl = new Uri($"{settings.AuthorizeEndpoint}?{string.Join('&', query.Select(kv => $"{Uri.EscapeDataString(kv.Key)}={Uri.EscapeDataString(kv.Value)}"))}");
        var callback = await browser.AuthorizeAsync(authorizeUrl, listener, ct).ConfigureAwait(false);
        if (callback.IsFailure)
        {
            return Result.Failure<CalendarAccount>(callback.Error);
        }

        var parameters = callback.Value;
        if (parameters.TryGetValue("error", out var error))
        {
            return Result.Failure<CalendarAccount>(new Error("auth.denied", $"{settings.Kind}: Anmeldung abgelehnt ({error})."));
        }

        // Schutz gegen untergeschobene Antworten (CSRF).
        if (!parameters.TryGetValue("state", out var returnedState) || returnedState != state || !parameters.TryGetValue("code", out var code))
        {
            return Result.Failure<CalendarAccount>(new Error("auth.state", $"{settings.Kind}: Ungültige Antwort vom Anmeldeserver."));
        }

        var form = new Dictionary<string, string>
        {
            ["grant_type"] = "authorization_code",
            ["code"] = code,
            ["redirect_uri"] = redirect.ToString(),
            ["client_id"] = settings.ClientId,
        };
        if (settings.ClientSecret is { Length: > 0 } secret)
        {
            form["client_secret"] = secret;
        }

        if (verifier is not null)
        {
            form["code_verifier"] = verifier;
        }

        var tokens = await RequestTokensAsync(form, previousRefreshToken: null, ct).ConfigureAwait(false);
        if (tokens.IsFailure)
        {
            return Result.Failure<CalendarAccount>(tokens.Error);
        }

        var token = tokens.Value;
        if (token.AccountName is null && ResolveAccountName is not null)
        {
            token = token with { AccountName = await ResolveAccountName(token.AccessToken, ct).ConfigureAwait(false) };
        }

        await SaveAsync(token).ConfigureAwait(false);
        return Result.Success(Account!);
    }

    public async Task SignOutAsync()
    {
        _tokens = null;
        _loaded = true;
        await store.RemoveAsync(StoreKey).ConfigureAwait(false);
    }

    public async Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureLoadedAsync().ConfigureAwait(false);
            if (_tokens is null)
            {
                return Result.Failure<string>(Errors.NotConnected(settings.Kind.ToString()));
            }

            if (_tokens.ExpiresUtc - clock.UtcNow > TimeSpan.FromMinutes(2))
            {
                return Result.Success(_tokens.AccessToken);
            }

            if (_tokens.RefreshToken is null)
            {
                return Result.Failure<string>(Errors.NotConnected(settings.Kind.ToString()));
            }

            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = _tokens.RefreshToken,
                ["client_id"] = settings.ClientId,
            };
            if (settings.ClientSecret is { Length: > 0 } secret)
            {
                form["client_secret"] = secret;
            }

            var refreshed = await RequestTokensAsync(form, _tokens.RefreshToken, cancellationToken).ConfigureAwait(false);
            if (refreshed.IsFailure)
            {
                return Result.Failure<string>(refreshed.Error);
            }

            await SaveAsync(refreshed.Value with { AccountName = refreshed.Value.AccountName ?? _tokens.AccountName }).ConfigureAwait(false);
            return Result.Success(_tokens!.AccessToken);
        }
        finally
        {
            _lock.Release();
        }
    }

    private async Task<Result<OAuthTokens>> RequestTokensAsync(Dictionary<string, string> form, string? previousRefreshToken, CancellationToken ct)
    {
        try
        {
            using var content = new FormUrlEncodedContent(form);
            using var response = await http.PostAsync(settings.TokenEndpoint, content, ct).ConfigureAwait(false);
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                // Antwort NICHT loggen (kann Tokens enthalten), nur den Status.
                logger.LogWarning("{Provider}: Token-Anfrage fehlgeschlagen (HTTP {Status})", settings.Kind, (int)response.StatusCode);
                return Result.Failure<OAuthTokens>(new Error("auth.not_connected", $"{settings.Kind}: Anmeldung abgelaufen oder widerrufen. Bitte in den Einstellungen neu verbinden."));
            }

            using var json = JsonDocument.Parse(body);
            var root = json.RootElement;
            var access = root.GetProperty("access_token").GetString()!;
            var refresh = root.TryGetProperty("refresh_token", out var r) ? r.GetString() : previousRefreshToken;
            var expires = root.TryGetProperty("expires_at", out var at) && at.ValueKind == JsonValueKind.Number
                ? DateTimeOffset.FromUnixTimeSeconds(at.GetInt64())
                : clock.UtcNow.AddSeconds(root.TryGetProperty("expires_in", out var inSec) ? inSec.GetInt32() : 3600);
            return Result.Success(new OAuthTokens(access, refresh, expires, AccountNameFromTokenResponse?.Invoke(root)));
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or KeyNotFoundException or InvalidOperationException)
        {
            logger.LogWarning("{Provider}: Token-Anfrage nicht möglich ({Type})", settings.Kind, ex.GetType().Name);
            return Result.Failure<OAuthTokens>(Errors.ProviderFailure(settings.Kind.ToString(), "Anmeldeserver nicht erreichbar."));
        }
    }

    private async Task EnsureLoadedAsync()
    {
        if (_loaded)
        {
            return;
        }

        var json = await store.GetAsync(StoreKey).ConfigureAwait(false);
        if (!string.IsNullOrWhiteSpace(json))
        {
            try
            {
                _tokens = JsonSerializer.Deserialize<OAuthTokens>(json);
            }
            catch (JsonException)
            {
                _tokens = null;
            }
        }

        _loaded = true;
    }

    private async Task SaveAsync(OAuthTokens tokens)
    {
        _tokens = tokens;
        _loaded = true;
        await store.SetAsync(StoreKey, JsonSerializer.Serialize(tokens)).ConfigureAwait(false);
    }

    private static string RandomToken(int bytes) => Base64Url(RandomNumberGenerator.GetBytes(bytes));

    private static string Challenge(string verifier) => Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));

    private static string Base64Url(byte[] data) => Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}

/// <summary>Verbinden/Trennen für OAuth-Anbieter (Google, Strava) in der Einstellungsseite.</summary>
public sealed class OAuthAccountConnector(OAuthSession session) : IAccountConnector
{
    public ProviderKind Kind => session.Kind;

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => session.IsConnectedAsync();

    public Task<Result<CalendarAccount>> ConnectAsync(CancellationToken cancellationToken = default) => session.SignInAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => session.SignOutAsync();
}
