using System.Reflection;
using Ason.Bridge.Tests.Operators;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Builds the options and the live operator directory every bridge test needs. Instances are registered
/// through <see cref="RootOperator.AttachChildOperator{TOperator}"/>, which is exactly how a real application
/// attaches its view operators.
/// </summary>
internal static class BridgeTestApp {

    public static readonly Assembly AppAssembly = typeof(BridgeStaticOperator).Assembly;

    public static RootOperator NewRoot() => new(new object());

    /// <summary>Attaches an operator to the root and returns the instance the runtime will operate on.</summary>
    public static T Attach<T>(RootOperator root, string? id = null) where T : OperatorBase, new() {
        root.AttachChildOperator<T>(new object(), id);
        return (T)root.OperatorInstances[typeof(T).Name + (id ?? string.Empty)];
    }

    public static AsonBridgeOptions Options() => new() {
        AppName = "Bridge test app",
        Assemblies = new[] { AppAssembly },
        // Tests run under xunit's own synchronization context; marshalling is exercised explicitly by
        // SynchronizationContextTests instead of being inherited implicitly.
        CaptureSynchronizationContext = false
    };

    public static AsonBridgeOptions OptionsFor(RootOperator root, Action<AsonBridgeOptions>? configure = null) {
        var options = Options();
        options.OperatorInstances = root.OperatorInstances;
        configure?.Invoke(options);
        return options;
    }

    public static AsonBridgeRuntime CreateRuntime(RootOperator root, Action<AsonBridgeOptions>? configure = null)
        => new(OptionsFor(root, configure));
}
