using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The remote script host, in a split deployment and across process boundaries: the application publishes a
/// bridge, its child executor runs on a separate runner host, and operator calls still resolve inside the
/// application. The manifest has to say so, because a caller that cannot see where scripts run cannot reason
/// about the deployment at all.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class RemoteRunnerBridgeEndToEndTests {

    [RequiresRemoteRunnerFact]
    public async Task The_application_reports_the_remote_runner_and_still_resolves_operators_itself() {
        using var runner = await RemoteRunnerHost.StartAsync();
        using var application = await ConsoleBridgeHost.StartAsync("remote", runner.Url);
        await using var client = GrpcAsonBridgeClient.Connect(application.GrpcUrl);

        // 1. The manifest says where scripts are evaluated, and the value is the remote runner's.
        var manifest = await client.GetManifestAsync();
        Assert.Equal("remote-runner", manifest.Execution);

        // 2. A script is evaluated - on the runner host, in a child process of it - and its operator call comes
        //    all the way back here, which is the whole point of keeping the operators on this side.
        var script = await client.ExecuteScriptAsync("return LibDemoStaticOperator.Add(40, 2);");
        Assert.True(script.Success, script.Error);
        Assert.Equal(42, script.Result!.Value.GetInt32());

        // 3. The single-function interface never leaves this process at all, and reports the same answer.
        var function = await client.InvokeFunctionAsync(BridgeCalls.Call("LibDemoStaticOperator", "Add", 20, 22));
        Assert.True(function.Success, function.Error);
        Assert.Equal(42, function.Result!.Value.GetInt32());
    }
}
