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
        var context = new SingleThreadSynchronizationContext();
        try {
            var probe = new ThreadProbeOperator();
            var options = BridgeTestApp.Options();
            options.SynchronizationContext = context;
            options.OperatorInstances = BridgeTestApp.HandlesWith(probe);
            await using var runtime = new AsonBridgeRuntime(options);

            var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("ThreadProbeOperator", "ProbeThread"));

            Assert.True(result.Success, result.Error);
            Assert.Equal(context.ThreadId, result.Result!.Value.GetInt32());
            Assert.NotEqual(Environment.CurrentManagedThreadId, context.ThreadId);
            Assert.Equal(context.ThreadId, probe.LastThreadId);
        }
        finally {
            context.Dispose();
        }
    }

    [Fact]
    public async Task ExecuteScript_marshals_the_operator_call_to_the_captured_context() {
        var context = new SingleThreadSynchronizationContext();
        try {
            var probe = new ThreadProbeOperator();
            var options = BridgeTestApp.Options();
            options.SynchronizationContext = context;
            options.OperatorInstances = BridgeTestApp.HandlesWith(probe);
            await using var runtime = new AsonBridgeRuntime(options);

            var result = await runtime.ExecuteScriptAsync("return threadProbeOperator.ProbeThread();");

            Assert.True(result.Success, result.Error);
            Assert.Equal(context.ThreadId, result.Result!.Value.GetInt32());
            Assert.Equal(context.ThreadId, probe.LastThreadId);
        }
        finally {
            context.Dispose();
        }
    }

    [Fact]
    public async Task Without_a_context_the_operator_runs_on_the_calling_threads_flow() {
        var probe = new ThreadProbeOperator();
        var options = BridgeTestApp.Options();
        options.CaptureSynchronizationContext = false;
        options.OperatorInstances = BridgeTestApp.HandlesWith(probe);
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("ThreadProbeOperator", "ProbeThread"));

        Assert.True(result.Success, result.Error);
        Assert.Equal(probe.LastThreadId, result.Result!.Value.GetInt32());
    }
}
