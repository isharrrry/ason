using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;
using Grpc.Core;
using Grpc.Net.Client;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Tests;

/// <summary>
/// The MCP pass-through capability (T3): the application can expose the MCP servers *it* consumes, so an agent
/// reaches them through the bridge instead of being configured with its own credentials. Two refusals must stay
/// distinguishable: the capability is off (the call does not exist) versus it is on but the application has no
/// MCP server configured (the call exists and answers "not supported").
/// </summary>
public class McpPassthroughTests {

    static AsonBridgeOptions OptionsWithPassthrough(IAsonExecutor? executor = null) {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeMcpTool = true };
        if (executor is not null) options.Executor = executor;
        return options;
    }

    [Fact]
    public async Task InvokeMcpTool_reports_not_supported_when_the_application_has_no_mcp_server_configured() {
        // The real executor: the capability is on, but nothing registered an MCP client with the runner.
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough());

        var result = await runtime.InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.Contains("MCP", result.Error);
    }

    [Fact]
    public async Task InvokeMcpTool_is_forwarded_when_a_server_is_configured() {
        var fake = new FakeAsonExecutor { McpServers = new[] { "stub-server" } };
        fake.McpToolResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement(new { server = "stub-server", tool = "stub-tool" }));
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough(fake));

        var result = await runtime.InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());

        Assert.True(result.Success, result.Error);
        Assert.Equal("stub-server", result.Result!.Value.GetProperty("server").GetString());
        var (server, tool) = Assert.Single(fake.InvokedMcpTools);
        Assert.Equal("stub-server", server);
        Assert.Equal("stub-tool", tool);
    }

    [Fact]
    public async Task The_gRPC_rpc_is_unimplemented_when_the_capability_is_off() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());   // InvokeMcpTool defaults to false
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        using var channel = GrpcChannel.ForAddress(host.Url);
        var raw = new AsonBridge.AsonBridgeClient(channel);

        var rejection = await Assert.ThrowsAsync<RpcException>(async () => await raw.InvokeMcpToolAsync(new InvokeMcpToolRequest {
            Server = "stub-server",
            Tool = "stub-tool"
        }));

        Assert.Equal(StatusCode.Unimplemented, rejection.StatusCode);
    }

    [Fact]
    public async Task The_gRPC_rpc_forwards_to_the_application_and_returns_its_result() {
        var fake = new FakeAsonExecutor { McpServers = new[] { "stub-server" } };
        fake.McpToolResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("from-the-application"));
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough(fake));
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var result = await client.InvokeMcpToolAsync("stub-server", "stub-tool");

        Assert.True(result.Success, result.Error);
        Assert.Equal("from-the-application", result.Result!.Value.GetString());
    }

    [Fact]
    public async Task The_MCP_tool_appears_only_when_the_capability_is_on() {
        await using var without = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var with = new AsonBridgeRuntime(OptionsWithPassthrough(new FakeAsonExecutor { McpServers = new[] { "stub-server" } }));
        await using var offHost = await BridgeMcpHost.StartAsync(without);
        await using var onHost = await BridgeMcpHost.StartAsync(with);
        var offClient = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(offHost.Url);
        var onClient = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(onHost.Url);

        var offTools = (await offClient.ListToolsAsync()).Select(t => t.Name).ToList();
        var onTools = (await onClient.ListToolsAsync()).Select(t => t.Name).ToList();

        Assert.DoesNotContain("ason_invoke_mcp_tool", offTools);
        Assert.Contains("ason_invoke_mcp_tool", onTools);
    }

    [Fact]
    public async Task The_MCP_tool_forwards_to_the_application_and_returns_its_result() {
        var fake = new FakeAsonExecutor { McpServers = new[] { "stub-server" } };
        fake.McpToolResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("from-the-application"));
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough(fake));
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(host.Url);

        var result = await client.InvokeMcpToolAsync("stub-server", "stub-tool");

        Assert.True(result.Success, result.Error);
        Assert.Equal("from-the-application", result.Result!.Value.GetString());
    }

    [Fact]
    public async Task The_MCP_tool_reports_not_supported_when_the_application_has_no_server_configured() {
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough());
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(host.Url);

        var result = await client.InvokeMcpToolAsync("stub-server", "stub-tool");

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task A_forwarding_endpoint_relays_the_pass_through_instead_of_refusing_it_itself() {
        var fake = new FakeAsonExecutor { McpServers = new[] { "stub-server" } };
        fake.McpToolResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("from-the-application"));
        await using var runtime = new AsonBridgeRuntime(OptionsWithPassthrough(fake));
        await using var grpcHost = await BridgeGrpcHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        await using var grpc = GrpcAsonBridgeClient.Connect(grpcHost.Url);
        var mcp = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(mcpHost.Url);

        var viaGrpc = await new GrpcAsonBridgeEndpoint(grpc).InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());
        var viaMcp = await new Ason.Bridge.Mcp.McpAsonBridgeEndpoint(mcp).InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());

        Assert.True(viaGrpc.Success, viaGrpc.Error);
        Assert.Equal("from-the-application", viaGrpc.Result!.Value.GetString());
        Assert.True(viaMcp.Success, viaMcp.Error);
        Assert.Equal("from-the-application", viaMcp.Result!.Value.GetString());
    }

    [Fact]
    public async Task A_forwarding_endpoint_reports_not_supported_when_the_remote_capability_is_off() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());   // capability off
        await using var grpcHost = await BridgeGrpcHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        await using var grpc = GrpcAsonBridgeClient.Connect(grpcHost.Url);
        var mcp = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(mcpHost.Url);

        var viaGrpc = await new GrpcAsonBridgeEndpoint(grpc).InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());
        var viaMcp = await new Ason.Bridge.Mcp.McpAsonBridgeEndpoint(mcp).InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());

        Assert.False(viaGrpc.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, viaGrpc.ErrorCode);
        Assert.False(viaMcp.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, viaMcp.ErrorCode);
    }
}
