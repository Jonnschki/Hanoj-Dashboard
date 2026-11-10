using System.Globalization;
using System.Text;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.Core.News;

/// <summary>
/// Fasst gleiche Meldungen aus verschiedenen Quellen zu Geschichten zusammen und sortiert sie nach Relevanz.
/// Relevanz = Anzahl unabhängiger Quellen (je mehr Zeitungen berichten, desto wichtiger) + Aktualitätsbonus.
/// Bewusst einfach und nachvollziehbar (keine KI): Ähnlichkeit über gemeinsame Schlüsselwörter im Titel.
/// </summary>
public sealed class HeadlineRanker
{
    private static readonly HashSet<string> StopWords = new(StringComparer.Ordinal)
    {
        // Deutsch
        "der", "die", "das", "den", "dem", "des", "ein", "eine", "einen", "einem", "einer", "und", "oder", "aber", "mit", "von",
        "für", "fuer", "auf", "aus", "bei", "nach", "über", "ueber", "unter", "vor", "zum", "zur", "ist", "sind", "wird", "werden",
        "hat", "haben", "nicht", "sich", "auch", "noch", "wie", "was", "wer", "als", "bis", "gegen", "nun", "jetzt", "mehr",
        "neue", "neuer", "neues", "erste", "will", "soll", "kann", "nach", "dass", "durch", "ohne", "beim", "vom", "ins", "im",
        // Englisch
        "the", "and", "for", "with", "from", "that", "this", "are", "was", "will", "has", "have", "into", "over", "after", "new",
        "says", "say", "its", "his", "her", "who", "why", "how", "what", "not", "but",
    };

    /// <summary>Ab welcher Wort-Überschneidung (Jaccard) zwei Titel als dieselbe Geschichte gelten.</summary>
    public double SimilarityThreshold { get; init; } = 0.3;

    /// <summary>Nur Meldungen, die jünger sind, werden berücksichtigt.</summary>
    public TimeSpan MaxAge { get; init; } = TimeSpan.FromHours(24);

    /// <summary>Halbwertszeit des Aktualitätsbonus.</summary>
    public TimeSpan RecencyHalfLife { get; init; } = TimeSpan.FromHours(3);

    public IReadOnlyList<NewsStory> Rank(IEnumerable<NewsItem> items, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(items);

        var fresh = items
            .Where(i => nowUtc - i.PublishedUtc <= MaxAge && i.PublishedUtc <= nowUtc.AddMinutes(10))
            .GroupBy(i => i.Link.AbsoluteUri, StringComparer.Ordinal).Select(g => g.First()) // gleicher Link = gleiche Meldung
            .OrderByDescending(i => i.PublishedUtc)
            .ToList();

        var clusters = new List<Cluster>();
        foreach (var item in fresh)
        {
            var tokens = Tokenize(item.Title);
            var match = clusters
                .Where(c => c.Category == item.Category)
                .Select(c => (Cluster: c, Similarity: Jaccard(c.Tokens, tokens)))
                .Where(x => x.Similarity >= SimilarityThreshold)
                .OrderByDescending(x => x.Similarity)
                .Select(x => x.Cluster)
                .FirstOrDefault();

            if (match is null)
            {
                clusters.Add(new Cluster(item.Category, tokens, [item]));
            }
            else
            {
                match.Items.Add(item);
            }
        }

        return clusters
            .Select(c => new NewsStory(c.Items[0], c.Items, Score(c.Items, nowUtc)))
            .OrderByDescending(s => s.Score)
            .ThenByDescending(s => s.Headline.PublishedUtc)
            .ToList();
    }

    private double Score(IReadOnlyCollection<NewsItem> items, DateTimeOffset nowUtc)
    {
        var sources = items.Select(i => i.Source).Distinct(StringComparer.Ordinal).Count();
        var newest = items.Max(i => i.PublishedUtc);
        var ageHours = Math.Max(0, (nowUtc - newest).TotalHours);
        var recency = Math.Pow(0.5, ageHours / RecencyHalfLife.TotalHours); // 1.0 = gerade erschienen
        return sources + (1.5 * recency);
    }

    internal static HashSet<string> Tokenize(string title)
    {
        var normalized = RemoveDiacritics(title.ToLowerInvariant());
        var words = new StringBuilder();
        foreach (var ch in normalized)
        {
            words.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
        }

        return words.ToString()
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => w.Length >= 3 && !StopWords.Contains(w))
            .Select(Stem)
            .ToHashSet(StringComparer.Ordinal);
    }

    /// <summary>Sehr einfaches Stemming, damit "Zölle"/"Zoll" oder "Wahlen"/"Wahl" eher zusammenfinden.</summary>
    private static string Stem(string word)
    {
        foreach (var suffix in new[] { "ungen", "ung", "en", "er", "es", "s", "e", "n" })
        {
            if (word.Length > suffix.Length + 3 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    private static string RemoveDiacritics(string text)
    {
        // Deutsche Umlaute wie in der Ersatzschreibweise (Zölle = Zoelle), danach restliche Akzente entfernen.
        var decomposed = text
            .Replace("ä", "ae", StringComparison.Ordinal)
            .Replace("ö", "oe", StringComparison.Ordinal)
            .Replace("ü", "ue", StringComparison.Ordinal)
            .Replace("ß", "ss", StringComparison.Ordinal)
            .Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var ch in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(ch) != UnicodeCategory.NonSpacingMark)
            {
                sb.Append(ch);
            }
        }

        return sb.ToString().Normalize(NormalizationForm.FormC);
    }

    private static double Jaccard(HashSet<string> a, HashSet<string> b)
    {
        if (a.Count == 0 || b.Count == 0)
        {
            return 0;
        }

        var intersection = a.Count(b.Contains);
        return (double)intersection / (a.Count + b.Count - intersection);
    }

    private sealed record Cluster(NewsCategory Category, HashSet<string> Tokens, List<NewsItem> Items);
}
