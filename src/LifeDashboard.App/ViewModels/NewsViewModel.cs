using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeDashboard.App.Services;
using LifeDashboard.Core.Domain;

namespace LifeDashboard.App.ViewModels;

/// <summary>Auswahl in einem Filter (Anzeige-Text + Wert, null = alle).</summary>
public sealed record FilterOption<T>(string Label, T? Value)
    where T : struct
{
    public override string ToString() => Label;
}

/// <summary>Eine Geschichte in der Liste.</summary>
public sealed record StoryItem(string Title, string Meta, string? Summary, Uri Link, string OtherSources)
{
    public bool HasOtherSources => OtherSources.Length > 0;

    public bool HasSummary => !string.IsNullOrWhiteSpace(Summary);

    public static StoryItem From(NewsStory story, NewsCoordinator news)
    {
        var others = story.Coverage.Select(c => c.Source).Distinct(StringComparer.Ordinal).Where(s => s != story.Headline.Source).ToList();
        var meta = $"{story.Headline.Source} · {news.Ago(story.Headline.PublishedUtc)}";
        return new StoryItem(
            story.Headline.Title,
            meta,
            story.Headline.Summary,
            story.Headline.Link,
            others.Count == 0 ? string.Empty : $"Auch bei: {string.Join(", ", others)}");
    }
}

/// <summary>News-Seite: relevanteste Schlagzeilen, gefiltert nach Thema und Region, Auto-Aktualisierung.</summary>
public sealed partial class NewsViewModel(NewsCoordinator news) : ObservableObject
{
    public IReadOnlyList<FilterOption<NewsCategory>> Categories { get; } =
    [
        new("Alle Themen", null),
        new("Politik", NewsCategory.Politics),
        new("Welt & Geopolitik", NewsCategory.World),
        new("Wirtschaft", NewsCategory.Economy),
    ];

    public IReadOnlyList<FilterOption<NewsRegion>> Regions { get; } =
    [
        new("Alle Regionen", null),
        new("Österreich", NewsRegion.Austria),
        new("Deutschland", NewsRegion.Germany),
        new("Europa", NewsRegion.Europe),
        new("International", NewsRegion.International),
    ];

    public ObservableCollection<StoryItem> Stories { get; } = [];

    [ObservableProperty]
    public partial FilterOption<NewsCategory>? SelectedCategory { get; set; }

    [ObservableProperty]
    public partial FilterOption<NewsRegion>? SelectedRegion { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string Status { get; set; } = string.Empty;

    partial void OnSelectedCategoryChanged(FilterOption<NewsCategory>? value) => LoadCommand.Execute(null);

    partial void OnSelectedRegionChanged(FilterOption<NewsRegion>? value) => LoadCommand.Execute(null);

    [RelayCommand(AllowConcurrentExecutions = false)]
    private async Task LoadAsync()
    {
        IsBusy = true;
        try
        {
            var stories = await news.GetStoriesAsync(SelectedCategory?.Value, SelectedRegion?.Value);
            Stories.Clear();
            foreach (var story in stories.Take(40))
            {
                Stories.Add(StoryItem.From(story, news));
            }

            var failed = news.Last?.FailedSources ?? [];
            Status = $"Stand {DateTime.Now:HH:mm} · {Stories.Count} wichtigste Geschichten aus {news.Last?.Items.Count ?? 0} Meldungen"
                + (failed.Count > 0 ? $" · nicht erreichbar: {string.Join(", ", failed)}" : string.Empty);
        }
        catch (HttpRequestException)
        {
            Status = "Keine Verbindung. Bitte später erneut versuchen.";
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private static Task OpenAsync(StoryItem item) => Launcher.Default.OpenAsync(item.Link);
}
