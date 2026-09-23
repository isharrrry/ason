using System.Text.Json;
using Grpc.Core;

namespace Ason.Bridge.Grpc;

/// <summary>
/// Serves the bridge over gRPC. It only translates: capability gating, script validation, operator
/// resolution and error codes all live in <see cref="AsonBridgeRuntime"/>, so this adapter and any other
/// adapter behave identically.
///
/// A disabled capability is reported as gRPC <see cref="StatusCode.Unimplemented"/>, which is the signal any
/// gRPC client understands - and the manifest says the same thing, so a client can avoid the call altogether.
/// </summary>
public sealed class GrpcAsonBridgeService : AsonBridge.AsonBridgeBase {

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    readonly AsonBridgeRuntime _runtime;

    public GrpcAsonBridgeService(AsonBridgeRuntime runtime) => _runtime = runtime ?? throw new ArgumentNullException(nameof(runtime));

    public override async Task<ManifestReply> GetManifest(GetManifestRequest request, ServerCallContext context) {
        var manifest = await _runtime.GetManifestAsync(context.CancellationToken).ConfigureAwait(false);

        var reply = new ManifestReply {
            ProtocolVersion = manifest.ProtocolVersion,
            AppName = manifest.AppName,
            Execution = manifest.Execution,
            ManifestJson = JsonSerializer.Serialize(manifest, Json),
            Capabilities = new Capabilities {
                ListApis = manifest.Capabilities.ListApis,
                ExecuteScript = manifest.Capabilities.ExecuteScript,
                InvokeFunction = manifest.Capabilities.InvokeFunction,
                InvokeMcpTool = manifest.Capabilities.InvokeMcpTool,
                LogStream = manifest.Capabilities.LogStream
            },
            InstancesRevision = manifest.InstancesRevision
        };
        reply.Operators.AddRange(manifest.Api.Operators.Select(o => new OperatorSummary {
            TypeName = o.TypeName,
            IsStatic = o.IsStatic,
            MethodCount = o.Methods.Count
        }));
        reply.Instances.AddRange(manifest.Instances.Select(ToProto));
        return reply;
    }

    public override async Task<InstancesReply> ListInstances(ListInstancesRequest request, ServerCallContext context) {
        var instances = await _runtime.ListInstancesAsync(context.CancellationToken).ConfigureAwait(false);
        var reply = new InstancesReply();
        reply.Instances.AddRange(instances.Select(ToProto));
        return reply;
    }

    public override async Task<ExecuteResult> ExecuteScript(ExecuteScriptRequest request, ServerCallContext context) {
        Require(_runtime.Options.Capabilities.ExecuteScript, "executeScript");
        var result = await _runtime.ExecuteScriptAsync(request.Code, request.IncludeProxyPreamble, request.IncludeInstanceDeclarations, context.CancellationToken).ConfigureAwait(false);
        return ToProto(result);
    }

    public override async Task<ExecuteResult> InvokeMcpTool(InvokeMcpToolRequest request, ServerCallContext context) {
        Require(_runtime.Options.Capabilities.InvokeMcpTool, "invokeMcpTool");
        var result = await _runtime.InvokeMcpToolAsync(request.Server, request.Tool, ParseArgumentObject(request.ArgumentsJson), context.CancellationToken).ConfigureAwait(false);
        return ToProto(result);
    }

    public override async Task<ExecuteResult> InvokeFunction(InvokeFunctionRequest request, ServerCallContext context) {
        Require(_runtime.Options.Capabilities.InvokeFunction, "invokeFunction");
        var call = new AsonBridgeFunctionCall(
            request.Operator,
            request.Method,
            string.IsNullOrEmpty(request.Handle) ? null : request.Handle,
            ParseArguments(request.ArgumentsJson));
        var result = await _runtime.InvokeFunctionAsync(call, context.CancellationToken).ConfigureAwait(false);
        return ToProto(result);
    }

    public override async Task StreamExecution(ExecuteScriptRequest request, IServerStreamWriter<ExecutionEvent> responseStream, ServerCallContext context) {
        Require(_runtime.Options.Capabilities.ExecuteScript, "executeScript");

        // Logs are produced while the script runs, so they are pushed into a channel and streamed as they
        // arrive; the execution itself writes the terminating result event.
        var channel = System.Threading.Channels.Channel.CreateUnbounded<ExecutionEvent>();
        void OnLog(object? sender, AsonBridgeLogEventArgs e) =>
            channel.Writer.TryWrite(new ExecutionEvent { Type = "log", Level = e.Level, Message = e.Message ?? string.Empty });

        _runtime.Log += OnLog;
        var execution = Task.Run(async () => {
            try {
                var result = await _runtime.ExecuteScriptAsync(request.Code, request.IncludeProxyPreamble, request.IncludeInstanceDeclarations, context.CancellationToken).ConfigureAwait(false);
                channel.Writer.TryWrite(new ExecutionEvent { Type = result.Success ? "result" : "error", Result = ToProto(result) });
            }
            finally {
                channel.Writer.TryComplete();
            }
        }, CancellationToken.None);

        try {
            await foreach (var evt in channel.Reader.ReadAllAsync(context.CancellationToken).ConfigureAwait(false)) {
                await responseStream.WriteAsync(evt).ConfigureAwait(false);
            }
            await execution.ConfigureAwait(false);
        }
        finally {
            _runtime.Log -= OnLog;
        }
    }

    static void Require(bool enabled, string capability) {
        if (!enabled) {
            throw new RpcException(new Status(StatusCode.Unimplemented, $"The '{capability}' capability is disabled on this bridge."));
        }
    }

    static IReadOnlyList<JsonElement> ParseArguments(string argumentsJson) {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return Array.Empty<JsonElement>();
        JsonDocument document;
        try {
            document = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException ex) {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"arguments_json is not valid JSON: {ex.Message}"));
        }
        using (document) {
            if (document.RootElement.ValueKind != JsonValueKind.Array) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "arguments_json must be a JSON array, for example [2, 3]."));
            }
            return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
        }
    }

    /// <summary>MCP tool arguments are a JSON object - named tool parameters - rather than a positional array.</summary>
    static IReadOnlyDictionary<string, JsonElement> ParseArgumentObject(string argumentsJson) {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return new Dictionary<string, JsonElement>();
        JsonDocument document;
        try {
            document = JsonDocument.Parse(argumentsJson);
        }
        catch (JsonException ex) {
            throw new RpcException(new Status(StatusCode.InvalidArgument, $"arguments_json is not valid JSON: {ex.Message}"));
        }
        using (document) {
            if (document.RootElement.ValueKind != JsonValueKind.Object) {
                throw new RpcException(new Status(StatusCode.InvalidArgument, "arguments_json must be a JSON object of the tool's parameters, for example {\"path\":\"/tmp\"}."));
            }
            return document.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.Clone(), StringComparer.Ordinal);
        }
    }

    static ExecuteResult ToProto(AsonBridgeCallResult result) => new() {
        Success = result.Success,
        Json = result.Result is { } element ? element.GetRawText() : string.Empty,
        Error = result.Error ?? string.Empty,
        ErrorCode = result.ErrorCode ?? string.Empty
    };

    static Instance ToProto(AsonBridgeInstance instance) => new() {
        Handle = instance.Handle,
        TypeName = instance.TypeName,
        Initialized = instance.Initialized
    };
}
