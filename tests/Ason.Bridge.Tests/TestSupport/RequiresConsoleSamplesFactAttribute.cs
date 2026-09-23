namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs only where the console samples have been built. They are cross-platform, so unlike the WPF tests this
/// runs on Linux too - the condition exists only so an unbuilt tree reports a skip instead of a failure.
/// </summary>
public sealed class RequiresConsoleSamplesFactAttribute : FactAttribute {
    public RequiresConsoleSamplesFactAttribute() {
        if (ConsoleBridgeHost.LocateAssembly() is null) {
            Skip = "samples/ConsoleGrpcBridgeHost has not been built, so this end-to-end test is skipped.";
            return;
        }
        if (ConsoleAgentRunner.LocateAssembly() is null) {
            Skip = "samples/ConsoleAgentSample has not been built, so this end-to-end test is skipped.";
        }
    }
}
