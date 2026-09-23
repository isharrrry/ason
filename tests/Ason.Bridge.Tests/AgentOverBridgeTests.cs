using System.Reflection;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Ason.CodeGen;
using Ason.Invocation;
using Microsoft.SemanticKernel.ChatCompletion;
using AsonRunner;
using ModelContextProtocol.Client;

namespace Ason.Bridge.Tests;

/// <summary>
/// The point of the whole design: an agent keeps ASON's orchestration and the application keeps the
/// operators. The agent reads the manifest, builds its operator library from it, and points its runner at the
/// application - so the script is generated here and executed there, with no operator on this side.
/// </summary>
public class AgentOverBridgeTests {

    [Fact]
    public async Task An_agent_runs_a_script_inside_the_application_without_owning_any_operator() {
        // --- application side: operators only, no model ---
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        // --- agent side: no operators, no handle map, just the manifest ---
        await using var bridgeClient = GrpcAsonBridgeClient.Connect(host.Url);
        var manifest = await bridgeClient.GetManifestAsync();

        // One call turns the application's manifest into the library the client works against: the proxy layer
        // and signatures the application published, and no local operators at all.
        var library = manifest.ToOperatorsLibrary();

        // The script a script-agent would produce: a body, written against the proxy layer it was shown.
        var body = "return bridgeCalculatorOperator.Add(20, 22);";
        var chat = new ScriptedChatCompletionService(body);

        var agent = new AsonClient(chat, new RootOperator(new object()), library, new AsonClientOptions {
            ExecutionMode = ExecutionMode.ExternalProcess,
            TransportFactory = () => new GrpcAsonBridgeTransport(bridgeClient),
            ForbiddenScriptKeywords = Array.Empty<string>(),
            SkipReceptionAgent = true,
            SkipExplainerAgent = true
        });

        var reply = await agent.SendAsync("add 20 and 22 using the application");

        // Without the explainer the reply is the raw result of the script - the value the application
        // produced and sent back over the bridge.
        Assert.Equal("42", reply);
        // The prompt the script agent received is the application's API, fetched over the wire.
        Assert.Contains("bridgeCalculatorOperator", chat.Received[0]);
        Assert.Contains("BridgeCalculatorOperator", chat.Received[0]);
        // And the script really ran on the other side.
        var verification = await runtime.ExecuteScriptAsync("return bridgeCalculatorOperator.Add(1, 1);");
        Assert.True(verification.Success, verification.Error);
        Assert.Equal(2, verification.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task The_agent_reports_a_failure_of_the_application_as_a_failed_task() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var bridgeClient = GrpcAsonBridgeClient.Connect(host.Url);
        var manifest = await bridgeClient.GetManifestAsync();

        var library = manifest.ToOperatorsLibrary();

        // The script compiles and runs in the application, where it throws: the failure must travel back and
        // be reported as a failed task, with the application's own error text.
        var chat = new ScriptedChatCompletionService("BridgeStaticOperator.AlwaysFails();");
        var agent = new AsonClient(chat, new RootOperator(new object()), library, new AsonClientOptions {
            ExecutionMode = ExecutionMode.ExternalProcess,
            TransportFactory = () => new GrpcAsonBridgeTransport(bridgeClient),
            ForbiddenScriptKeywords = Array.Empty<string>(),
            SkipReceptionAgent = true,
            SkipExplainerAgent = true,
            MaxFixAttempts = 0
        });

        var reply = await agent.SendAsync("make the application fail");

        Assert.Contains("operator failed on purpose", reply);
    }
}
