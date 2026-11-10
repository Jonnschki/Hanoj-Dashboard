using System.Net;
using System.Net.Sockets;
using System.Text;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Common;

namespace LifeDashboard.Infrastructure.Auth;

/// <summary>
/// OAuth-Login für Desktop-Apps (RFC 8252): öffnet den System-Browser und empfängt die Rückleitung
/// auf http://127.0.0.1:{freier Port}/… . Bewusst mit <see cref="TcpListener"/> statt HttpListener:
/// funktioniert ohne Administratorrechte und ohne URL-Reservierung.
/// </summary>
public sealed class LoopbackBrowser(IUrlLauncher launcher)
{
    /// <summary>Wie lange auf die Anmeldung im Browser gewartet wird.</summary>
    public TimeSpan Timeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Reserviert einen freien Port und liefert die Redirect-URI dafür.</summary>
    public static Uri CreateRedirectUri(string path, out TcpListener listener)
    {
        listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        return new Uri($"http://127.0.0.1:{port}/{path.TrimStart('/')}");
    }

    /// <summary>Öffnet <paramref name="authorizeUrl"/> und wartet auf den Aufruf der Redirect-URI. Liefert deren Query-Parameter.</summary>
    public async Task<Result<IReadOnlyDictionary<string, string>>> AuthorizeAsync(Uri authorizeUrl, TcpListener listener, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(listener);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(Timeout);
        try
        {
            await launcher.OpenAsync(authorizeUrl, cancellationToken).ConfigureAwait(false);

            while (true)
            {
                using var client = await listener.AcceptTcpClientAsync(timeout.Token).ConfigureAwait(false);
                var stream = client.GetStream();
                var requestLine = await ReadRequestLineAsync(stream, timeout.Token).ConfigureAwait(false);

                // Browser fragen gern zusätzlich /favicon.ico an – ignorieren.
                var target = requestLine?.Split(' ') is [_, var path, ..] ? path : null;
                if (target is null || !target.Contains('?', StringComparison.Ordinal))
                {
                    await RespondAsync(stream, HttpStatusCode.NotFound, string.Empty, timeout.Token).ConfigureAwait(false);
                    continue;
                }

                var query = ParseQuery(target[(target.IndexOf('?', StringComparison.Ordinal) + 1)..]);
                var ok = !query.ContainsKey("error");
                await RespondAsync(
                    stream,
                    HttpStatusCode.OK,
                    ok ? "Anmeldung abgeschlossen. Du kannst dieses Fenster schließen und zur App zurückkehren."
                       : "Anmeldung abgebrochen. Du kannst dieses Fenster schließen.",
                    timeout.Token).ConfigureAwait(false);
                return Result.Success<IReadOnlyDictionary<string, string>>(query);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Result.Failure<IReadOnlyDictionary<string, string>>(new Error("auth.timeout", "Anmeldung nicht innerhalb von 5 Minuten abgeschlossen."));
        }
        finally
        {
            listener.Stop();
        }
    }

    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('=', 2);
            result[Uri.UnescapeDataString(kv[0])] = kv.Length > 1 ? Uri.UnescapeDataString(kv[1].Replace('+', ' ')) : string.Empty;
        }

        return result;
    }

    private static async Task<string?> ReadRequestLineAsync(NetworkStream stream, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var total = 0;
        while (total < buffer.Length)
        {
            var read = await stream.ReadAsync(buffer.AsMemory(total), ct).ConfigureAwait(false);
            if (read == 0)
            {
                break;
            }

            total += read;
            var text = Encoding.ASCII.GetString(buffer, 0, total);
            if (text.Contains("\r\n\r\n", StringComparison.Ordinal) || text.Contains("\n\n", StringComparison.Ordinal))
            {
                return text.Split('\n')[0].TrimEnd('\r');
            }
        }

        return total > 0 ? Encoding.ASCII.GetString(buffer, 0, total).Split('\n')[0].TrimEnd('\r') : null;
    }

    private static async Task RespondAsync(NetworkStream stream, HttpStatusCode status, string message, CancellationToken ct)
    {
        var html = $"<!doctype html><html lang=\"de\"><meta charset=\"utf-8\"><title>Life Dashboard</title><body style=\"font-family:sans-serif;padding:3em\"><h2>Life Dashboard</h2><p>{WebUtility.HtmlEncode(message)}</p></body></html>";
        var body = Encoding.UTF8.GetBytes(html);
        var header = Encoding.ASCII.GetBytes($"HTTP/1.1 {(int)status} {status}\r\nContent-Type: text/html; charset=utf-8\r\nContent-Length: {body.Length}\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(header, ct).ConfigureAwait(false);
        await stream.WriteAsync(body, ct).ConfigureAwait(false);
        await stream.FlushAsync(ct).ConfigureAwait(false);
    }
}
