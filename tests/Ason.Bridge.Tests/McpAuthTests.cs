using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Authorization on the MCP adapter: an application can require an authenticated caller, and an MCP client
/// proves who it is with headers of its own.
/// </summary>
public class McpAuthTests {

    [Fact]
    public async Task Mcp_serves_an_authenticated_caller_when_authorization_is_required() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime, requireAuthorization: true);

        var client = await McpAsonBridgeClient.ConnectAsync(host.Url, TestAuth.Authorized);
        await using var disposable = client;

        var manifest = await client.GetManifestAsync();
        Assert.Equal("Bridge test app", manifest.AppName);

        var call = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 20, 22));
        Assert.True(call.Success, call.Error);
        Assert.Equal(42, call.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task Mcp_rejects_an_anonymous_caller_when_authorization_is_required() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime, requireAuthorization: true);

        // The handshake itself is refused, so connecting is what fails - there is no session to read a manifest
        // from, which is the point: an unauthorized caller never reaches the tool surface.
        await Assert.ThrowsAnyAsync<Exception>(async () => await McpAsonBridgeClient.ConnectAsync(host.Url));
    }

    [Fact]
    public async Task Mcp_stays_open_without_authorization() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeMcpHost.StartAsync(runtime);

        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);
        await using var disposable = client;

        var manifest = await client.GetManifestAsync();
        Assert.Equal("Bridge test app", manifest.AppName);
    }
}
