using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Ason.CodeGen;

namespace Ason.Bridge.Tests;

/// <summary>
/// The MCP face of the bridge. An MCP client discovers the tools the capabilities enable, reads the manifest
/// through them and calls the two execution interfaces - the same contract the gRPC adapter serves.
/// </summary>
public class McpBridgeTests {

    [Fact]
    public async Task Tools_are_published_according_to_the_capabilities() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.Contains("ason_get_manifest", names);
        Assert.Contains("ason_get_script_api", names);
        Assert.Contains("ason_list_instances", names);
        Assert.Contains("ason_execute_script", names);
        Assert.Contains("ason_invoke_function", names);
    }

    [Fact]
    public async Task A_disabled_capability_removes_its_tool() {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false, ListApis = true };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var names = (await client.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.DoesNotContain("ason_invoke_function", names);
        Assert.Contains("ason_execute_script", names);
        Assert.Contains("ason_get_manifest", names);
    }

    [Fact]
    public async Task GetManifest_returns_the_same_contract_as_every_other_adapter() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var manifest = await client.GetManifestAsync();

        Assert.Equal(AsonBridgeProtocol.Version, manifest.ProtocolVersion);
        Assert.Equal("Bridge test app", manifest.AppName);
        Assert.Equal("in-process", manifest.Execution);
        Assert.Contains(manifest.Api.Operators, o => o.TypeName == "BridgeCalculatorOperator");
        var instance = Assert.Single(manifest.Instances);
        Assert.Equal("BridgeCalculatorOperator", instance.Handle);
    }

    [Fact]
    public async Task InvokeFunction_calls_one_operator_method_over_mcp() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 2, 3));

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task ExecuteScript_evaluates_a_script_body_over_mcp() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var result = await client.ExecuteScriptAsync("return BridgeStaticOperator.Add(20, 22);");

        Assert.True(result.Success, result.Error);
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task InvokeFunction_reports_structured_errors_over_mcp() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("NoSuchOperator", "Do"));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.OperatorNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task Script_api_tool_returns_the_prompt_layer_for_an_agent_that_writes_scripts() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var scriptApi = await client.GetScriptApiAsync();

        Assert.Contains("BridgeStaticOperator", scriptApi.Proxies);
        Assert.Contains("BridgeCalculatorOperator", scriptApi.Signatures);
    }

    [Fact]
    public async Task The_mcp_transport_carries_exec_lines_to_the_application() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var transport = new McpAsonBridgeTransport(client);
        var received = new List<string>();
        transport.LineReceived += line => received.Add(line);
        await transport.StartAsync();

        // The complete script a client builds from the manifest: the proxy layer plus the declarations for the
        // live instances it reported.
        var code = ProxySerializer.SerializeAll(typeof(BridgeStaticOperator).Assembly)
            + OperatorVariableDeclarations.Build(root.OperatorInstances)
            + "\nreturn bridgeCalculatorOperator.Add(40, 2);";
        await transport.SendAsync($"{{\"id\":\"1\",\"type\":\"exec\",\"code\":{System.Text.Json.JsonSerializer.Serialize(code)}}}");

        var reply = Assert.Single(received);
        Assert.Contains("execResult", reply);
        Assert.Contains("42", reply);
    }
}
