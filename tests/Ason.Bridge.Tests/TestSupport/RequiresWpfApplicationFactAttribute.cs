namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs only where the WPF sample exists: it is Windows-only and has to be built first (the Windows CI job
/// does that explicitly, and the Linux job skips the test). Same pattern as the WPF UI tests in this repo.
/// </summary>
public sealed class RequiresWpfApplicationFactAttribute : FactAttribute {
    public RequiresWpfApplicationFactAttribute() {
        if (!OperatingSystem.IsWindows()) {
            Skip = "The WPF application sample only runs on Windows, so this end-to-end test is skipped.";
            return;
        }
        if (WpfApplication.LocateExecutable() is null) {
            Skip = "samples/WpfAppOnlyDemo has not been built, so this end-to-end test is skipped.";
        }
    }
}
