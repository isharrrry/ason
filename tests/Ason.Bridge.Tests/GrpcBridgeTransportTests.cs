using System.Collections.Concurrent;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using AsonRunner;

namespace Ason.Bridge.Tests;

/// <summary>
/// The other half of the bridge: an agent whose runner transport is the application's gRPC endpoint. The
/// agent keeps the whole ASON pipeline and the script simply runs in the application.
/// </summary>
public class GrpcBridgeTransportTests {

    [Fact]
    public async Task Exec_lines_travel_to_the_application_and_results_come_back() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var agent = new RunnerClient(new ConcurrentDictionary<string, OperatorBase>(StringComparer.Ordinal), null) { Mode = ExecutionMode.ExternalProcess };
        agent.UseTransport(() => new GrpcAsonBridgeTransport(client));

        // Exactly the shape AsonClient produces: the generated proxy layer followed by the script body.
        var completeScript = ProxySerializer.SerializeAll(typeof(BridgeStaticOperator).Assembly) + "\nreturn BridgeStaticOperator.Add(20, 22);";
        var result = await agent.ExecuteAsync(completeScript);

        Assert.Equal(42, result!.Value.GetInt32());
    }

    [Fact]
    public async Task An_invoke_request_is_answered_with_an_error_instead_of_hanging() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var transport = new GrpcAsonBridgeTransport(client);
        var received = new List<string>();
        transport.LineReceived += line => received.Add(line);
        await transport.StartAsync();

        // The application resolves operator calls inside its own process, so an invoke coming back over this
        // transport means the deployment is wrong - the caller must learn that instead of waiting forever.
        await transport.SendAsync("{\"id\":\"1\",\"type\":\"invoke\",\"target\":\"BridgeStaticOperator\",\"method\":\"Add\",\"args\":[1,2]}");

        var reply = Assert.Single(received);
        Assert.Contains("invokeResult", reply);
        Assert.Contains("error", reply);
    }

    [Fact]
    public async Task Execution_logs_from_the_application_are_relayed_to_the_agent() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var transport = new GrpcAsonBridgeTransport(client) { RelayLogs = true };
        var logs = new List<string>();
        var results = new List<string>();
        transport.LineReceived += line => (line.Contains("\"log\"") ? logs : results).Add(line);

        await transport.StartAsync();
        await transport.SendAsync("{\"id\":\"7\",\"type\":\"exec\",\"code\":\"return 1;\"}");

        Assert.Single(results);
        Assert.NotEmpty(logs);
    }
}
