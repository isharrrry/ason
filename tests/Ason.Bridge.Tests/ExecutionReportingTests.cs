using System.Net.Http.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The execution location is reported, not guessed (T13). The manifest's <c>execution</c> comes from the
/// executor's name, so the value travels the same way through every adapter - and a host that supplies its own
/// executor controls what callers are told. The real Docker case needs a daemon, so it follows the repository's
/// existing policy (excluded from the default filter, documented as requiring Docker) and what is asserted here
/// is the reporting path itself.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class ExecutionReportingTests {

    static AsonBridgeOptions OptionsWithExecutor(string name) {
        var options = BridgeTestApp.Options();
        options.Executor = new FakeAsonExecutor { Name = name };
        return options;
    }

    [Fact]
    public async Task A_host_supplied_executor_decides_what_every_adapter_reports() {
        await using var runtime = new AsonBridgeRuntime(OptionsWithExecutor("docker"));
        await using var grpcHost = await BridgeGrpcHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        await using var httpHost = await OpenApiHost.StartAsync(runtime);

        var manifest = await runtime.GetManifestAsync();
        await using var grpc = GrpcAsonBridgeClient.Connect(grpcHost.Url);
        var overGrpc = await grpc.GetManifestAsync();
        var overMcp = await (await Ason.Bridge.Mcp.McpAsonBridgeClient.ConnectAsync(mcpHost.Url)).GetManifestAsync();
        using var http = new HttpClient { BaseAddress = new Uri(httpHost.Url) };
        var overHttp = await http.GetFromJsonAsync<AsonBridgeManifest>("/ason/manifest");

        Assert.Equal("docker", manifest.Execution);
        Assert.Equal("docker", overGrpc.Execution);
        Assert.Equal("docker", overMcp.Execution);
        Assert.Equal("docker", overHttp!.Execution);
    }

    [Theory]
    [InlineData(AsonBridgeExecution.InProcess, "in-process")]
    [InlineData(AsonBridgeExecution.ExternalProcess, "external-process")]
    [InlineData(AsonBridgeExecution.Docker, "docker")]
    [InlineData(AsonBridgeExecution.RemoteRunner, "remote-runner")]
    public async Task Every_execution_location_maps_to_the_name_the_manifest_publishes(AsonBridgeExecution execution, string expected) {
        var options = BridgeTestApp.Options();
        options.Execution = execution;
        options.RemoteRunnerBaseUrl = "http://localhost:1";   // required only by the remote location; never contacted here

        await using var executor = new RunnerClientAsonExecutor(options);

        Assert.Equal(expected, executor.Name);
    }
}
