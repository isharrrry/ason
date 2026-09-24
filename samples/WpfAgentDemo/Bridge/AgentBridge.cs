using System.Reflection;
using Ason;
using Ason.CodeGen;
using Ason.Invocation;
using Ason.Transport;
using Ason.Bridge;
using Ason.Bridge.Grpc;
#if ASON_MCP
using Ason.Bridge.Mcp;
#endif
using ModelContextProtocol.Client;

namespace WpfAgentDemo.Bridge;

public enum AgentTransportKind {
    Grpc,
    Mcp
}

/// <summary>
/// Everything the agent knows about the application: how to reach it, what it published, and how to execute.
///
/// The agent has no operators of its own, so its <see cref="OperatorsLibrary"/> is built from the manifest -
/// the application's script prompt layer - and its runner transport is the bridge. That is the whole
/// integration: no operator is duplicated here.
/// </summary>
internal sealed class AgentBridge : IAsyncDisposable {

    GrpcAsonBridgeClient? _grpc;
#if ASON_MCP
    McpAsonBridgeClient? _mcp;
#endif
    OperatorsLibrary? _library;

    /// <summary>
    /// This build has no MCP client: the official SDK requires net8+, and these legs exist for the legacy hosts
    /// that cannot go above net6. Failing loudly is the point - silently falling back to gRPC would hide which
    /// transport actually ran.
    /// </summary>
    const string McpUnavailable = "This build has no MCP client (the official MCP SDK requires net8+; this is the net6.0-windows leg). Use --transport grpc, or drive the application through the stdio relay (Ason.Bridge.McpHost).";

    AgentBridge(string endpoint, AgentTransportKind transport) {
        Endpoint = endpoint;
        Transport = transport;
    }

    public string Endpoint { get; }

    public AgentTransportKind Transport { get; }

    public AsonBridgeManifest? Manifest { get; private set; }

    /// <summary>Connects and reads the manifest. The endpoint is normalized for MCP (it always ends in /mcp).</summary>
    public static async Task<AgentBridge> ConnectAsync(string endpoint, AgentTransportKind transport, CancellationToken cancellationToken = default) {
        if (string.IsNullOrWhiteSpace(endpoint)) throw new ArgumentException("A bridge endpoint is required.", nameof(endpoint));
        var trimmed = endpoint.Trim().TrimEnd('/');
        var bridge = new AgentBridge(transport == AgentTransportKind.Mcp && !trimmed.EndsWith("/mcp", StringComparison.OrdinalIgnoreCase) ? trimmed + "/mcp" : trimmed, transport);

        switch (transport) {
            case AgentTransportKind.Grpc:
                bridge._grpc = GrpcAsonBridgeClient.Connect(bridge.Endpoint);
                break;
            case AgentTransportKind.Mcp:
#if ASON_MCP
                bridge._mcp = await McpAsonBridgeClient.ConnectAsync(bridge.Endpoint, cancellationToken: cancellationToken).ConfigureAwait(false);
                break;
#else
                throw new PlatformNotSupportedException(McpUnavailable);
#endif
            default:
                throw new ArgumentOutOfRangeException(nameof(transport));
        }

        await bridge.RefreshAsync(cancellationToken).ConfigureAwait(false);
        return bridge;
    }

    /// <summary>Re-reads the manifest and rebuilds the operator library from it.</summary>
    public async Task<AsonBridgeManifest> RefreshAsync(CancellationToken cancellationToken = default) {
        Manifest = Transport switch {
            AgentTransportKind.Grpc => await _grpc!.GetManifestAsync(cancellationToken).ConfigureAwait(false),
#if ASON_MCP
            AgentTransportKind.Mcp => await _mcp!.GetManifestAsync(cancellationToken).ConfigureAwait(false),
#else
            AgentTransportKind.Mcp => throw new PlatformNotSupportedException(McpUnavailable),
#endif
            _ => throw new InvalidOperationException()
        };

        // The manifest carries the generated proxy layer and the signature listing, which is exactly what the
        // client needs in order to be told about the application's API.
        var (proxies, signatures) = (Manifest.Proxies, Manifest.Signatures);
        _library = new OperatorsLibrary(
            Task.FromResult((proxies, signatures, (IOperatorMethodCache)new RemoteOperatorMethodCache())),
            false,
            Array.Empty<IMcpClient>(),
            Array.Empty<Assembly>());
        return Manifest;
    }

    /// <summary>The operator library the ASON client works against - built from the application, not from here.</summary>
    public OperatorsLibrary CreateOperatorsLibrary() => _library ?? throw new InvalidOperationException("Connect before using the bridge.");

    /// <summary>The transport that carries the runner protocol to the application.</summary>
    public IRunnerTransport CreateTransport() => Transport switch {
        AgentTransportKind.Grpc => new GrpcAsonBridgeTransport(_grpc!),
#if ASON_MCP
        AgentTransportKind.Mcp => new McpAsonBridgeTransport(_mcp!),
#else
        AgentTransportKind.Mcp => throw new PlatformNotSupportedException(McpUnavailable),
#endif
        _ => throw new InvalidOperationException()
    };

    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, CancellationToken cancellationToken = default) => Transport switch {
        AgentTransportKind.Grpc => _grpc!.ExecuteScriptAsync(code, cancellationToken: cancellationToken),
#if ASON_MCP
        AgentTransportKind.Mcp => _mcp!.ExecuteScriptAsync(code, cancellationToken: cancellationToken),
#else
        AgentTransportKind.Mcp => throw new PlatformNotSupportedException(McpUnavailable),
#endif
        _ => throw new InvalidOperationException()
    };

    public Task<AsonBridgeCallResult> InvokeFunctionAsync(string @operator, string method, string? argumentsJson, CancellationToken cancellationToken = default) {
        var arguments = string.IsNullOrWhiteSpace(argumentsJson)
            ? Array.Empty<System.Text.Json.JsonElement>()
            : System.Text.Json.JsonDocument.Parse(argumentsJson).RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
        var call = new AsonBridgeFunctionCall(@operator, method, null, arguments);
        return Transport switch {
            AgentTransportKind.Grpc => _grpc!.InvokeFunctionAsync(call, cancellationToken),
#if ASON_MCP
            AgentTransportKind.Mcp => _mcp!.InvokeFunctionAsync(call, cancellationToken),
#else
            AgentTransportKind.Mcp => throw new PlatformNotSupportedException(McpUnavailable),
#endif
            _ => throw new InvalidOperationException()
        };
    }

    public async ValueTask DisposeAsync() {
        if (_grpc is not null) await _grpc.DisposeAsync().ConfigureAwait(false);
#if ASON_MCP
        if (_mcp is not null) await _mcp.DisposeAsync().ConfigureAwait(false);
#endif
    }

    /// <summary>
    /// The agent never resolves an operator method locally - the application does that - so this cache reports
    /// "not found" and nothing else. It exists only because the client insists on having one.
    /// </summary>
    sealed class RemoteOperatorMethodCache : IOperatorMethodCache {
        public bool TryGet(Type declaringType, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
        public bool TryGetStatic(string targetTypeName, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
        public OperatorMethodEntry GetOrAddClosedGeneric(OperatorMethodEntry openEntry, Type[] typeArguments) => openEntry;
    }
}
