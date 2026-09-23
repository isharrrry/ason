using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Operators touch UI-bound state, so every operator call must be marshalled to the synchronization context
/// captured when the runtime was built (in WPF: the dispatcher thread) - for the whole-script interface and
/// for the single-function interface alike.
/// </summary>
public class SynchronizationContextTests {

    [Fact]
    public async Task InvokeFunction_marshals_the_operator_call_to_the_captured_context() {
        using var context = new SingleThreadSynchronizationContext();
        var root = BridgeTestApp.NewRoot();
        var probe = BridgeTestApp.Attach<ThreadProbeOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.SynchronizationContext = context;
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("ThreadProbeOperator", "ProbeThread"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(context.ThreadId, result.Result!.Value.GetInt32());
        Assert.NotEqual(Environment.CurrentManagedThreadId, context.ThreadId);
        Assert.Equal(context.ThreadId, probe.LastThreadId);
    }

    [Fact]
    public async Task ExecuteScript_marshals_the_operator_call_to_the_captured_context() {
        using var context = new SingleThreadSynchronizationContext();
        var root = BridgeTestApp.NewRoot();
        var probe = BridgeTestApp.Attach<ThreadProbeOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.SynchronizationContext = context;
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("return threadProbeOperator.ProbeThread();");

        Assert.True(result.Success, result.Error);
        Assert.Equal(context.ThreadId, result.Result!.Value.GetInt32());
        Assert.Equal(context.ThreadId, probe.LastThreadId);
    }

    [Fact]
    public async Task Without_a_context_the_operator_runs_inline_on_the_caller() {
        var root = BridgeTestApp.NewRoot();
        var probe = BridgeTestApp.Attach<ThreadProbeOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.CaptureSynchronizationContext = false;
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("ThreadProbeOperator", "ProbeThread"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(probe.LastThreadId, result.Result!.Value.GetInt32());
    }
}
