using System.Collections.Concurrent;
using System.Reflection;
using Ason.Bridge.Tests.Operators;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>Builds the options/handle maps every bridge test needs, mirroring how a host app wires the bridge.</summary>
internal static class BridgeTestApp {

    public static readonly Assembly AppAssembly = typeof(BridgeStaticOperator).Assembly;

    public static AsonBridgeOptions Options() => new() {
        AppName = "Bridge test app",
        Assemblies = new[] { AppAssembly },
        // Tests run under xunit's own synchronization context; marshalling is exercised explicitly
        // by SynchronizationContextTests instead of being inherited implicitly.
        CaptureSynchronizationContext = false
    };

    public static ConcurrentDictionary<string, OperatorBase> HandlesWith(params OperatorBase[] operators) {
        var map = new ConcurrentDictionary<string, OperatorBase>(StringComparer.Ordinal);
        foreach (var op in operators) {
            var handle = op.GetType().Name;
            map[handle] = op;
        }
        return map;
    }

    public static AsonBridgeRuntime CreateRuntime(out ConcurrentDictionary<string, OperatorBase> handles, params OperatorBase[] operators) {
        handles = HandlesWith(operators);
        var options = Options();
        options.OperatorInstances = handles;
        return new AsonBridgeRuntime(options);
    }
}
