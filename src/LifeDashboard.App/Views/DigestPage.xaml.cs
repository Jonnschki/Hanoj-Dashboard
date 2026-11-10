using LifeDashboard.App.ViewModels;

namespace LifeDashboard.App.Views;

public partial class DigestPage : ContentPage
{
    private readonly DigestViewModel _viewModel;

    public DigestPage(DigestViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.BuildCommand.CanExecute(null))
        {
            _viewModel.BuildCommand.Execute(null);
        }
    }
}
