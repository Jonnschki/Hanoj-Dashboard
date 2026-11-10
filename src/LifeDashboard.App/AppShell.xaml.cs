using LifeDashboard.App.Views;

namespace LifeDashboard.App;

public partial class AppShell : Shell
{
    public AppShell()
    {
        InitializeComponent();

        // Push-Route für die Einstellungen (iPhone: über Zahnrad-Symbol; Desktop: zusätzlich im Flyout).
        Routing.RegisterRoute("settings", typeof(SettingsPage));
    }
}
