using System.Runtime.CompilerServices;
using System.Text.Json;
using Grpc.Core;
using Grpc.Net.Client;

namespace Ason.Bridge.Grpc;

/// <summary>
/// A typed client for an application's gRPC bridge. It turns the wire messages back into the domain types
/// (<see cref="AsonBridgeManifest"/>, <see cref="AsonBridgeFunctionCall"/>, <see cref="AsonBridgeCallResult"/>)
/// so a consumer never touches the generated stubs - and so a gRPC client looks exactly like an MCP one.
/// </summary>
public sealed class GrpcAsonBridgeClient : IAsyncDisposable {

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly AsonBridge.AsonBridgeClient _client;
    readonly GrpcChannel? _ownedChannel;

    GrpcAsonBridgeClient(AsonBridge.AsonBridgeClient client, GrpcChannel? ownedChannel) {
        _client = client;
        _ownedChannel = ownedChannel;
    }

    /// <summary>Connects to a bridge at <paramref name="address"/>, for example <c>http://localhost:5222</c>.</summary>
    public static GrpcAsonBridgeClient Connect(string address) {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("An address is required.", nameof(address));
        var channel = GrpcChannel.ForAddress(address);
        return new GrpcAsonBridgeClient(new AsonBridge.AsonBridgeClient(channel), channel);
    }

    /// <summary>Uses a channel the caller owns (its lifetime stays with the caller).</summary>
    public static GrpcAsonBridgeClient FromChannel(GrpcChannel channel) => new(new AsonBridge.AsonBridgeClient(channel), null);

    /// <summary>The generated client, for callers that need a stub this wrapper does not model.</summary>
    public AsonBridge.AsonBridgeClient Raw => _client;

    public async Task<AsonBridgeManifest> GetManifestAsync(CancellationToken cancellationToken = default) {
        var reply = await _client.GetManifestAsync(new GetManifestRequest(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return JsonSerializer.Deserialize<AsonBridgeManifest>(reply.ManifestJson, Json)
            ?? throw new RpcException(new Status(StatusCode.Internal, "The bridge returned an empty manifest."));
    }

    public async Task<IReadOnlyList<AsonBridgeInstance>> ListInstancesAsync(CancellationToken cancellationToken = default) {
        var reply = await _client.ListInstancesAsync(new ListInstancesRequest(), cancellationToken: cancellationToken).ConfigureAwait(false);
        return reply.Instances.Select(i => new AsonBridgeInstance(i.Handle, i.TypeName, i.Initialized)).ToList();
    }

    /// <summary>The whole-script interface.</summary>
    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, bool includeProxyPreamble = true, CancellationToken cancellationToken = default) =>
        Translate(() => _client.ExecuteScriptAsync(
            new ExecuteScriptRequest { Code = code, IncludeProxyPreamble = includeProxyPreamble },
            cancellationToken: cancellationToken));

    /// <summary>The single-function interface.</summary>
    public Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        if (call is null) throw new ArgumentNullException(nameof(call));
        var arguments = call.EffectiveArguments.Count == 0 ? string.Empty : "[" + string.Join(",", call.EffectiveArguments.Select(a => a.GetRawText())) + "]";
        return Translate(() => _client.InvokeFunctionAsync(
            new InvokeFunctionRequest {
                Operator = call.Operator ?? string.Empty,
                Method = call.Method ?? string.Empty,
                Handle = call.Handle ?? string.Empty,
                ArgumentsJson = arguments
            },
            cancellationToken: cancellationToken));
    }

    /// <summary>Executes a script and streams the application's logs while it runs.</summary>
    public async IAsyncEnumerable<ExecutionEvent> StreamExecutionAsync(string code, bool includeProxyPreamble = true, [EnumeratorCancellation] CancellationToken cancellationToken = default) {
        using var streaming = _client.StreamExecution(
            new ExecuteScriptRequest { Code = code, IncludeProxyPreamble = includeProxyPreamble },
            cancellationToken: cancellationToken);
        while (await streaming.ResponseStream.MoveNext(cancellationToken).ConfigureAwait(false)) {
            yield return streaming.ResponseStream.Current;
        }
    }

    public ValueTask DisposeAsync() {
        _ownedChannel?.Dispose();
        return ValueTask.CompletedTask;
    }

    // A capability that is switched off arrives as Unimplemented; folding it back into a result keeps every
    // failure path of a bridge call the same shape for the caller.
    static async Task<AsonBridgeCallResult> Translate(Func<AsyncUnaryCall<ExecuteResult>> call) {
        try {
            return ToDomain(await call().ConfigureAwait(false));
        }
        catch (RpcException ex) {
            var code = ex.StatusCode == StatusCode.Unimplemented ? AsonBridgeErrorCodes.NotSupported : AsonBridgeErrorCodes.ExecutionFailed;
            return AsonBridgeCallResult.Fail(code, ex.Status.Detail);
        }
    }

    static AsonBridgeCallResult ToDomain(ExecuteResult result) {
        if (!result.Success) {
            var code = string.IsNullOrEmpty(result.ErrorCode) ? AsonBridgeErrorCodes.ExecutionFailed : result.ErrorCode;
            return AsonBridgeCallResult.Fail(code, string.IsNullOrEmpty(result.Error) ? "The bridge reported a failure." : result.Error);
        }
        if (string.IsNullOrEmpty(result.Json)) return AsonBridgeCallResult.Ok(null);
        using var document = JsonDocument.Parse(result.Json);
        return AsonBridgeCallResult.Ok(document.RootElement.Clone());
    }
}
