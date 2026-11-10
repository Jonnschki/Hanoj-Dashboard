using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using LifeDashboard.Core.Abstractions;
using LifeDashboard.Core.Domain;
using Microsoft.Extensions.Logging;

namespace LifeDashboard.Infrastructure.News;

/// <summary>
/// Lädt alle Feeds parallel. Höflich gegenüber den Verlagen:
/// - HTTP-Caching (ETag / Last-Modified → 304 Not Modified ohne erneuten Download),
/// - ein Feed wird höchstens einmal pro <see cref="MinRefreshInterval"/> abgefragt,
/// - maximal 6 gleichzeitige Anfragen.
/// </summary>
public sealed class RssNewsProvider(HttpClient http, IClock clock, ILogger<RssNewsProvider> logger) : INewsProvider
{
    private readonly ConcurrentDictionary<string, CachedFeed> _cache = new(StringComparer.Ordinal);

    public IReadOnlyList<NewsFeed> Feeds { get; init; } = NewsFeedCatalog.Default;

    /// <summary>Mindestabstand zwischen zwei Abrufen desselben Feeds.</summary>
    public TimeSpan MinRefreshInterval { get; init; } = TimeSpan.FromMinutes(2);

    public async Task<NewsSnapshot> GetLatestAsync(CancellationToken cancellationToken = default)
    {
        using var gate = new SemaphoreSlim(6);
        var failed = new ConcurrentBag<string>();

        var tasks = Feeds.Select(async feed =>
        {
            await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var items = await LoadAsync(feed, cancellationToken).ConfigureAwait(false);
                if (items is null)
                {
                    failed.Add(feed.Source);
                }

                return items ?? [];
            }
            finally
            {
                gate.Release();
            }
        });

        var all = (await Task.WhenAll(tasks).ConfigureAwait(false)).SelectMany(x => x).ToList();
        return new NewsSnapshot(all, failed.Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList(), clock.UtcNow);
    }

    /// <returns>Meldungen des Feeds, oder null bei Fehler (dann ggf. letzte bekannte Meldungen aus dem Cache).</returns>
    private async Task<IReadOnlyList<NewsItem>?> LoadAsync(NewsFeed feed, CancellationToken ct)
    {
        _cache.TryGetValue(feed.Id, out var cached);
        if (cached is not null && clock.UtcNow - cached.FetchedUtc < MinRefreshInterval)
        {
            return cached.Items;
        }

        using var request = new HttpRequestMessage(HttpMethod.Get, feed.Url);
        if (cached?.ETag is { } etag)
        {
            request.Headers.IfNoneMatch.Add(etag);
        }

        if (cached?.LastModified is { } lastModified)
        {
            request.Headers.IfModifiedSince = lastModified;
        }

        try
        {
            using var response = await http.SendAsync(request, ct).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.NotModified && cached is not null)
            {
                _cache[feed.Id] = cached with { FetchedUtc = clock.UtcNow };
                return cached.Items;
            }

            if (!response.IsSuccessStatusCode)
            {
                logger.LogWarning("Feed {Feed}: HTTP {Status}", feed.Id, (int)response.StatusCode);
                return cached?.Items is { Count: > 0 } ? cached.Items : null;
            }

            var xml = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var items = FeedParser.Parse(xml, feed);
            _cache[feed.Id] = new CachedFeed(items, clock.UtcNow, response.Headers.ETag, response.Content.Headers.LastModified);
            return items;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            logger.LogWarning("Feed {Feed}: nicht erreichbar ({Type})", feed.Id, ex.GetType().Name);
            return cached?.Items is { Count: > 0 } ? cached.Items : null;
        }
    }

    private sealed record CachedFeed(IReadOnlyList<NewsItem> Items, DateTimeOffset FetchedUtc, EntityTagHeaderValue? ETag, DateTimeOffset? LastModified);
}
