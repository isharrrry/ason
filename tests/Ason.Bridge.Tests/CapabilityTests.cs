using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Capabilities are composable: a host may expose the API list, the whole-script interface, the
/// single-function interface or any combination of them. A request for a capability that is switched off
/// must fail with a dedicated code and must never reach the executor.
/// </summary>
public class CapabilityTests {

    [Fact]
    public async Task ExecuteScript_is_refused_when_the_capability_is_off() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = true, ExecuteScript = false };
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("return 1;", includeProxyPreamble: false);

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.Empty(fake.ExecutedScripts);
    }

    [Fact]
    public async Task InvokeFunction_is_refused_when_the_capability_is_off() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false, ExecuteScript = true };
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 1, 1));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
        Assert.Empty(fake.InvokedFunctions);
    }

    [Fact]
    public async Task InvokeMcpTool_is_refused_when_the_capability_is_off() {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeMcpTool = false };
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.InvokeMcpToolAsync("someServer", "someTool", new Dictionary<string, System.Text.Json.JsonElement>());

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, result.ErrorCode);
    }

    [Fact]
    public async Task Both_interfaces_can_be_enabled_together_without_conflicting() {
        using var calculator = new BridgeCalculatorOperator();
        await using var runtime = BridgeTestApp.CreateRuntime(out _, calculator);

        var scriptResult = await runtime.ExecuteScriptAsync("return bridgeCalculatorOperator.Add(1, 1);");
        var functionResult = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 1, 1));

        Assert.True(scriptResult.Success, scriptResult.Error);
        Assert.True(functionResult.Success, functionResult.Error);
        Assert.Equal(2, scriptResult.Result!.Value.GetInt32());
        Assert.Equal(2, functionResult.Result!.Value.GetInt32());
    }
}
