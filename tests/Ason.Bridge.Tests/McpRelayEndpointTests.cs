using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The MCP-backed forwarding endpoint: it lets a process reach an application that publishes MCP, and it is
/// what makes the stdio relay possible without gRPC. Same contract as the gRPC one, so adapters do not care
/// which they were given.
/// </summary>
public class McpRelayEndpointTests {

    [Fact]
    public async Task A_forwarding_endpoint_reports_the_application_capabilities_and_instances() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        var endpoint = new McpAsonBridgeEndpoint(client);

        var manifest = await endpoint.GetManifestAsync();

        Assert.Equal("Bridge test app", manifest.AppName);
        Assert.Equal("Bridge test app", endpoint.Options.AppName);
        Assert.True(endpoint.Options.Capabilities.ExecuteScript);
        Assert.Equal(AsonBridgeExecution.InProcess, endpoint.Options.Execution);
        var instance = Assert.Single(await endpoint.ListInstancesAsync());
        Assert.Equal("BridgeCalculatorOperator", instance.Handle);
    }

    [Fact]
    public async Task A_forwarding_endpoint_relays_both_execution_interfaces() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        var endpoint = new McpAsonBridgeEndpoint(client);
        await endpoint.GetManifestAsync();

        var function = await endpoint.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 20, 22));
        var script = await endpoint.ExecuteScriptAsync("return BridgeStaticOperator.Add(40, 2);");

        Assert.True(function.Success, function.Error);
        Assert.Equal(42, function.Result!.Value.GetInt32());
        Assert.True(script.Success, script.Error);
        Assert.Equal(42, script.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task Mcp_pass_through_is_reported_as_unsupported_by_the_forwarding_endpoint() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        var endpoint = new McpAsonBridgeEndpoint(client);

        var result = await endpoint.InvokeMcpToolAsync("server", "tool", new Dictionary<string, System.Text.Json.JsonElement>());

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
    }
}
