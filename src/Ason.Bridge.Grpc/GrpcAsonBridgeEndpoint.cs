using Ason.Bridge;

namespace Ason.Bridge.Grpc;

/// <summary>
/// A bridge endpoint that forwards to another bridge over gRPC. It lets a process that has no operators of
/// its own - a relay that republishes gRPC as MCP, a test harness, an aggregation host - present the remote
/// application through the same interface the adapters expect.
///
/// Capabilities come from the remote manifest, so what the relay publishes is exactly what the application
/// enabled.
/// </summary>
public sealed class GrpcAsonBridgeEndpoint : IAsonBridgeEndpoint {

    readonly AsonBridgeOptions _options = new() { AppName = "ASON bridge (remote)" };
    readonly GrpcAsonBridgeClient _client;

    public GrpcAsonBridgeEndpoint(GrpcAsonBridgeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

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

    /// <summary>MCP pass-through is not part of the gRPC contract; the relay reports it as unsupported.</summary>
    public Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments, CancellationToken cancellationToken = default) =>
        Task.FromResult(AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.NotSupported,
            "The gRPC bridge contract does not carry MCP tool pass-through; enable it on a bridge that owns the MCP clients."));

    static AsonBridgeExecution ParseExecution(string execution) => execution switch {
        "in-process" => AsonBridgeExecution.InProcess,
        "external-process" => AsonBridgeExecution.ExternalProcess,
        "docker" => AsonBridgeExecution.Docker,
        "remote-runner" => AsonBridgeExecution.RemoteRunner,
        _ => AsonBridgeExecution.InProcess
    };
}
