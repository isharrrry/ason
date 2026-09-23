using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The separation, end to end, against the real WPF application: the application is started headless, an
/// external client discovers its operators and calls them, and the application stays in charge of its own
/// state - including the UI thread its operators run on.
///
/// Shares a collection with the agent tests because each test starts a real process and they must not race for
/// ports.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class WpfApplicationEndToEndTests {

    [RequiresWpfApplicationFact]
    public async Task The_wpf_application_serves_its_operators_and_keeps_ui_thread_affinity() {
        using var application = await WpfApplication.StartAsync(WpfApplication.LocateExecutable()!);
        await using var client = GrpcAsonBridgeClient.Connect(application.GrpcUrl);

        // 1. What the application exposes: its own operators, a marker-only module, a static module and the
        //    operators of the LibDemo class library.
        var manifest = await client.GetManifestAsync();
        Assert.Equal("WpfAppOnlyDemo", manifest.AppName);
        var operatorNames = manifest.Api.Operators.Select(o => o.TypeName).ToList();
        Assert.Contains("EmployeesOperator", operatorNames);
        Assert.Contains("AppInfoOperator", operatorNames);
        Assert.Contains("ReportOperator", operatorNames);
        Assert.Contains("LibDemoOperator", operatorNames);

        // 2. Its live instances, i.e. the handles the single-function interface addresses.
        Assert.Contains(manifest.Instances, i => i is { Handle: "EmployeesOperator", Initialized: true });
        Assert.Contains("employeesOperator", manifest.Proxies);

        // 3. The operator that mutates the window's bound collection ran on the dispatcher thread, even though
        //    the request arrived on a Kestrel thread.
        var diagnostics = await client.InvokeFunctionAsync(BridgeCalls.Call("EmployeesOperator", "GetDiagnostics"));
        Assert.True(diagnostics.Success, diagnostics.Error);
        Assert.True(diagnostics.Result!.Value.GetProperty("onUiThread").GetBoolean());
        Assert.Equal(3, diagnostics.Result!.Value.GetProperty("employeeCount").GetInt32());

        // 4. A static module needs no handle at all.
        var sum = await client.InvokeFunctionAsync(BridgeCalls.Call("AppInfoOperator", "Add", 40, 2));
        Assert.True(sum.Success, sum.Error);
        Assert.Equal(42, sum.Result!.Value.GetInt32());

        // 5. The whole-script interface reaches the same operators.
        var count = await client.ExecuteScriptAsync("return employeesOperator.GetEmployees().Count;");
        Assert.True(count.Success, count.Error);
        Assert.Equal(3, count.Result!.Value.GetInt32());

        // 6. A function call can change application state, and a later call sees the change.
        var renamed = await client.InvokeFunctionAsync(BridgeCalls.Call("EmployeesOperator", "Rename", 2, "Grace Hopper II"));
        Assert.True(renamed.Success, renamed.Error);
        Assert.Equal("Grace Hopper II", renamed.Result!.Value.GetProperty("name").GetString());

        var employees = await client.InvokeFunctionAsync(BridgeCalls.Call("EmployeesOperator", "GetEmployees"));
        Assert.True(employees.Success, employees.Error);
        Assert.Contains(employees.Result!.Value.EnumerateArray(), e => e.GetProperty("name").GetString() == "Grace Hopper II");
    }

    [RequiresWpfApplicationFact]
    public async Task The_wpf_application_can_evaluate_scripts_in_a_child_process_and_still_keep_ui_affinity() {
        using var application = await WpfApplication.StartAsync(WpfApplication.LocateExecutable()!, execution: "external");
        await using var client = GrpcAsonBridgeClient.Connect(application.GrpcUrl);

        // 1. The application says where it evaluates, so a caller never has to guess.
        var manifest = await client.GetManifestAsync();
        Assert.Equal("external-process", manifest.Execution);

        // 2. A script runs - in the child process - and its operator call still comes back here.
        var count = await client.ExecuteScriptAsync("return employeesOperator.GetEmployees().Count;");
        Assert.True(count.Success, count.Error);
        Assert.Equal(3, count.Result!.Value.GetInt32());

        // 3. That round trip (child -> application -> child) ran the method on the dispatcher thread, exactly as
        //    it does in-process: moving where the script is isolated does not move where the operators live.
        var diagnostics = await client.InvokeFunctionAsync(BridgeCalls.Call("EmployeesOperator", "GetDiagnostics"));
        Assert.True(diagnostics.Success, diagnostics.Error);
        Assert.True(diagnostics.Result!.Value.GetProperty("onUiThread").GetBoolean());
    }

    [RequiresWpfApplicationFact]
    public async Task The_same_application_is_reachable_over_mcp_with_the_same_contract() {
        using var application = await WpfApplication.StartAsync(WpfApplication.LocateExecutable()!);
        var mcpEndpoint = application.GrpcUrl.Replace(":" + new Uri(application.GrpcUrl).Port, ":" + (new Uri(application.GrpcUrl).Port + 1)) + "/mcp";
        await using var client = await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(mcpEndpoint);

        var manifest = await client.GetManifestAsync();
        var result = await client.InvokeFunctionAsync(BridgeCalls.Call("AppInfoOperator", "Add", 20, 22));

        Assert.Equal("WpfAppOnlyDemo", manifest.AppName);
        Assert.True(result.Success, result.Error);
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }
}
