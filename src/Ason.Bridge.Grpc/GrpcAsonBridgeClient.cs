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
    readonly HttpClient? _ownedHttpClient;

    GrpcAsonBridgeClient(AsonBridge.AsonBridgeClient client, GrpcChannel? ownedChannel, HttpClient? ownedHttpClient = null) {
        _client = client;
        _ownedChannel = ownedChannel;
        _ownedHttpClient = ownedHttpClient;
    }

    /// <summary>
    /// Connects to a bridge at <paramref name="address"/>, for example <c>http://localhost:5222</c>.
    ///
    /// <paramref name="headers"/> are sent with every call, which is how a caller proves who it is when the
    /// application requires authorization (see <c>AddAsonGrpcBridge(runtime, policy)</c>): gRPC metadata is an
    /// HTTP/2 header, so a default request header is the whole mechanism.
    /// </summary>
    public static GrpcAsonBridgeClient Connect(string address, IReadOnlyDictionary<string, string>? headers = null) {
        if (string.IsNullOrWhiteSpace(address)) throw new ArgumentException("An address is required.", nameof(address));

        HttpClient? httpClient = null;
        if (headers is { Count: > 0 }) {
            httpClient = new HttpClient();
            foreach (var header in headers) {
                httpClient.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        var channel = httpClient is null
            ? GrpcChannel.ForAddress(address)
            : GrpcChannel.ForAddress(address, new GrpcChannelOptions { HttpClient = httpClient });
        return new GrpcAsonBridgeClient(new AsonBridge.AsonBridgeClient(channel), channel, httpClient);
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
        _ownedHttpClient?.Dispose();
        return ValueTask.CompletedTask;
    }

    // A capability that is switched off arrives as Unimplemented, a malformed payload as InvalidArgument;
    // folding them back into a result keeps every failure path of a bridge call the same shape for the caller.
    static async Task<AsonBridgeCallResult> Translate(Func<AsyncUnaryCall<ExecuteResult>> call) {
        try {
            return ToDomain(await call().ConfigureAwait(false));
        }
        catch (RpcException ex) {
            // Credentials are the caller's business, not an application result: an authorization failure must
            // surface as a status so it can be fixed, instead of being folded into a failed bridge call.
            if (ex.StatusCode is StatusCode.Unauthenticated or StatusCode.PermissionDenied) throw;
            var code = ex.StatusCode switch {
                StatusCode.Unimplemented => AsonBridgeErrorCodes.NotSupported,
                StatusCode.InvalidArgument => AsonBridgeErrorCodes.InvalidArguments,
                _ => AsonBridgeErrorCodes.ExecutionFailed
            };
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
