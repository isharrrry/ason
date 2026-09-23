using System.Text.Json;
using Ason.Bridge;

namespace Ason.Bridge.Mcp;

/// <summary>
/// A bridge endpoint that forwards to an application over MCP. It is the counterpart of
/// <see cref="Grpc.GrpcAsonBridgeEndpoint"/> and exists for the same reason: a process with no operators of its
/// own - the stdio relay, above all - presents a remote application through the interface the adapters expect.
///
/// With this in place the relay no longer has to speak gRPC: an application that publishes MCP can be relayed
/// as MCP, and gRPC becomes one option among several rather than a prerequisite.
/// </summary>
public sealed class McpAsonBridgeEndpoint : IAsonBridgeEndpoint {

    readonly AsonBridgeOptions _options = new() { AppName = "ASON bridge (remote, MCP)" };
    readonly McpAsonBridgeClient _client;

    public McpAsonBridgeEndpoint(McpAsonBridgeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    public AsonBridgeOptions Options => _options;

    /// <summary>The manifest most recently read from the application, if any.</summary>
    public AsonBridgeManifest? Manifest { get; private set; }

    // A manifest client has no log stream of its own; declared explicitly so no unused-event warning is raised.
    public event EventHandler<AsonBridgeLogEventArgs>? Log { add { } remove { } }

    public async Task<AsonBridgeManifest> GetManifestAsync(CancellationToken cancellationToken = default) {
        var manifest = await _client.GetManifestAsync(cancellationToken).ConfigureAwait(false);
        Manifest = manifest;
        _options.AppName = manifest.AppName;
        _options.Capabilities = manifest.Capabilities;
        _options.Execution = ParseExecution(manifest.Execution);
        return manifest;
    }

    public Task<IReadOnlyList<AsonBridgeInstance>> ListInstancesAsync(CancellationToken cancellationToken = default) =>
        _client.ListInstancesAsync(cancellationToken);

    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string script, bool includeProxyPreamble = true, bool includeInstanceDeclarations = false, CancellationToken cancellationToken = default) =>
        _client.ExecuteScriptAsync(script, includeProxyPreamble, includeInstanceDeclarations, cancellationToken);

    public Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) =>
        _client.InvokeFunctionAsync(call, cancellationToken);

    /// <summary>
    /// Relays the pass-through to the application that owns the MCP clients, exactly as the gRPC endpoint does:
    /// the relay has no MCP servers of its own, so refusing locally would hide a capability the application has.
    /// </summary>
    public Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) =>
        _client.InvokeMcpToolAsync(server, tool, arguments, cancellationToken);

    static AsonBridgeExecution ParseExecution(string execution) => execution switch {
        "external-process" => AsonBridgeExecution.ExternalProcess,
        "docker" => AsonBridgeExecution.Docker,
        "remote-runner" => AsonBridgeExecution.RemoteRunner,
        _ => AsonBridgeExecution.InProcess
    };
}
