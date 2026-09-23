using System.Collections.Concurrent;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// A host has to tell the bridge which operators are alive. Views do that themselves as they load; marker-only
/// operators (a marked class with no <see cref="OperatorBase"/>) have no view, so the bridge materialises them
/// the same way <c>AsonClient</c> does for its own scripts.
/// </summary>
public class BridgeOperatorsTests {

    [Fact]
    public void Marker_only_operators_are_materialised_and_called_by_type_name() {
        var singletons = AsonBridgeOperators.MaterializeMarkerOnly(BridgeTestApp.AppAssembly);

        Assert.True(singletons.ContainsKey("BridgeMarkerOperator"));
        Assert.False(singletons.ContainsKey("BridgeCalculatorOperator")); // needs a view, so not materialised
        Assert.False(singletons.ContainsKey("BridgeStaticOperator"));     // static module: addressed without an instance
    }

    [Fact]
    public async Task A_materialised_marker_operator_is_callable_and_listed_as_a_live_instance() {
        var options = BridgeTestApp.Options();
        options.SingletonOperators = AsonBridgeOperators.MaterializeMarkerOnly(BridgeTestApp.AppAssembly);
        await using var runtime = new AsonBridgeRuntime(options);

        var manifest = await runtime.GetManifestAsync();
        var instance = Assert.Single(manifest.Instances);
        Assert.Equal("BridgeMarkerOperator", instance.Handle);

        var result = await runtime.InvokeFunctionAsync(BridgeCalls.Call("BridgeMarkerOperator", "Marker"));

        Assert.True(result.Success, result.Error);
        Assert.Equal("marker", result.Result!.Value.GetString());
    }

    [Fact]
    public void Materialising_no_assembly_yields_no_instances() {
        var singletons = AsonBridgeOperators.MaterializeMarkerOnly();

        Assert.Empty(singletons);
        Assert.IsType<ConcurrentDictionary<string, object>>(singletons);
    }
}
