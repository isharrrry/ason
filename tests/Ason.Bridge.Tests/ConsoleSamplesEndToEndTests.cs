using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The cross-platform pair of samples, driven as processes: a console application that publishes its operators,
/// and a console agent that owns none of them and drives the application over gRPC or MCP.
///
/// This is also the automated cover for <c>--execution external</c>: the script host is a child process of the
/// application, yet an operator call still runs inside the application, which is the two-way protocol in action.
///
/// Shares the non-parallel collection with the WPF tests: every test here starts a real process and binds ports.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class ConsoleSamplesEndToEndTests {

    [RequiresConsoleSamplesFact]
    public async Task A_console_agent_drives_a_console_application_over_gRPC_and_owns_no_operator() {
        using var host = await ConsoleBridgeHost.StartAsync();

        var (exitCode, output) = await ConsoleAgentRunner.RunAsync(host.GrpcUrl, mcp: false, timeout: null, "--list");

        Assert.Equal(0, exitCode);
        Assert.Contains("app=LibDemo application", output);
        Assert.Contains("transport=grpc", output);
        Assert.Contains("execution=in-process", output);
        Assert.Contains("operators=LibDemoOperator,LibDemoStaticOperator", output);
        // The point of the split: the agent built its API from the manifest and declares nothing itself.
        Assert.Contains("agent-operators=0 library=manifest", output);
    }

    [RequiresConsoleSamplesFact]
    public async Task The_same_agent_works_over_mcp() {
        using var host = await ConsoleBridgeHost.StartAsync();

        var (exitCode, output) = await ConsoleAgentRunner.RunAsync(host.McpUrl, mcp: true, timeout: null, "--list");

        Assert.Equal(0, exitCode);
        Assert.Contains("transport=mcp", output);
        Assert.Contains("agent-operators=0 library=manifest", output);
    }

    [RequiresConsoleSamplesFact]
    public async Task Without_a_model_key_the_agent_reports_it_and_still_lists_the_api() {
        using var host = await ConsoleBridgeHost.StartAsync();

        var (listExit, listOutput) = await ConsoleAgentRunner.RunAsync(host.GrpcUrl, mcp: false, timeout: null, "--list");
        var (sendExit, sendOutput) = await ConsoleAgentRunner.RunAsync(host.GrpcUrl, mcp: false, timeout: null, "--send", "add 20 and 22");

        Assert.Equal(0, listExit);
        Assert.Contains("library=manifest", listOutput);
        // The chat path needs a model; that is reported, not crashed, and the key-free path keeps working.
        if (sendExit == 3) Assert.Contains("no model configured", sendOutput);
        else Assert.Contains("--- task:", sendOutput); // a key is configured in this environment, so it really ran
    }

    [RequiresConsoleSamplesFact]
    public async Task The_application_can_evaluate_scripts_in_a_child_process_and_still_resolve_operators() {
        using var host = await ConsoleBridgeHost.StartAsync(execution: "external");
        await using var client = GrpcAsonBridgeClient.Connect(host.GrpcUrl);

        var manifest = await client.GetManifestAsync();
        var result = await client.ExecuteScriptAsync("return LibDemoStaticOperator.Add(40, 2);");

        Assert.Equal("external-process", manifest.Execution);
        Assert.True(result.Success, result.Error);
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }

    [RequiresConsoleSamplesFact]
    public async Task The_external_execution_choice_is_visible_over_mcp_too() {
        using var host = await ConsoleBridgeHost.StartAsync(execution: "external");
        await using var client = await McpAsonBridgeClient.ConnectAsync(host.McpUrl);

        var manifest = await client.GetManifestAsync();
        var function = await client.InvokeFunctionAsync(BridgeCalls.Call("LibDemoStaticOperator", "Add", 20, 22));

        Assert.Equal("external-process", manifest.Execution);
        Assert.True(function.Success, function.Error);
        Assert.Equal(42, function.Result!.Value.GetInt32());
    }
}
