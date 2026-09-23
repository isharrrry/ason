using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// Where a script is evaluated is a deployment decision, not a transport decision: the bridge resolves it
/// from the options so an application can pick the in-process executor or the out-of-process
/// Ason.ExternalExecutor - and a host can inject its own executor entirely.
/// </summary>
public class ExecutorSelectionTests {

    [Theory]
    [InlineData(AsonBridgeExecution.InProcess, "in-process")]
    [InlineData(AsonBridgeExecution.ExternalProcess, "external-process")]
    [InlineData(AsonBridgeExecution.Docker, "docker")]
    [InlineData(AsonBridgeExecution.RemoteRunner, "remote-runner")]
    public void Create_resolves_an_executor_for_every_execution_location(AsonBridgeExecution execution, string expectedName) {
        var options = BridgeTestApp.Options();
        options.Execution = execution;
        options.RemoteRunnerBaseUrl = "http://localhost:1";

        var executor = AsonExecutors.Create(options);

        Assert.Equal(expectedName, executor.Name);
    }

    [Fact]
    public void Create_prefers_an_executor_injected_by_the_host() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        options.Execution = AsonBridgeExecution.ExternalProcess;

        var executor = AsonExecutors.Create(options);

        Assert.Same(fake, executor);
    }
}
