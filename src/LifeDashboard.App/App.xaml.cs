namespace LifeDashboard.App;

public partial class App : Application
{
    public App()
    {
        InitializeComponent();
    }

    protected override Window CreateWindow(IActivationState? activationState) => new(new AppShell())
    {
        Title = "Life Dashboard",

        // Sinnvolle Mindestgröße für Windows; auf iOS ohne Wirkung.
        MinimumWidth = 420,
        MinimumHeight = 600,
        Width = 1200,
        Height = 820,
    };
}
