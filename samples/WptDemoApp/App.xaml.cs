using System.Windows;

#if !NET9_0_OR_GREATER
using Wpf.Ui.Appearance;
using Wpf.Ui.Markup;
#endif

namespace WpfSampleApp;

/// <summary>
/// Interaction logic for App.xaml
/// </summary>
public partial class App : Application {

    /// <summary>
    /// Which Fluent theme this build uses. The demo targets three frameworks and only .NET 9+ has the
    /// platform Fluent theme, so the net6.0-windows build brings the same look with it (see WpfSampleApp.csproj).
    /// Exposed so the chat panel can show it, which also makes it testable without reading pixels.
    /// </summary>
    public static string ThemeLabel { get; private set; } = "unknown";

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
        ThemeLabel = "Fluent (platform, ThemeMode = Light)";
#else
        // net6.0-windows predates the Fluent theme and has no ThemeMode at all, so without this the app
        // would render with the classic Aero2 theme and look nothing like the net9/net10 builds. WPF-UI
        // ships the same Fluent styling for net6; ThemesDictionary/ControlsDictionary are its own types, so
        // this block only compiles into the net6 leg.
        Resources.MergedDictionaries.Add(new ThemesDictionary { Theme = ApplicationTheme.Light });
        Resources.MergedDictionaries.Add(new ControlsDictionary());
        ThemeLabel = "Fluent (WPF-UI on net6)";
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
