using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Grpc.Core;
using Grpc.Net.Client;

namespace Ason.Bridge.Tests;

/// <summary>
/// The gRPC face of the bridge, exercised the way a real client uses it: an application hosts the service,
/// a client connects over the wire, reads the manifest and calls the two execution interfaces.
/// </summary>
public class GrpcBridgeTests {

    [Fact]
    public async Task GetManifest_returns_the_same_contract_the_runtime_builds() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var manifest = await client.GetManifestAsync();

        Assert.Equal(AsonBridgeProtocol.Version, manifest.ProtocolVersion);
        Assert.Equal("in-process", manifest.Execution);
        Assert.True(manifest.Capabilities.ExecuteScript);
        Assert.Contains(manifest.Api.Operators, o => o.TypeName == "BridgeCalculatorOperator");
        Assert.False(string.IsNullOrWhiteSpace(manifest.Proxies));
        var instance = Assert.Single(manifest.Instances);
        Assert.Equal("BridgeCalculatorOperator", instance.Handle);
    }

    [Fact]
    public async Task InvokeFunction_calls_one_operator_method_over_the_wire() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 2, 3));

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task InvokeFunction_reports_structured_errors_over_the_wire() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("NoSuchOperator", "Do"));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.OperatorNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteScript_evaluates_a_script_body_over_the_wire() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var result = await client.ExecuteScriptAsync("return BridgeStaticOperator.Add(20, 22);");

        Assert.True(result.Success, result.Error);
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task A_disabled_capability_is_refused_on_the_wire_and_translated_on_the_client() {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        // A plain gRPC client sees the idiomatic signal... (note the fully qualified channel type: the
        // Ason.Bridge.Grpc namespace shadows the "Grpc" root when written inline).
        using var channel = GrpcChannel.ForAddress(host.Url);
        var raw = new AsonBridge.AsonBridgeClient(channel);
        var status = await Assert.ThrowsAsync<RpcException>(async () => await raw.InvokeFunctionAsync(new InvokeFunctionRequest { Operator = "BridgeStaticOperator", Method = "Add" }));
        Assert.Equal(StatusCode.Unimplemented, status.StatusCode);

        // ...and the typed client folds it back into the same result shape every other call returns.
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 1, 1));
        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task StreamExecution_streams_logs_and_then_the_result() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var events = new List<ExecutionEvent>();
        await foreach (var evt in client.StreamExecutionAsync("return BridgeStaticOperator.Add(1, 2);")) events.Add(evt);

        Assert.Contains(events, e => e.Type == "log");
        var last = events[^1];
        Assert.Equal("result", last.Type);
        Assert.True(last.Result.Success);
        Assert.Equal(3, System.Text.Json.JsonDocument.Parse(last.Result.Json).RootElement.GetInt32());
    }

    [Fact]
    public async Task ListInstances_reflects_the_live_directory_as_it_changes() {
        var root = BridgeTestApp.NewRoot();
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        Assert.Empty(await client.ListInstancesAsync());

        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);

        var instances = await client.ListInstancesAsync();
        var instance = Assert.Single(instances);
        Assert.Equal("BridgeCalculatorOperator", instance.Handle);
        Assert.Equal("BridgeCalculatorOperator", instance.TypeName);
    }
}
