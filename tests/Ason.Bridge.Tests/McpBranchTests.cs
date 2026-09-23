using System.Text.Json;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Microsoft.Extensions.DependencyInjection;

namespace Ason.Bridge.Tests;

/// <summary>
/// The MCP adapter's remaining branches: the runner-protocol transport used the way the agent library uses it,
/// the forwarding endpoint in full, and the small decisions a client makes when a tool answer is missing,
/// malformed or already an error.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class McpBranchTests {

    static AsonBridgeRuntime Runtime(Action<AsonBridgeOptions>? configure = null) {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        return BridgeTestApp.CreateRuntime(root, configure);
    }

    [Fact]
    public async Task The_MCP_runner_transport_answers_messages_that_must_not_travel_this_way() {
        await using var runtime = Runtime();
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        await using var transport = new Probe(new McpAsonBridgeTransport(client));

        Assert.False(transport.Started);
        await transport.Start();
        Assert.True(transport.Started);
        await transport.Stop();
        Assert.False(transport.Started);

        foreach (var type in new[] { "invoke", "invokeMcp" }) {
            await transport.Send(JsonSerializer.Serialize(new { type, handle = "x" }));
        }

        Assert.Equal(2, transport.Lines.Count);
        foreach (var line in transport.Lines) {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("invokeResult", document.RootElement.GetProperty("type").GetString());
            Assert.Contains("operators are resolved inside the application process", document.RootElement.GetProperty("error").GetString());
        }
    }

    [Fact]
    public async Task The_MCP_runner_transport_reports_a_failure_and_keeps_an_unmatched_snapshot_layer() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.Executor = new FakeAsonExecutor { ScriptResult = AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, "the script failed") };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        // Proxies is set but the code does not start with it, so the layer travels exactly as it was composed.
        await using var transport = new Probe(new McpAsonBridgeTransport(client) { Proxies = "something else entirely" });

        await transport.Send(JsonSerializer.Serialize(new { id = "7", type = "exec", code = "return 1;" }));

        using var document = JsonDocument.Parse(Assert.Single(transport.Lines));
        Assert.Equal("execResult", document.RootElement.GetProperty("type").GetString());
        Assert.Equal("the script failed", document.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task The_MCP_forwarding_endpoint_serves_the_whole_surface() {
        await using var runtime = Runtime();
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        var endpoint = new McpAsonBridgeEndpoint(client);

        var manifest = await endpoint.GetManifestAsync();
        var instances = await endpoint.ListInstancesAsync();
        var script = await endpoint.ExecuteScriptAsync("return 1;");
        var function = await endpoint.InvokeFunctionAsync(new AsonBridgeFunctionCall("BridgeCalculatorOperator", "Add", "BridgeCalculatorOperator", new[] { JsonSerializer.SerializeToElement(1), JsonSerializer.SerializeToElement(2) }));
        var logs = await endpoint.ExecuteScriptWithLogsAsync("return 1;");

        Assert.Equal("Bridge test app", manifest.AppName);
        Assert.Same(manifest, endpoint.Manifest);
        Assert.Contains(instances, i => i.Handle == "BridgeCalculatorOperator");
        Assert.True(script.Success, script.Error);
        Assert.Equal(1, script.Result!.Value.GetInt32());
        Assert.True(function.Success, function.Error);
        Assert.Equal(3, function.Result!.Value.GetInt32());
        Assert.True(logs.Result.Success, logs.Result.Error);
    }

    [Fact]
    public async Task A_refused_call_comes_back_as_a_result_and_not_as_a_protocol_error() {
        await using var runtime = Runtime();
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        // No arguments where the method needs some: the application answers with an error code, which travels as
        // an ordinary tool result. Turning that into a protocol-level failure would lose the code.
        var result = await client.InvokeFunctionAsync(new AsonBridgeFunctionCall("BridgeCalculatorOperator", "Add", null, Array.Empty<JsonElement>()));

        Assert.False(result.Success);
        Assert.False(string.IsNullOrWhiteSpace(result.ErrorCode));
    }

    [Fact]
    public async Task A_missing_argument_makes_the_pass_through_tool_answer_rather_than_throw() {
        await using var runtime = Runtime(options => options.Capabilities = new AsonBridgeCapabilities { InvokeMcpTool = true });
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        // A non-object argumentsJson is an empty argument set, not a crash: the JSON is opaque to the bridge.
        var tool = (await client.ListToolsAsync()).Single(t => t.Name == AsonBridgeMcpTools.InvokeMcpTool);
        var result = await tool.CallAsync(new Dictionary<string, object?> { ["server"] = "stub-server", ["tool"] = "stub-tool", ["argumentsJson"] = "5" });
        var text = string.Concat(result.Content.OfType<ModelContextProtocol.Protocol.TextContentBlock>().Select(c => c.Text));

        Assert.Contains(AsonBridgeErrorCodes.NotSupported, text);
    }

    [Fact]
    public async Task The_MCP_client_rejects_what_it_cannot_call() {
        await Assert.ThrowsAsync<ArgumentException>(async () => await McpAsonBridgeClient.ConnectAsync("  "));
        Assert.Throws<ArgumentNullException>(() => McpAsonBridgeClient.FromClient(null!));

        await using var runtime = Runtime();
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        await Assert.ThrowsAsync<ArgumentNullException>(async () => await client.InvokeFunctionAsync(null!));
    }

    [Fact]
    public async Task The_stdio_bridge_registration_is_available_to_hosts() {
        var runtime = Runtime();
        var services = new ServiceCollection();

        services.AddAsonMcpStdioBridge(runtime);

        Assert.Contains(services, d => d.ServiceType == typeof(IAsonBridgeEndpoint));
        await runtime.DisposeAsync();
    }
}

/// <summary>Collects the lines a runner transport produced, and exposes its start/stop state.</summary>
internal sealed class Probe : IAsyncDisposable {

    readonly Ason.Transport.IRunnerTransport _inner;

    internal Probe(Ason.Transport.IRunnerTransport inner) {
        _inner = inner;
        _inner.LineReceived += line => Lines.Add(line);
    }

    internal List<string> Lines { get; } = new();

    internal bool Started => _inner.IsStarted;

    internal Task Start() => _inner.StartAsync();

    internal Task Stop() => _inner.StopAsync();

    internal Task Send(string line) => _inner.SendAsync(line);

    public ValueTask DisposeAsync() => _inner is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
}
