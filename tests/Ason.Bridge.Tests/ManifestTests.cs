using System.Text.Json;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The manifest is the single contract between the application and an agent: it must describe the operator
/// API (the same data the script prompt is built from), the live instances, the effective capabilities and
/// the execution location - and it must survive a JSON round trip, because that is how it crosses a boundary.
/// </summary>
public class ManifestTests {

    [Fact]
    public async Task Manifest_describes_every_marked_operator_and_model() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var manifest = await runtime.GetManifestAsync();

        var names = manifest.Api.Operators.Select(o => o.TypeName).ToList();
        Assert.Contains("BridgeCalculatorOperator", names);
        Assert.Contains("BridgeStaticOperator", names);

        var staticModule = manifest.Api.Operators.Single(o => o.TypeName == "BridgeStaticOperator");
        Assert.True(staticModule.IsStatic);
        Assert.Contains(staticModule.Methods, m => m.Name == "Add");

        var instanceOperator = manifest.Api.Operators.Single(o => o.TypeName == "BridgeCalculatorOperator");
        Assert.False(instanceOperator.IsStatic);
        var add = instanceOperator.Methods.Single(m => m.Name == "Add");
        Assert.Equal("int", add.ReturnType);
        Assert.Equal(new[] { "int", "int" }, add.Parameters.Select(p => p.Type));
        Assert.Equal(new[] { "left", "right" }, add.Parameters.Select(p => p.Name));

        Assert.Contains(manifest.Api.Models, m => m.Name == "BridgeTestModel");
        Assert.True(manifest.Api.MethodCount >= 4);
    }

    [Fact]
    public async Task Manifest_carries_the_script_prompt_layer() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());

        var manifest = await runtime.GetManifestAsync();

        Assert.False(string.IsNullOrWhiteSpace(manifest.Proxies));
        Assert.False(string.IsNullOrWhiteSpace(manifest.Signatures));
        Assert.Contains("BridgeStaticOperator", manifest.Proxies);
        Assert.Contains("BridgeStaticOperator", manifest.Signatures);
        Assert.Contains("BridgeCalculatorOperator", manifest.Proxies);
        Assert.Contains("BridgeTestModel", manifest.Markdown);
    }

    [Fact]
    public async Task Manifest_reports_the_effective_capabilities_and_the_execution_location() {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false, InvokeMcpTool = false };
        await using var runtime = new AsonBridgeRuntime(options);

        var manifest = await runtime.GetManifestAsync();

        Assert.True(manifest.Capabilities.ListApis);
        Assert.True(manifest.Capabilities.ExecuteScript);
        Assert.False(manifest.Capabilities.InvokeFunction);
        Assert.False(manifest.Capabilities.InvokeMcpTool);
        Assert.Equal("in-process", manifest.Execution);
        Assert.Equal(AsonBridgeProtocol.Version, manifest.ProtocolVersion);
        Assert.Equal("Bridge test app", manifest.AppName);
    }

    [Fact]
    public async Task Manifest_lists_live_instances_and_declares_them_to_scripts() {
        using var calculator = new BridgeCalculatorOperator();
        await using var runtime = BridgeTestApp.CreateRuntime(out _, calculator);

        var manifest = await runtime.GetManifestAsync();

        var instance = Assert.Single(manifest.Instances);
        Assert.Equal("BridgeCalculatorOperator", instance.Handle);
        Assert.Equal("BridgeCalculatorOperator", instance.TypeName);
        Assert.True(instance.Initialized);

        // The generated script layer declares a variable for every live instance, so a script can call
        // bridgeCalculatorOperator.Add(...) without knowing the handle.
        Assert.Contains("bridgeCalculatorOperator", manifest.Proxies);
    }

    [Fact]
    public async Task Manifest_round_trips_through_json_because_it_crosses_a_process_boundary() {
        await using var runtime = BridgeTestApp.CreateRuntime(out _, new BridgeCalculatorOperator());
        var manifest = await runtime.GetManifestAsync();
        var json = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        var restored = JsonSerializer.Deserialize<AsonBridgeManifest>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(restored);
        Assert.Equal(manifest.ProtocolVersion, restored!.ProtocolVersion);
        Assert.Equal(manifest.AppName, restored.AppName);
        Assert.Equal(manifest.Execution, restored.Execution);
        Assert.Equal(manifest.Markdown, restored.Markdown);
        Assert.Equal(manifest.Capabilities, restored.Capabilities);
        Assert.Equal(manifest.Api.Operators.Count, restored.Api.Operators.Count);
        Assert.Equal(manifest.Api.Models.Count, restored.Api.Models.Count);
        Assert.Equal(manifest.Instances.Count, restored.Instances.Count);
        Assert.Equal(manifest.Proxies, restored.Proxies);
        Assert.Equal(manifest.Signatures, restored.Signatures);
    }
}
