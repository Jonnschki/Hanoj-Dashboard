using LifeDashboard.App.ViewModels;

namespace LifeDashboard.App.Views;

public partial class SportPage : ContentPage
{
    private readonly SportViewModel _viewModel;

    public SportPage(SportViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        if (_viewModel.LoadCommand.CanExecute(null))
        {
            _viewModel.LoadCommand.Execute(null);
        }
    }
}
