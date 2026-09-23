using System.Text.Json;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Records every call so tests can prove that a rejected or unsupported request never reaches an executor.
/// </summary>
internal sealed class FakeAsonExecutor : IAsonExecutor {

    public string Name => "fake";
    public int StartCalls { get; private set; }
    public List<string> ExecutedScripts { get; } = new();
    public List<AsonBridgeFunctionCall> InvokedFunctions { get; } = new();

    public AsonBridgeCallResult ScriptResult { get; set; } = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("script-ok"));
    public AsonBridgeCallResult FunctionResult { get; set; } = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("function-ok"));

    public Task StartAsync(CancellationToken cancellationToken = default) {
        StartCalls++;
        return Task.CompletedTask;
    }

    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, CancellationToken cancellationToken = default) {
        ExecutedScripts.Add(code);
        return Task.FromResult(ScriptResult);
    }

    public Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        InvokedFunctions.Add(call);
        return Task.FromResult(FunctionResult);
    }

    public Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default)
        => Task.FromResult(AsonBridgeCallResult.Ok(null));

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
