using Ason.Bridge;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Chaining a bridge through another process: a relay has no operators of its own, so it forwards to the
/// application's gRPC bridge and republishes whatever that application enabled. This is the path the stdio
/// MCP relay host uses, exercised without spawning a process.
/// </summary>
public class RelayEndpointTests {

    [Fact]
    public async Task A_forwarding_endpoint_reports_the_application_capabilities_and_instances() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);

        var manifest = await endpoint.GetManifestAsync();

        Assert.Equal("Bridge test app", manifest.AppName);
        Assert.Equal("Bridge test app", endpoint.Options.AppName);
        Assert.False(endpoint.Options.Capabilities.InvokeFunction);
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
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);
        await endpoint.GetManifestAsync();

        var function = await endpoint.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 20, 22));
        var script = await endpoint.ExecuteScriptAsync("return BridgeStaticOperator.Add(40, 2);");

        Assert.True(function.Success, function.Error);
        Assert.Equal(42, function.Result!.Value.GetInt32());
        Assert.True(script.Success, script.Error);
        Assert.Equal(42, script.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task The_mcp_tool_surface_is_built_from_the_forwarding_endpoint() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var grpc = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(grpc);
        await endpoint.GetManifestAsync();

        // What the stdio relay host publishes: the same tools, derived from the remote capabilities.
        var tools = Ason.Bridge.Mcp.AsonBridgeMcpTools.Create(endpoint);

        Assert.Contains(tools, t => t.ProtocolTool.Name == Ason.Bridge.Mcp.AsonBridgeMcpTools.InvokeFunction);
        Assert.Contains(tools, t => t.ProtocolTool.Name == Ason.Bridge.Mcp.AsonBridgeMcpTools.ExecuteScript);
    }
}
