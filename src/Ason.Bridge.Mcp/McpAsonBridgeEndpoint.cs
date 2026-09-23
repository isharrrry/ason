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

    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string script, bool includeProxyPreamble = true, CancellationToken cancellationToken = default) =>
        _client.ExecuteScriptAsync(script, includeProxyPreamble, cancellationToken);

    public Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) =>
        _client.InvokeFunctionAsync(call, cancellationToken);

    /// <summary>The MCP tool surface has no pass-through tool, so a relay reports it as unsupported.</summary>
    public Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.NotSupported,
            "The MCP tool surface does not carry MCP tool pass-through; enable it on a bridge that owns the MCP clients."));

    static AsonBridgeExecution ParseExecution(string execution) => execution switch {
        "external-process" => AsonBridgeExecution.ExternalProcess,
        "docker" => AsonBridgeExecution.Docker,
        "remote-runner" => AsonBridgeExecution.RemoteRunner,
        _ => AsonBridgeExecution.InProcess
    };
}
