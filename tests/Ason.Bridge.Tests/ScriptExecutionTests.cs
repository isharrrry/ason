using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The whole-script interface: the bridge evaluates generated code in the configured executor and returns
/// whatever the script returned. This is the interface an ASON agent uses.
/// </summary>
public class ScriptExecutionTests {

    [Fact]
    public async Task ExecuteScript_runs_a_script_against_the_generated_proxy_layer() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var result = await runtime.ExecuteScriptAsync("return BridgeStaticOperator.Add(2, 3);");

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());
        Assert.Null(result.ErrorCode);
    }

    [Fact]
    public async Task ExecuteScript_can_call_a_live_instance_through_its_declared_variable() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.ExecuteScriptAsync("return bridgeCalculatorOperator.Add(4, 5);");

        Assert.True(result.Success, result.Error);
        Assert.Equal(9, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task ExecuteScript_appends_the_preamble_only_when_asked_to() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var withPreamble = await runtime.ExecuteScriptAsync("return BridgeStaticOperator.Add(1, 1);");
        var withoutPreamble = await runtime.ExecuteScriptAsync("BridgeStaticOperator.Add(1, 1);", includeProxyPreamble: false);

        Assert.True(withPreamble.Success, withPreamble.Error);
        Assert.False(withoutPreamble.Success);
        Assert.Equal(AsonBridgeErrorCodes.ExecutionFailed, withoutPreamble.ErrorCode);
    }

    [Fact]
    public async Task ExecuteScript_reports_an_operator_failure_as_a_failed_result() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        // AlwaysFails returns void, so the script calls it as a statement.
        var result = await runtime.ExecuteScriptAsync("BridgeStaticOperator.AlwaysFails();");

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.ExecutionFailed, result.ErrorCode);
        Assert.Contains("operator failed on purpose", result.Error);
    }

    [Fact]
    public async Task ExecuteScript_forwards_the_code_to_a_custom_executor() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("return 1;", includeProxyPreamble: false);

        Assert.True(result.Success, result.Error);
        var code = Assert.Single(fake.ExecutedScripts);
        Assert.Equal("return 1;", code);
        Assert.Equal(1, fake.StartCalls);
    }
}
