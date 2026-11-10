using LifeDashboard.App.Services;

namespace LifeDashboard.App;

public partial class App : Application
{
    private readonly DigestScheduler _digestScheduler;

    public App(DigestScheduler digestScheduler)
    {
        InitializeComponent();
        _digestScheduler = digestScheduler;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(new AppShell())
        {
            Title = "Life Dashboard",

            // Sinnvolle Mindestgröße für Windows; auf iOS ohne Wirkung.
            MinimumWidth = 420,
            MinimumHeight = 600,
            Width = 1200,
            Height = 820,
        };

        // Täglicher Digest zur eingestellten Uhrzeit (nur solange die App läuft).
        window.Created += (_, _) => _digestScheduler.Start(window.Dispatcher);
        return window;
    }
}
