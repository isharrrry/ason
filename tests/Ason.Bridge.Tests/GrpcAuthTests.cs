using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;
using Grpc.Core;
using Grpc.Net.Client;

namespace Ason.Bridge.Tests;

/// <summary>
/// Authorization on the gRPC adapter: the application names a policy, the endpoint requires it, and an
/// unauthorized caller must see <see cref="StatusCode.Unauthenticated"/> - never <c>Unimplemented</c>, which
/// every adapter uses to mean "this capability is switched off".
/// </summary>
public class GrpcAuthTests {

    [Fact]
    public async Task An_unauthorized_caller_is_rejected_as_unauthenticated_not_as_unimplemented() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy);

        using var channel = GrpcChannel.ForAddress(host.Url);
        var raw = new AsonBridge.AsonBridgeClient(channel);

        var rejection = await Assert.ThrowsAsync<RpcException>(async () => await raw.GetManifestAsync(new GetManifestRequest()));

        Assert.Equal(StatusCode.Unauthenticated, rejection.StatusCode);
        Assert.NotEqual(StatusCode.Unimplemented, rejection.StatusCode);
    }

    [Fact]
    public async Task The_typed_client_sends_the_headers_it_was_given() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy);

        await using var client = GrpcAsonBridgeClient.Connect(host.Url, TestAuth.Authorized);

        var manifest = await client.GetManifestAsync();
        Assert.Equal("Bridge test app", manifest.AppName);

        // Application-level results are unaffected by authorization being on.
        var call = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 1, 1));
        Assert.True(call.Success, call.Error);
        Assert.Equal(2, call.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task Authorization_failures_propagate_as_a_status_instead_of_becoming_a_result() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy);

        // No headers: an application-level failure would be a result, a credentials problem must not be.
        await using var anonymous = GrpcAsonBridgeClient.Connect(host.Url);

        var rejection = await Assert.ThrowsAsync<RpcException>(async () => await anonymous.GetManifestAsync());

        Assert.Equal(StatusCode.Unauthenticated, rejection.StatusCode);
    }

    [Fact]
    public async Task Without_a_policy_the_endpoint_stays_open() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var manifest = await client.GetManifestAsync();
        Assert.Equal("Bridge test app", manifest.AppName);
    }
}
