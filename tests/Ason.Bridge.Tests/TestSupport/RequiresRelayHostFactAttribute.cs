namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs only where the stdio relay has been built. The relay is cross-platform, so this is not a Windows-only
/// condition - it exists so a machine that has not built `src/Ason.Bridge.McpHost` reports a skip instead of a
/// failure.
/// </summary>
public sealed class RequiresRelayHostFactAttribute : FactAttribute {
    public RequiresRelayHostFactAttribute() {
        if (RelayHost.LocateAssembly() is null) {
            Skip = "src/Ason.Bridge.McpHost has not been built, so this relay test is skipped.";
        }
    }
}
