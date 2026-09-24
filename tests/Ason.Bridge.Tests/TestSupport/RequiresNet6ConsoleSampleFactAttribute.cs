namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs only where the console sample's <c>net6.0</c> leg has been built. That leg is the legacy-host shape this
/// suite is about: no embedded MCP server, so the application is reached over gRPC (or through the stdio relay).
/// </summary>
public sealed class RequiresNet6ConsoleSampleFactAttribute : FactAttribute {
    public RequiresNet6ConsoleSampleFactAttribute() {
        if (ConsoleBridgeHost.LocateAssembly(ConsoleBridgeHost.Net6Framework) is null) {
            Skip = "samples/ConsoleBridgeAppSample has not been built for net6.0, so this end-to-end test is skipped.";
        }
    }
}
