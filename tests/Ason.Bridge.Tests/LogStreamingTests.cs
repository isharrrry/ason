using System.Net.Http.Json;
using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Log streaming (T5). `Capabilities.LogStream` says the runtime supports relaying an execution's logs; whether
/// a transport can deliver them is visible in that transport's own surface - the gRPC `StreamExecution` rpc, the
/// HTTP SSE route, the MCP `ason_stream_script` tool. These tests pin the two adapters that were still missing,
/// and that switching the capability off removes them instead of leaving a silent hole.
/// </summary>
public class LogStreamingTests {

    static AsonBridgeOptions OptionsWith(FakeAsonExecutor executor, bool logStream = true, bool executeScript = true) {
        var options = BridgeTestApp.Options();
        options.Executor = executor;
        options.Capabilities = new AsonBridgeCapabilities { ExecuteScript = executeScript, LogStream = logStream };
        return options;
    }

    static FakeAsonExecutor ExecutorThatLogs(params string[] lines) {
        var executor = new FakeAsonExecutor();
        executor.LogsToEmit.AddRange(lines);
        executor.ScriptResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement(42));
        return executor;
    }

    [Fact]
    public async Task The_HTTP_adapter_streams_the_logs_before_the_result() {
        await using var runtime = new AsonBridgeRuntime(OptionsWith(ExecutorThatLogs("first line", "second line")));
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/script/stream", new { code = "return 42;" });
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/event-stream", response.Content.Headers.ContentType?.MediaType);
        var events = Sse.Parse(body);
        Assert.Equal(new[] { "log", "log", "result" }, events.Select(e => e.Name).ToArray());
        Assert.Contains("first line", events[0].Data);
        Assert.Contains("second line", events[1].Data);
        Assert.Equal(42, JsonDocument.Parse(events[2].Data).RootElement.GetProperty("result").GetInt32());
    }

    [Fact]
    public async Task The_HTTP_adapter_reports_a_failed_script_as_an_error_event() {
        var executor = ExecutorThatLogs("starting");
        executor.ScriptResult = AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, "boom");
        await using var runtime = new AsonBridgeRuntime(OptionsWith(executor));
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var body = await (await http.PostAsJsonAsync("/ason/script/stream", new { code = "return 1;" })).Content.ReadAsStringAsync();
        var events = Sse.Parse(body);

        Assert.Equal(new[] { "log", "error" }, events.Select(e => e.Name).ToArray());
        Assert.Contains("boom", events[1].Data);
    }

    [Fact]
    public async Task The_MCP_tool_returns_the_logs_together_with_the_result() {
        await using var runtime = new AsonBridgeRuntime(OptionsWith(ExecutorThatLogs("a", "b")));
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        var streamed = await client.StreamScriptAsync("return 42;");

        Assert.Contains("ason_stream_script", tools);
        Assert.True(streamed.Result.Success, streamed.Result.Error);
        Assert.Equal(2, streamed.Logs.Count);
        Assert.Equal("a", streamed.Logs[0].Message);
        Assert.Equal(42, streamed.Result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task The_streaming_surfaces_are_absent_when_the_capability_is_off() {
        await using var runtime = new AsonBridgeRuntime(OptionsWith(ExecutorThatLogs("x"), logStream: false));
        await using var host = await OpenApiHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(mcpHost.Url);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/script/stream", new { code = "return 1;" });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        Assert.DoesNotContain("ason_stream_script", (await client.ListToolsAsync()).Select(t => t.Name));

        // The non-streaming interfaces are untouched: the capability is independent.
        Assert.Contains("ason_execute_script", (await client.ListToolsAsync()).Select(t => t.Name));
    }

    [Fact]
    public async Task Streaming_needs_the_script_interface_it_streams() {
        await using var runtime = new AsonBridgeRuntime(OptionsWith(ExecutorThatLogs("x"), executeScript: false));
        await using var host = await OpenApiHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(mcpHost.Url);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/script/stream", new { code = "return 1;" });

        Assert.Equal(System.Net.HttpStatusCode.NotFound, response.StatusCode);
        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.DoesNotContain("ason_stream_script", tools);
        Assert.DoesNotContain("ason_execute_script", tools);
    }

    [Fact]
    public async Task A_relay_reports_the_applications_logs_rather_than_its_own() {
        // The application owns the executor and the logs; the relay only has a transport.
        await using var runtime = new AsonBridgeRuntime(OptionsWith(ExecutorThatLogs("from-the-application")));
        await using var grpcHost = await BridgeGrpcHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        await using var grpc = GrpcAsonBridgeClient.Connect(grpcHost.Url);
        var mcp = await McpAsonBridgeClient.ConnectAsync(mcpHost.Url);

        var overGrpc = await new GrpcAsonBridgeEndpoint(grpc).ExecuteScriptWithLogsAsync("return 42;");
        var overMcp = await new McpAsonBridgeEndpoint(mcp).ExecuteScriptWithLogsAsync("return 42;");

        Assert.True(overGrpc.Result.Success, overGrpc.Result.Error);
        Assert.Equal("from-the-application", Assert.Single(overGrpc.Logs).Message);
        Assert.Equal(42, overGrpc.Result.Result!.Value.GetInt32());
        Assert.True(overMcp.Result.Success, overMcp.Result.Error);
        Assert.Equal("from-the-application", Assert.Single(overMcp.Logs).Message);
        Assert.Equal(42, overMcp.Result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task A_relay_still_answers_when_the_remote_cannot_stream() {
        var options = OptionsWith(ExecutorThatLogs("x"), logStream: false);
        await using var runtime = new AsonBridgeRuntime(options);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        var mcp = await McpAsonBridgeClient.ConnectAsync(mcpHost.Url);

        var streamed = await new McpAsonBridgeEndpoint(mcp).ExecuteScriptWithLogsAsync("return 42;");

        Assert.True(streamed.Result.Success, streamed.Result.Error);
        Assert.Empty(streamed.Logs);
        Assert.Equal(42, streamed.Result.Result!.Value.GetInt32());
    }

    /// <summary>Minimal server-sent-events reader: enough to assert the wire shape, not a general parser.</summary>
    static class Sse {
        internal sealed record Event(string Name, string Data);

        internal static List<Event> Parse(string body) {
            var events = new List<Event>();
            foreach (var block in body.Replace("\r\n", "\n").Split("\n\n", StringSplitOptions.RemoveEmptyEntries)) {
                string? name = null;
                var data = new List<string>();
                foreach (var line in block.Split('\n')) {
                    if (line.StartsWith("event:", StringComparison.Ordinal)) name = line["event:".Length..].Trim();
                    else if (line.StartsWith("data:", StringComparison.Ordinal)) data.Add(line["data:".Length..].TrimStart());
                }
                if (name is not null) events.Add(new Event(name, string.Join("\n", data)));
            }
            return events;
        }
    }
}
