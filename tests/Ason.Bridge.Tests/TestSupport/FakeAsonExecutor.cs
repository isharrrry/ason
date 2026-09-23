using System.Text.Json;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Records every call so tests can prove that a rejected or unsupported request never reaches an executor.
/// </summary>
internal sealed class FakeAsonExecutor : IAsonExecutor {

    /// <summary>What the manifest publishes as the execution location; settable so a test can stand in for one.</summary>
    public string Name { get; set; } = "fake";
    public int StartCalls { get; private set; }
    public List<string> ExecutedScripts { get; } = new();
    public List<AsonBridgeFunctionCall> InvokedFunctions { get; } = new();

    public AsonBridgeCallResult ScriptResult { get; set; } = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("script-ok"));
    public AsonBridgeCallResult FunctionResult { get; set; } = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("function-ok"));
    public AsonBridgeCallResult McpToolResult { get; set; } = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("mcp-ok"));

    /// <summary>Names of the MCP servers the fake bridge consumes; empty means "none configured".</summary>
    public IReadOnlyCollection<string> McpServers { get; set; } = Array.Empty<string>();

    /// <summary>Every MCP tool invocation the fake received, so a test can assert the call was forwarded.</summary>
    public List<(string Server, string Tool)> InvokedMcpTools { get; } = new();

    /// <summary>Log lines the fake emits while a script runs, so a transport's streaming can be asserted.</summary>
    public List<string> LogsToEmit { get; } = new();

    public event EventHandler<AsonBridgeLogEventArgs>? Log;

    public Task StartAsync(CancellationToken cancellationToken = default) {
        StartCalls++;
        return Task.CompletedTask;
    }

    public Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, CancellationToken cancellationToken = default) {
        ExecutedScripts.Add(code);
        foreach (var message in LogsToEmit) {
            Log?.Invoke(this, new AsonBridgeLogEventArgs("Information", message, "fake"));
        }
        return Task.FromResult(ScriptResult);
    }

    public Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        InvokedFunctions.Add(call);
        return Task.FromResult(FunctionResult);
    }

    public Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) {
        InvokedMcpTools.Add((server, tool));
        return Task.FromResult(McpToolResult);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
