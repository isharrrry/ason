using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The agent half of the split, driven against the running application: it connects, reads the operator API
/// the application published, and - the point of the whole exercise - owns no operator of its own.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class WpfAgentEndToEndTests {

    [RequiresWpfAgentFact]
    public async Task The_agent_connects_over_grpc_and_declares_no_operator() {
        using var application = await WpfApplication.StartAsync(WpfApplication.LocateExecutable()!);

        var (exitCode, output) = await WpfAgentRunner.VerifyAsync(WpfAgentRunner.LocateExecutable()!, application.GrpcUrl, mcp: false);

        Assert.Equal(0, exitCode);
        // Nothing to call lives on this side.
        Assert.Contains("agent-operators=0", output);
        // Everything it can call came from the application.
        Assert.Contains("app=WpfAppOnlyDemo", output);
        Assert.Contains("EmployeesOperator", output);
        Assert.Contains("ReportOperator", output);
        Assert.Contains("library-proxies=ready", output);
        // And a function call from the agent really executed in the application.
        Assert.Contains("remote-call=42", output);
    }

    [RequiresWpfAgentFact]
    public async Task The_agent_connects_over_mcp_with_the_same_result() {
        using var application = await WpfApplication.StartAsync(WpfApplication.LocateExecutable()!);
        var mcpEndpoint = $"http://localhost:{new Uri(application.GrpcUrl).Port + 1}/mcp";

        var (exitCode, output) = await WpfAgentRunner.VerifyAsync(WpfAgentRunner.LocateExecutable()!, mcpEndpoint, mcp: true);

        Assert.Equal(0, exitCode);
        Assert.Contains("transport=Mcp", output);
        Assert.Contains("remote-call=42", output);
    }
}
