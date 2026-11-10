using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;
using LifeDashboard.Core.Domain;
using LifeDashboard.Infrastructure.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Identity.Client;

namespace LifeDashboard.Infrastructure.MicrosoftGraph;

/// <summary>
/// Microsoft-Anmeldung mit MSAL (öffentlicher Client, System-Browser, Redirect http://localhost).
/// Der MSAL-Token-Cache wird verschlüsselt im <see cref="ISecureTokenStore"/> gespeichert, damit die Anmeldung
/// einen App-Neustart übersteht. Tokens werden still erneuert; nur wenn das nicht geht, ist eine neue Anmeldung nötig.
/// </summary>
public sealed class MicrosoftSession : IAccessTokenSource
{
    private const string CacheKey = "msal-cache";
    private static readonly string[] Scopes = ["User.Read", "Calendars.ReadWrite", "Calendars.Read.Shared"];

    private readonly MicrosoftOptions _options;
    private readonly ISecureTokenStore _store;
    private readonly ILogger<MicrosoftSession> _logger;
    private readonly Lazy<IPublicClientApplication?> _app;

    public MicrosoftSession(IOptions<MicrosoftOptions> options, ISecureTokenStore store, ILogger<MicrosoftSession> logger)
    {
        _options = options.Value;
        _store = store;
        _logger = logger;
        _app = new Lazy<IPublicClientApplication?>(CreateApp);
    }

    public CalendarAccount? Account { get; private set; }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(_options.ClientId);

    public async Task<bool> IsConnectedAsync()
        => _app.Value is { } app && (await app.GetAccountsAsync().ConfigureAwait(false)).Any();

    public async Task<Result<CalendarAccount>> SignInAsync(CancellationToken ct = default)
    {
        if (_app.Value is not { } app)
        {
            return Result.Failure<CalendarAccount>(new Error("auth.config", "Microsoft: Client-ID fehlt (appsettings.Local.json, siehe docs/SETUP.md)."));
        }

        try
        {
            var result = await app.AcquireTokenInteractive(Scopes)
                .WithUseEmbeddedWebView(false)
                .WithPrompt(Prompt.SelectAccount)
                .WithSystemWebViewOptions(new SystemWebViewOptions
                {
                    HtmlMessageSuccess = "<h2>Life Dashboard</h2><p>Anmeldung abgeschlossen. Du kannst dieses Fenster schließen.</p>",
                    HtmlMessageError = "<h2>Life Dashboard</h2><p>Anmeldung fehlgeschlagen: {0} {1}</p>",
                })
                .ExecuteAsync(ct)
                .ConfigureAwait(false);
            Account = ToAccount(result.Account);
            return Result.Success(Account);
        }
        catch (MsalException ex)
        {
            _logger.LogWarning("Microsoft-Anmeldung fehlgeschlagen: {Code}", ex.ErrorCode);
            return Result.Failure<CalendarAccount>(new Error("auth.failed", $"Microsoft-Anmeldung fehlgeschlagen ({ex.ErrorCode})."));
        }
    }

    public async Task SignOutAsync()
    {
        if (_app.Value is { } app)
        {
            foreach (var account in await app.GetAccountsAsync().ConfigureAwait(false))
            {
                await app.RemoveAsync(account).ConfigureAwait(false);
            }
        }

        Account = null;
        await _store.RemoveAsync(CacheKey).ConfigureAwait(false);
    }

    public async Task<Result<string>> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        if (_app.Value is not { } app)
        {
            return Result.Failure<string>(Errors.NotConnected("Microsoft"));
        }

        var account = (await app.GetAccountsAsync().ConfigureAwait(false)).FirstOrDefault();
        if (account is null)
        {
            return Result.Failure<string>(Errors.NotConnected("Microsoft"));
        }

        try
        {
            var result = await app.AcquireTokenSilent(Scopes, account).ExecuteAsync(cancellationToken).ConfigureAwait(false);
            Account = ToAccount(result.Account);
            return Result.Success(result.AccessToken);
        }
        catch (MsalUiRequiredException)
        {
            return Result.Failure<string>(new Error("auth.not_connected", "Microsoft: Anmeldung abgelaufen. Bitte in den Einstellungen neu verbinden."));
        }
        catch (MsalServiceException ex)
        {
            _logger.LogWarning("Microsoft-Token: {Code}", ex.ErrorCode);
            return Result.Failure<string>(Errors.ProviderFailure("Microsoft", ex.ErrorCode));
        }
        catch (MsalClientException ex)
        {
            _logger.LogWarning("Microsoft-Token: {Code}", ex.ErrorCode);
            return Result.Failure<string>(Errors.ProviderFailure("Microsoft", ex.ErrorCode));
        }
    }

    private static CalendarAccount ToAccount(IAccount account)
        => new("microsoft", ProviderKind.Microsoft, account.Username, account.Username);

    private IPublicClientApplication? CreateApp()
    {
        if (!IsConfigured)
        {
            return null;
        }

        var app = PublicClientApplicationBuilder.Create(_options.ClientId)
            .WithAuthority(AzureCloudInstance.AzurePublic, string.IsNullOrWhiteSpace(_options.TenantId) ? "common" : _options.TenantId)
            .WithRedirectUri(string.IsNullOrWhiteSpace(_options.RedirectUri) || !_options.RedirectUri.StartsWith("http", StringComparison.OrdinalIgnoreCase) ? "http://localhost" : _options.RedirectUri)
            .Build();

        // Token-Cache im sicheren Speicher (Keychain / Credential Locker).
        app.UserTokenCache.SetBeforeAccessAsync(async args =>
        {
            var data = await _store.GetAsync(CacheKey).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(data))
            {
                args.TokenCache.DeserializeMsalV3(Convert.FromBase64String(data));
            }
        });
        app.UserTokenCache.SetAfterAccessAsync(async args =>
        {
            if (args.HasStateChanged)
            {
                await _store.SetAsync(CacheKey, Convert.ToBase64String(args.TokenCache.SerializeMsalV3())).ConfigureAwait(false);
            }
        });
        return app;
    }
}

/// <summary>Verbinden/Trennen des Microsoft-Kontos in der Einstellungsseite.</summary>
public sealed class MsalAccountConnector(MicrosoftSession session) : IAccountConnector
{
    public ProviderKind Kind => ProviderKind.Microsoft;

    public Task<bool> IsConnectedAsync(CancellationToken cancellationToken = default) => session.IsConnectedAsync();

    public Task<Result<CalendarAccount>> ConnectAsync(CancellationToken cancellationToken = default) => session.SignInAsync(cancellationToken);

    public Task DisconnectAsync(CancellationToken cancellationToken = default) => session.SignOutAsync();
}
