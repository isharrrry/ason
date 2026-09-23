using System.Text.Json;
using System.Text.Json.Serialization;
using Ason.Transport;

namespace Ason.Bridge.Grpc;

/// <summary>
/// The runner protocol carried over gRPC: the agent's <c>RunnerClient</c> sends an <c>exec</c> line, the
/// application evaluates it and the result travels back. This is what lets an agent reuse the entire ASON
/// pipeline - generated proxies, retries, result handling - while the script itself runs inside the
/// application process.
///
/// The code on these lines is complete (the agent has already prepended the proxy layer it read from the
/// manifest), so the application is asked not to add its own preamble.
/// </summary>
public sealed class GrpcAsonBridgeTransport : IRunnerTransport {

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull };

    readonly GrpcAsonBridgeClient _client;

    public GrpcAsonBridgeTransport(GrpcAsonBridgeClient client) => _client = client ?? throw new ArgumentNullException(nameof(client));

    /// <summary>
    /// Relays the logs the application produces while a script runs. Off by default: the runner protocol only
    /// needs the result, and relaying costs a streaming call per execution.
    /// </summary>
    public bool RelayLogs { get; init; }

    public bool IsStarted { get; private set; }

    public event Action<string>? LineReceived;
    public event Action<string>? Closed;

    public Task StartAsync() {
        IsStarted = true;
        return Task.CompletedTask;
    }

    public Task StopAsync() {
        IsStarted = false;
        return Task.CompletedTask;
    }

    public async Task SendAsync(string jsonLine) {
        using var document = JsonDocument.Parse(jsonLine);
        var root = document.RootElement;
        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
        var id = root.TryGetProperty("id", out var idElement) ? idElement.GetString() ?? NewId() : NewId();

        switch (type) {
            case "exec": {
                var code = root.TryGetProperty("code", out var codeElement) ? codeElement.GetString() ?? string.Empty : string.Empty;
                if (RelayLogs) {
                    AsonBridgeCallResult? completion = null;
                    await foreach (var evt in _client.StreamExecutionAsync(code, includeProxyPreamble: false).ConfigureAwait(false)) {
                        if (evt.Type == "log") EmitLog(id, evt.Level, evt.Message);
                        else completion = FromEvent(evt);
                    }
                    EmitExecResult(id, completion ?? AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, "The application ended the stream without a result."));
                }
                else {
                    EmitExecResult(id, await _client.ExecuteScriptAsync(code, includeProxyPreamble: false).ConfigureAwait(false));
                }
                break;
            }

            // The application resolves operator calls inside its own process, so these never travel this way.
            // Answering with an error is what keeps a misconfigured deployment from hanging forever.
            case "invoke":
                LineReceived?.Invoke(Serialize(new { id, type = "invokeResult", result = (object?)null, error = InvokeNotExpected("invoke") }));
                break;
            case "invokeMcp":
                LineReceived?.Invoke(Serialize(new { id, type = "invokeResult", result = (object?)null, error = InvokeNotExpected("invokeMcp") }));
                break;
        }
    }

    void EmitExecResult(string id, AsonBridgeCallResult result) =>
        LineReceived?.Invoke(Serialize(new { id, type = "execResult", result = result.Result, error = result.Success ? null : result.Error }));

    void EmitLog(string id, string level, string message) =>
        LineReceived?.Invoke(Serialize(new { id, type = "log", level, message, source = "Ason.Bridge.Grpc" }));

    static AsonBridgeCallResult FromEvent(ExecutionEvent evt) {
        if (evt.Result is null) return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, "The application sent no result.");
        if (!evt.Result.Success) {
            var code = string.IsNullOrEmpty(evt.Result.ErrorCode) ? AsonBridgeErrorCodes.ExecutionFailed : evt.Result.ErrorCode;
            return AsonBridgeCallResult.Fail(code, evt.Result.Error);
        }
        if (string.IsNullOrEmpty(evt.Result.Json)) return AsonBridgeCallResult.Ok(null);
        using var document = JsonDocument.Parse(evt.Result.Json);
        return AsonBridgeCallResult.Ok(document.RootElement.Clone());
    }

    static string InvokeNotExpected(string messageType) =>
        $"The application answered an '{messageType}' request, but operators are resolved inside the application process. " +
        "This means the bridge is pointed at an executor that is not the application itself.";

    static string Serialize(object payload) => JsonSerializer.Serialize(payload, Json);

    static string NewId() => Guid.NewGuid().ToString("N");
}
