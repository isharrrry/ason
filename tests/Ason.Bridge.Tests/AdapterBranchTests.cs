using System.Net.Http.Json;
using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Grpc.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Ason.Bridge.Tests;

/// <summary>
/// The branches that a passing deployment never walks: forwarding endpoints used in full, transports answering
/// messages they should never receive, registration guards, and the mappings from a wire failure back to a
/// bridge error code. They are cheap to reach and expensive to discover in production.
/// </summary>
[Collection(WpfEndToEnd.CollectionName)]
public class AdapterBranchTests {

    static AsonBridgeRuntime Runtime(string executorName = "in-process", params string[] logs) {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        var executor = new FakeAsonExecutor { Name = executorName };
        executor.LogsToEmit.AddRange(logs);
        options.Executor = executor;
        options.Capabilities = new AsonBridgeCapabilities { InvokeMcpTool = true };
        return new AsonBridgeRuntime(options);
    }

    [Theory]
    [InlineData("in-process", AsonBridgeExecution.InProcess)]
    [InlineData("external-process", AsonBridgeExecution.ExternalProcess)]
    [InlineData("docker", AsonBridgeExecution.Docker)]
    [InlineData("remote-runner", AsonBridgeExecution.RemoteRunner)]
    [InlineData("something-else", AsonBridgeExecution.InProcess)]
    public async Task A_gRPC_forwarding_endpoint_reads_the_execution_location_from_the_remote_manifest(string reported, AsonBridgeExecution expected) {
        await using var runtime = Runtime(reported);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);

        var manifest = await endpoint.GetManifestAsync();

        Assert.Equal(reported, manifest.Execution);
        Assert.Equal(expected, endpoint.Options.Execution);
        Assert.Same(manifest, endpoint.Manifest);
        Assert.Equal("Bridge test app", endpoint.Options.AppName);
    }

    [Fact]
    public async Task A_gRPC_forwarding_endpoint_serves_both_interfaces_and_the_instances() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);
        await endpoint.GetManifestAsync();

        var instances = await endpoint.ListInstancesAsync();
        var script = await endpoint.ExecuteScriptAsync("return 1;");
        var function = await endpoint.InvokeFunctionAsync(new AsonBridgeFunctionCall("BridgeCalculatorOperator", "Add", "BridgeCalculatorOperator", new[] { JsonSerializer.SerializeToElement(20), JsonSerializer.SerializeToElement(22) }));

        Assert.Contains(instances, i => i.Handle == "BridgeCalculatorOperator");
        Assert.True(script.Success, script.Error);
        Assert.Equal("script-ok", script.Result!.Value.GetString());
        Assert.True(function.Success, function.Error);
        Assert.Equal("function-ok", function.Result!.Value.GetString());
    }

    [Fact]
    public async Task A_gRPC_forwarding_endpoint_relays_logs_and_mcp_pass_through() {
        var fake = new FakeAsonExecutor { Name = "in-process", McpServers = new[] { "stub-server" }, McpToolResult = AsonBridgeCallResult.Ok(JsonSerializer.SerializeToElement("mcp-ok")) };
        fake.LogsToEmit.Add("through-the-relay");
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        var options = BridgeTestApp.OptionsFor(root);
        options.Executor = fake;
        options.Capabilities = new AsonBridgeCapabilities { InvokeMcpTool = true };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var endpoint = new GrpcAsonBridgeEndpoint(client);

        var streamed = await endpoint.ExecuteScriptWithLogsAsync("return 1;");
        var passThrough = await endpoint.InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement>());

        Assert.Equal("through-the-relay", Assert.Single(streamed.Logs).Message);
        Assert.True(streamed.Result.Success, streamed.Result.Error);
        Assert.True(passThrough.Success, passThrough.Error);
        Assert.Equal("mcp-ok", passThrough.Result!.Value.GetString());
    }

    [Fact]
    public async Task The_gRPC_runner_transport_answers_messages_that_must_not_travel_this_way() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        await using var transport = new TransportProbe(new GrpcAsonBridgeTransport(client));

        foreach (var type in new[] { "invoke", "invokeMcp" }) {
            // No id at all: the transport has to invent one instead of losing the answer.
            await transport.SendAsync(JsonSerializer.Serialize(new { type, handle = "x" }));
        }

        Assert.Equal(2, transport.Lines.Count);
        foreach (var line in transport.Lines) {
            using var document = JsonDocument.Parse(line);
            Assert.Equal("invokeResult", document.RootElement.GetProperty("type").GetString());
            Assert.Contains("operators are resolved inside the application process", document.RootElement.GetProperty("error").GetString());
            Assert.False(string.IsNullOrEmpty(document.RootElement.GetProperty("id").GetString()));
        }
    }

    [Fact]
    public async Task The_gRPC_runner_transport_relays_the_applications_logs_when_asked() {
        await using var runtime = Runtime("in-process", "line one");
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        await using var transport = new TransportProbe(new GrpcAsonBridgeTransport(client) { RelayLogs = true });

        await transport.SendAsync(JsonSerializer.Serialize(new { id = "42", type = "exec", code = "return 1;" }));

        Assert.Contains(transport.Lines, l => l.Contains("\"type\":\"log\"") && l.Contains("line one"));
        Assert.Contains(transport.Lines, l => l.Contains("\"type\":\"execResult\"") && l.Contains("\"id\":\"42\""));
    }

    [Fact]
    public async Task A_wire_level_argument_error_comes_back_as_a_bridge_error_code() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var malformed = await Assert.ThrowsAsync<RpcException>(async () => await client.Raw.InvokeFunctionAsync(new InvokeFunctionRequest {
            Operator = "BridgeCalculatorOperator",
            Method = "Add",
            ArgumentsJson = "{not json"
        }));

        // The service answers InvalidArgument; the raw stub surfaces it as a status, and the typed client folds
        // the same status back into a bridge error code instead of letting an RpcException escape.
        Assert.Equal(StatusCode.InvalidArgument, malformed.StatusCode);
    }

    [Fact]
    public void Registration_guards_reject_null_arguments() {
        var services = new ServiceCollection();
        var runtime = Runtime();

        Assert.Throws<ArgumentNullException>(() => AsonBridgeGrpcExtensions.AddAsonGrpcBridge(null!, runtime));
        Assert.Throws<ArgumentNullException>(() => services.AddAsonGrpcBridge(null!));
        Assert.Throws<ArgumentNullException>(() => AsonBridgeMcpExtensions.AddAsonMcpBridge(null!, runtime));
        Assert.Throws<ArgumentNullException>(() => services.AddAsonMcpBridge(null!));
        Assert.Throws<ArgumentNullException>(() => AsonBridgeMcpExtensions.AddAsonMcpStdioBridge(null!, runtime));
        Assert.Throws<ArgumentNullException>(() => services.AddAsonMcpStdioBridge(null!));
    }

    [Fact]
    public async Task The_MCP_client_surface_is_complete() {
        await using var runtime = Runtime();
        await using var host = await BridgeMcpHost.StartAsync(runtime);
        var client = await McpAsonBridgeClient.ConnectAsync(host.Url);

        var viaFactory = McpAsonBridgeClient.FromClient(client.Raw);
        var scriptApi = await viaFactory.GetScriptApiAsync();
        var instances = await viaFactory.ListInstancesAsync();
        var function = await viaFactory.InvokeFunctionAsync(new AsonBridgeFunctionCall("BridgeCalculatorOperator", "Add", "BridgeCalculatorOperator", new[] { JsonSerializer.SerializeToElement(1), JsonSerializer.SerializeToElement(2) }));
        var passThrough = await client.InvokeMcpToolAsync("stub-server", "stub-tool", new Dictionary<string, JsonElement> { ["a"] = JsonSerializer.SerializeToElement(1) });

        Assert.Contains("BridgeCalculatorOperator", scriptApi.Proxies);
        Assert.Contains("BridgeStaticOperator", scriptApi.Signatures);
        Assert.Contains(instances, i => i.Handle == "BridgeCalculatorOperator");
        Assert.True(function.Success, function.Error);
        Assert.Equal("function-ok", function.Result!.Value.GetString());
        Assert.False(passThrough.Success);
        Assert.Equal(AsonBridgeErrorCodes.NotSupported, passThrough.ErrorCode);
    }

    [Fact]
    public void Registration_guards_of_the_OpenAPI_adapter() {
        var services = new ServiceCollection();
        var runtime = Runtime();

        Assert.Throws<ArgumentNullException>(() => Ason.Bridge.OpenApi.AsonBridgeOpenApiExtensions.AddAsonOpenApiBridge(null!, runtime));
        Assert.Throws<ArgumentNullException>(() => Ason.Bridge.OpenApi.AsonBridgeOpenApiExtensions.AddAsonOpenApiBridge(services, null!));
    }

    [Fact]
    public async Task The_HTTP_adapter_answers_a_query_handle_and_a_missing_body() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        BridgeTestApp.Attach<ThreadProbeOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        // The handle travels as a query parameter on the path form, which is what makes it linkable in Swagger.
        var byQuery = await http.PostAsJsonAsync("/ason/functions/BridgeCalculatorOperator/Add?handle=BridgeCalculatorOperator",
            new { arguments = new[] { 20, 22 } });
        // No body at all is a valid "no arguments" request, not a crash.
        var noBody = await http.PostAsync("/ason/functions/ThreadProbeOperator/ProbeThread", null);

        Assert.Equal(System.Net.HttpStatusCode.OK, byQuery.StatusCode);
        Assert.Equal(42, JsonDocument.Parse(await byQuery.Content.ReadAsStringAsync()).RootElement.GetProperty("result").GetInt32());
        Assert.Equal(System.Net.HttpStatusCode.OK, noBody.StatusCode);
    }

    sealed class TransportProbe : IAsyncDisposable {
        readonly Ason.Transport.IRunnerTransport _inner;

        internal TransportProbe(Ason.Transport.IRunnerTransport inner) {
            _inner = inner;
            _inner.LineReceived += line => Lines.Add(line);
        }

        internal List<string> Lines { get; } = new();

        internal Task SendAsync(string line) => _inner.SendAsync(line);

        public ValueTask DisposeAsync() => _inner is IAsyncDisposable disposable ? disposable.DisposeAsync() : ValueTask.CompletedTask;
    }
}
