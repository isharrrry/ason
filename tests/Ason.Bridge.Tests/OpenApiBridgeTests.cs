using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ason.Bridge.OpenApi;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ason.Bridge.Tests;

/// <summary>
/// The HTTP/OpenAPI face of the bridge: the same contract as gRPC and MCP, reachable by any HTTP client, with
/// a document a generic tool can read. This is also the worked example of adding a transport without touching
/// the runtime.
/// </summary>
public class OpenApiBridgeTests {

    [Fact]
    public async Task The_manifest_and_the_instance_list_are_served_over_http() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var manifest = await http.GetFromJsonAsync<AsonBridgeManifest>("/ason/manifest");
        var instances = await http.GetFromJsonAsync<List<AsonBridgeInstance>>("/ason/instances");

        Assert.Equal("Bridge test app", manifest!.AppName);
        Assert.Contains(manifest.Api.Operators, o => o.TypeName == "BridgeCalculatorOperator");
        Assert.Equal("BridgeCalculatorOperator", Assert.Single(instances!).Handle);
    }

    [Fact]
    public async Task A_script_body_is_evaluated_over_http() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/script", new { code = "return BridgeStaticOperator.Add(20, 22);" });
        var result = await response.Content.ReadFromJsonAsync<AsonBridgeCallResult>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(result!.Success, result.Error);
        Assert.Equal(42, result.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task A_single_function_is_called_over_http_by_name() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        // The path-shaped endpoint is the one a Swagger UI user clicks.
        var response = await http.PostAsJsonAsync("/ason/functions/BridgeCalculatorOperator/Add", new { arguments = new object[] { 2, 3 } });
        var result = await response.Content.ReadFromJsonAsync<AsonBridgeCallResult>();
        Assert.True(result!.Success, result.Error);
        Assert.Equal(5, result.Result!.Value.GetInt32());

        // And the generic one carries the same call.
        var generic = await http.PostAsJsonAsync("/ason/functions/invoke", new { @operator = "BridgeStaticOperator", method = "Add", arguments = new object[] { 40, 2 } });
        var genericResult = await generic.Content.ReadFromJsonAsync<AsonBridgeCallResult>();
        Assert.True(genericResult!.Success, genericResult.Error);
        Assert.Equal(42, genericResult.Result!.Value.GetInt32());
    }

    [Fact]
    public async Task A_structured_failure_is_a_400_with_the_error_code() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/functions/invoke", new { @operator = "NoSuchOperator", method = "Do" });
        var result = await response.Content.ReadFromJsonAsync<AsonBridgeCallResult>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(AsonBridgeErrorCodes.OperatorNotFound, result!.ErrorCode);
    }

    [Fact]
    public async Task A_disabled_capability_is_not_served() {
        var options = BridgeTestApp.Options();
        options.Capabilities = new AsonBridgeCapabilities { InvokeFunction = false };
        await using var runtime = new AsonBridgeRuntime(options);
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var response = await http.PostAsJsonAsync("/ason/functions/invoke", new { @operator = "BridgeStaticOperator", method = "Add" });

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task The_openapi_document_describes_the_endpoints_and_the_callable_surface() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await OpenApiHost.StartAsync(runtime);
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var json = await http.GetStringAsync("/ason/openapi.json");
        using var document = JsonDocument.Parse(json);
        var rootElement = document.RootElement;

        Assert.Equal("3.0.3", rootElement.GetProperty("openapi").GetString());
        Assert.Equal("Bridge test app", rootElement.GetProperty("info").GetProperty("title").GetString());
        Assert.Equal("in-process", rootElement.GetProperty("x-ason-execution").GetString());

        var paths = rootElement.GetProperty("paths");
        Assert.True(paths.TryGetProperty("/script", out _));
        Assert.True(paths.TryGetProperty("/functions/invoke", out _));
        Assert.True(paths.TryGetProperty("/functions/{operator}/{method}", out _));
        Assert.True(rootElement.GetProperty("components").GetProperty("schemas").TryGetProperty("AsonBridgeCallResult", out _));

        // The callable surface is part of the document, so a client does not have to read the manifest.
        var operators = rootElement.GetProperty("x-ason-operators");
        var calculator = operators.EnumerateArray().Single(o => o.GetProperty("typeName").GetString() == "BridgeCalculatorOperator");
        var add = calculator.GetProperty("methods").EnumerateArray().Single(m => m.GetProperty("name").GetString() == "Add");
        Assert.Equal("Int32", add.GetProperty("returnType").GetString());
        Assert.Equal(2, add.GetProperty("parameters").GetArrayLength());
        Assert.True(rootElement.GetProperty("x-ason-capabilities").GetProperty("invokeFunction").GetBoolean());
    }

    [Fact]
    public async Task A_configured_bridge_key_is_required() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await OpenApiHost.StartAsync(runtime, options => options.ApiKey = "secret-key");
        using var http = new HttpClient { BaseAddress = new Uri(host.Url) };

        var anonymous = await http.GetAsync("/ason/manifest");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

        using var request = new HttpRequestMessage(HttpMethod.Get, "/ason/manifest");
        request.Headers.Add("X-Ason-Bridge-Key", "secret-key");
        var authorized = await http.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, authorized.StatusCode);
    }
}
