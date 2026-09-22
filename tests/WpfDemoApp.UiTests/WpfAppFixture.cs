using FlaUI.Core.AutomationElements;
using FlaUI.UIA3;

namespace WpfDemoApp.UiTests;

/// <summary>
/// Launches the WPF demo once per test collection and exposes its main window. All UI tests in the
/// collection share the instance because driving several copies of the app at once would fight over
/// window focus.
/// </summary>
public sealed class WpfAppFixture : IDisposable {

    public FlaUI.Core.Application App { get; }
    public UIA3Automation Automation { get; }
    public Window MainWindow { get; }

    public WpfAppFixture() {
        App = FlaUI.Core.Application.Launch(AppUnderTest.ExePath);
        Automation = new UIA3Automation();
        MainWindow = App.GetMainWindow(Automation, TimeSpan.FromSeconds(90))
            ?? throw new InvalidOperationException($"'{AppUnderTest.ExePath}' did not present a main window within 90 seconds.");
        MainWindow.WaitUntilClickable(TimeSpan.FromSeconds(20));
    }

    public void Dispose() {
        try { if (!App.HasExited) App.Close(); } catch { /* window already gone */ }
        try { if (!App.HasExited) App.Kill(); } catch { /* already terminated */ }
        Automation.Dispose();
    }
}

[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class WpfAppCollection : ICollectionFixture<WpfAppFixture> {
    public const string Name = "wpf-demo-app";
}
