using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using LifeDashboard.App.Services;
using LifeDashboard.Core.Sync;

namespace LifeDashboard.App.ViewModels;

/// <summary>Sync-Seite: Start, Fortschritt, Live-Log, Ergebnis.</summary>
public sealed partial class SyncViewModel : ObservableObject, IQueryAttributable
{
    private readonly SyncCoordinator _sync;
    private bool _autoStart;

    public SyncViewModel(SyncCoordinator sync)
    {
        _sync = sync;
        DryRun = sync.Settings.DryRunByDefault;
    }

    public ObservableCollection<string> Log { get; } = [];

    public ObservableCollection<SyncActionItem> Actions { get; } = [];

    /// <summary>Die letzten Läufe (aus dem Ledger).</summary>
    public ObservableCollection<SyncRunItem> History { get; } = [];

    [ObservableProperty]
    public partial bool DryRun { get; set; }

    [ObservableProperty]
    public partial bool ConfirmDeletes { get; set; }

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(StartCommand))]
    public partial bool IsRunning { get; set; }

    [ObservableProperty]
    public partial double Progress { get; set; }

    [ObservableProperty]
    public partial string Summary { get; set; } = "Bereit. Tipp: zuerst als Dry-Run starten.";

    [ObservableProperty]
    public partial int Created { get; set; }

    [ObservableProperty]
    public partial int Updated { get; set; }

    [ObservableProperty]
    public partial int Deleted { get; set; }

    [ObservableProperty]
    public partial int Conflicts { get; set; }

    [ObservableProperty]
    public partial int Errors { get; set; }

    [ObservableProperty]
    public partial int PendingDeletes { get; set; }

    public void ApplyQueryAttributes(IDictionary<string, object> query)
        => _autoStart = query.TryGetValue("autostart", out var v) && string.Equals(v?.ToString(), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>Wird beim Erscheinen der Seite aufgerufen (Start per Dashboard-Button).</summary>
    public async Task OnAppearingAsync()
    {
        await LoadHistoryAsync();
        if (_autoStart)
        {
            _autoStart = false;
            await StartAsync();
        }
    }

    private bool CanStart() => !IsRunning;

    [RelayCommand(CanExecute = nameof(CanStart))]
    private async Task StartAsync()
    {
        IsRunning = true;
        Log.Clear();
        Actions.Clear();
        Progress = 0;
        try
        {
            // Progress<T> meldet automatisch auf dem UI-Thread zurück.
            var progress = new Progress<SyncProgress>(p =>
            {
                Progress = p.Fraction;
                Log.Add($"{DateTime.Now:HH:mm:ss}  {p.Message}");
            });

            var report = await Task.Run(() => _sync.RunAsync(DryRun, ConfirmDeletes, progress));

            Created = report.Created;
            Updated = report.Updated;
            Deleted = report.Deleted;
            Conflicts = report.Conflicts;
            Errors = report.Errors;
            PendingDeletes = report.PendingDeletes;
            foreach (var line in report.Log.Where(l => l.Level != "Info"))
            {
                Log.Add($"{line.Level}: {line.Message}");
            }

            foreach (var action in report.Actions.Where(a => a.Kind != SyncActionKind.Unchanged))
            {
                Actions.Add(new SyncActionItem(action.Kind.ToString(), action.Title, action.Target.Name, action.Reason ?? action.Error ?? string.Empty));
            }

            Summary = report.DryRun
                ? "Dry-Run: Es wurde nichts geändert. Prüfe die Vorschau und starte dann den echten Sync."
                : "Sync abgeschlossen.";
        }
        catch (Exception ex)
        {
            Summary = $"Fehler: {ex.Message}";
            Log.Add(ex.ToString());
        }
        finally
        {
            IsRunning = false;
            await LoadHistoryAsync();
        }
    }

    private async Task LoadHistoryAsync()
    {
        History.Clear();
        foreach (var run in await _sync.GetRecentRunsAsync(10))
        {
            History.Add(SyncRunItem.From(run));
        }
    }
}

/// <summary>Zeile in der Ergebnisliste.</summary>
public sealed record SyncActionItem(string Kind, string Title, string Target, string Reason);
