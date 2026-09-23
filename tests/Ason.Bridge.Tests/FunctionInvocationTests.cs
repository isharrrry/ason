using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The single-function interface: a transport can call one operator method precisely, with JSON arguments,
/// without generating a script. Static modules need no handle, instance operators are resolved through the
/// live instance directory.
/// </summary>
public class FunctionInvocationTests {

    [Fact]
    public async Task InvokeFunction_calls_a_static_module_without_a_handle() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 2, 3));

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task InvokeFunction_calls_an_instance_operator_through_the_live_instance_directory() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 2, 3));

        Assert.True(result.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task InvokeFunction_accepts_an_explicit_handle() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.CallHandled("BridgeCalculatorOperator", "Concat", "BridgeCalculatorOperator", "a", "b"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("ab", result.Result!.Value.GetString());
    }

    [Fact]
    public async Task InvokeFunction_maps_json_arguments_onto_model_parameters_and_serializes_the_result() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Echo", new { a = 7, name = "seven" }));

        Assert.True(result.Success, result.Error);
        Assert.Equal(7, result.Result!.Value.GetProperty("a").GetInt32());
        Assert.Equal("seven", result.Result!.Value.GetProperty("name").GetString());
    }

    [Fact]
    public async Task InvokeFunction_reports_an_unknown_operator() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("NoSuchOperator", "Do"));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.OperatorNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task InvokeFunction_reports_that_an_instance_operator_needs_a_handle_when_none_is_live() {
        await using var runtime = BridgeTestApp.CreateRuntime(BridgeTestApp.NewRoot());

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 1, 2));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.HandleRequired, result.ErrorCode);
        Assert.Contains("BridgeCalculatorOperator", result.Error);
    }

    [Fact]
    public async Task InvokeFunction_reports_an_ambiguous_handle_when_two_instances_share_a_type() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root, "2");
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "Add", 1, 2));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.HandleAmbiguous, result.ErrorCode);
        Assert.Contains("BridgeCalculatorOperator2", result.Error);
    }

    [Fact]
    public async Task InvokeFunction_reports_a_missing_method() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeCalculatorOperator", "NoSuchMethod"));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.MethodNotFound, result.ErrorCode);
    }

    [Fact]
    public async Task InvokeFunction_reports_an_operator_that_threw() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "AlwaysFails"));

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.ExecutionFailed, result.ErrorCode);
        Assert.Contains("operator failed on purpose", result.Error);
    }
}
