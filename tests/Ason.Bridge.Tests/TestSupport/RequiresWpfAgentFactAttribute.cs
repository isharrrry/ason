namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs only where the WPF agent sample exists: Windows-only, and the sample has to be built first. The Linux
/// CI job skips it; the Windows job builds the samples before running this suite.
/// </summary>
public sealed class RequiresWpfAgentFactAttribute : FactAttribute {
    public RequiresWpfAgentFactAttribute() {
        if (!OperatingSystem.IsWindows()) {
            Skip = "The WPF agent sample only runs on Windows, so this end-to-end test is skipped.";
            return;
        }
        if (WpfAgentRunner.LocateExecutable() is null) {
            Skip = "samples/WpfAgentDemo has not been built, so this end-to-end test is skipped.";
        }
    }
}
