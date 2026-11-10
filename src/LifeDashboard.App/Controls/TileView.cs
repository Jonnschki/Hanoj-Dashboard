namespace LifeDashboard.App.Controls;

/// <summary>
/// Wiederverwendbare Dashboard-Kachel: Rahmen + Titelzeile + beliebiger Inhalt.
/// Verwendung: &lt;controls:TileView Title="Heute"&gt; ...Inhalt... &lt;/controls:TileView&gt;
/// </summary>
public sealed class TileView : ContentView
{
    public static readonly BindableProperty TitleProperty =
        BindableProperty.Create(nameof(Title), typeof(string), typeof(TileView), string.Empty);

    public TileView()
    {
        var title = new Label();
        title.SetDynamicResource(StyleProperty, "TileTitle");
        title.SetBinding(Label.TextProperty, static (TileView t) => t.Title, source: this);

        var presenter = new ContentPresenter();
        var stack = new VerticalStackLayout { Spacing = 10, Children = { title, presenter } };

        var border = new Border { Content = stack };
        border.SetDynamicResource(StyleProperty, "Tile");

        ControlTemplate = new ControlTemplate(() => border);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }
}
