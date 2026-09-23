using System.Text.Json;

namespace Ason.Bridge;

/// <summary>
/// The surface an adapter needs from a bridge. Implemented by <see cref="AsonBridgeRuntime"/> (a bridge that
/// owns the application's operators) and by endpoints that forward to another bridge - which is how a relay
/// can republish an application's gRPC bridge as MCP without knowing anything about its operators.
/// </summary>
public interface IAsonBridgeEndpoint {

    /// <summary>Configuration as far as the adapter can see it; capabilities drive what gets published.</summary>
    AsonBridgeOptions Options { get; }

    /// <summary>Log lines produced by execution, for transports that stream them.</summary>
    event EventHandler<AsonBridgeLogEventArgs>? Log;

    Task<AsonBridgeManifest> GetManifestAsync(CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AsonBridgeInstance>> ListInstancesAsync(CancellationToken cancellationToken = default);

    /// <summary>The whole-script interface.</summary>
    Task<AsonBridgeCallResult> ExecuteScriptAsync(string script, bool includeProxyPreamble = true, CancellationToken cancellationToken = default);

    /// <summary>The single-function interface.</summary>
    Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default);

    /// <summary>Pass-through to a tool on an MCP server the application itself consumes.</summary>
    Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default);
}
