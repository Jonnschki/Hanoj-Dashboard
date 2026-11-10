using LifeDashboard.App.ViewModels;

namespace LifeDashboard.App.Views;

public partial class NewsPage : ContentPage
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromMinutes(5);
    private readonly NewsViewModel _viewModel;
    private IDispatcherTimer? _timer;

    public NewsPage(NewsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.LoadCommand.Execute(null);

        // Auto-Aktualisierung nur, solange die Seite sichtbar ist.
        _timer ??= Dispatcher.CreateTimer();
        _timer.Interval = RefreshInterval;
        _timer.Tick -= OnTick;
        _timer.Tick += OnTick;
        _timer.Start();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        _timer?.Stop();
    }

    private void OnTick(object? sender, EventArgs e) => _viewModel.LoadCommand.Execute(null);
}
