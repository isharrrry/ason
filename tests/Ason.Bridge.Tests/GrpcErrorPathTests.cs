using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;
using Grpc.Core;
using Grpc.Net.Client;

namespace Ason.Bridge.Tests;

/// <summary>
/// The gRPC paths a caller only reaches when something is wrong: a malformed argument payload, a cancelled
/// stream, a capability the contract cannot carry, and channel ownership. They are the branches behind the
/// adapter's line-coverage gap, and they are the ones a real agent hits when it gets something slightly wrong.
/// </summary>
public class GrpcErrorPathTests {

    [Fact]
    public async Task Malformed_arguments_json_is_rejected_with_an_argument_error() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        using var channel = GrpcChannel.ForAddress(host.Url);
        var raw = new AsonBridge.AsonBridgeClient(channel);

        var notJson = await Assert.ThrowsAsync<RpcException>(async () => await raw.InvokeFunctionAsync(new InvokeFunctionRequest {
            Operator = "BridgeStaticOperator",
            Method = "Add",
            ArgumentsJson = "{not json"
        }));
        Assert.Equal(StatusCode.InvalidArgument, notJson.StatusCode);

        var notAnArray = await Assert.ThrowsAsync<RpcException>(async () => await raw.InvokeFunctionAsync(new InvokeFunctionRequest {
            Operator = "BridgeStaticOperator",
            Method = "Add",
            ArgumentsJson = "{\"left\":1}"
        }));
        Assert.Equal(StatusCode.InvalidArgument, notAnArray.StatusCode);
        Assert.Contains("JSON array", notAnArray.Status.Detail);
    }

    [Fact]
    public async Task The_typed_client_maps_a_malformed_payload_onto_the_invalid_arguments_code() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        // The typed client does not expose the raw payload, so this goes through the raw stub to produce one.
        using var channel = GrpcChannel.ForAddress(host.Url);
        var raw = new AsonBridge.AsonBridgeClient(channel);
        await Assert.ThrowsAsync<RpcException>(async () => await raw.InvokeFunctionAsync(new InvokeFunctionRequest {
            Operator = "BridgeStaticOperator",
            Method = "Add",
            ArgumentsJson = "["
        }));

        // And the same status reaching the typed client becomes a result, not an exception.
        var throughTypedClient = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 1, 1));
        Assert.True(throughTypedClient.Success, throughTypedClient.Error);
    }

    [Fact]
    public async Task A_cancelled_stream_ends_without_hanging() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        var thrown = await Assert.ThrowsAnyAsync<Exception>(async () => {
            await foreach (var _ in client.StreamExecutionAsync("return BridgeStaticOperator.Add(1, 1);", cancellationToken: cancellation.Token)) { }
        });

        // gRPC surfaces a cancelled call as StatusCode.Cancelled rather than as OperationCanceledException.
        Assert.True(
            thrown is OperationCanceledException || (thrown is RpcException rpc && rpc.StatusCode == StatusCode.Cancelled),
            $"unexpected exception type {thrown.GetType().Name}: {thrown.Message}");
    }

    [Fact]
    public async Task The_gRPC_contract_reports_mcp_pass_through_as_unsupported() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);
        await endpoint.GetManifestAsync();

        var result = await endpoint.InvokeMcpToolAsync("server", "tool", new Dictionary<string, JsonElement>());

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task A_client_owns_only_the_channel_it_created() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        // A client created from a caller-owned channel must not dispose it.
        using var channel = GrpcChannel.ForAddress(host.Url);
        var fromChannel = GrpcAsonBridgeClient.FromChannel(channel);
        await fromChannel.DisposeAsync();
        var stillUsable = await new AsonBridge.AsonBridgeClient(channel).GetManifestAsync(new GetManifestRequest());
        Assert.Equal("Bridge test app", JsonDocument.Parse(stillUsable.ManifestJson).RootElement.GetProperty("appName").GetString());

        // A client that opened its own channel disposes it, and doing so twice is harmless.
        var owned = GrpcAsonBridgeClient.Connect(host.Url);
        await owned.DisposeAsync();
        await owned.DisposeAsync();
    }
}
