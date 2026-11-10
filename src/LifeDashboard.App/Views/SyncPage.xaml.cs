using LifeDashboard.App.ViewModels;

namespace LifeDashboard.App.Views;

public partial class SyncPage : ContentPage
{
    private readonly SyncViewModel _viewModel;

    public SyncPage(SyncViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.OnAppearingAsync();
    }
}
