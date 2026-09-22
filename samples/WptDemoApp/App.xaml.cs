using System.Windows;

namespace WpfSampleApp; 
/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {

    // SystemColors.AccentColor is a .NET 9+ (Fluent) member; on net6.0-windows we fall back
    // to the classic highlight color.
    static System.Windows.Media.Color AccentColor {
        get {
#if NET9_0_OR_GREATER
            return SystemColors.AccentColor;
#else
            return SystemColors.HighlightColor;
#endif
        }
    }

    public App() {
#if NET9_0_OR_GREATER
        // ThemeMode is a .NET 9+ WPF API, so it cannot stay in App.xaml while the project also
        // targets net6.0-windows. WPF0001 marks it as evaluation-only: XAML usage is not
        // flagged, C# usage is.
#pragma warning disable WPF0001
        ThemeMode = System.Windows.ThemeMode.Light;
#pragma warning restore WPF0001
#endif
    }

    protected override void OnStartup(StartupEventArgs e) {
        // Views reference {DynamicResource AsonAccentBrush} instead of the .NET 9-only
        // SystemColors.AccentColorBrushKey, so net9/net10 keep the system accent brush and
        // net6.0-windows gets the closest classic equivalent.
        Resources["AsonAccentBrush"] = new System.Windows.Media.SolidColorBrush(AccentColor);
        base.OnStartup(e);
    }
}
