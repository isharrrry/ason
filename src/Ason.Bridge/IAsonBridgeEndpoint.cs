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

    /// <summary>The whole-script interface. <c>includeInstanceDeclarations</c> is the body-only mode: the
    /// application supplies its current proxy layer and instance declarations, so a caller whose manifest
    /// snapshot went stale is rescued.</summary>
    Task<AsonBridgeCallResult> ExecuteScriptAsync(string script, bool includeProxyPreamble = true, bool includeInstanceDeclarations = false, CancellationToken cancellationToken = default);

    /// <summary>The single-function interface.</summary>
    Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default);

    /// <summary>
    /// Runs a script and collects the log lines the executor produced while it ran, for transports that cannot
    /// stream (an MCP tool answers once). The default implementation is the right one for a bridge that owns its
    /// executor: it subscribes to <see cref="Log"/> for the duration of the call. A forwarding endpoint
    /// overrides it to use its transport's own streaming call, so the logs come from the application rather than
    /// from a bridge that never evaluates anything.
    /// </summary>
    async Task<AsonBridgeStreamedResult> ExecuteScriptWithLogsAsync(string script, bool includeProxyPreamble = true, bool includeInstanceDeclarations = false, CancellationToken cancellationToken = default) {
        var logs = new List<AsonBridgeLogEventArgs>();
        void OnLog(object? sender, AsonBridgeLogEventArgs entry) => logs.Add(entry);

        Log += OnLog;
        try {
            var result = await ExecuteScriptAsync(script, includeProxyPreamble, includeInstanceDeclarations, cancellationToken).ConfigureAwait(false);
            return new AsonBridgeStreamedResult(result, logs);
        }
        finally {
            Log -= OnLog;
        }
    }

    /// <summary>Pass-through to a tool on an MCP server the application itself consumes.</summary>
    Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default);
}
