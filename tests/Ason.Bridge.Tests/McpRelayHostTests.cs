using System.Text.Json;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Tests;

/// <summary>
/// The relay as an MCP-only agent sees it: the process is launched with its stdin/stdout as the protocol
/// channel, the tools are listed there, and every call travels to the running application and back. The core
/// forwarding logic is covered elsewhere; what this covers is that the process really speaks MCP on the right
/// streams, that it fails loudly when the application is unreachable, and that an application publishing MCP
/// (no gRPC at all) is an equally acceptable thing to relay.
/// </summary>
public class McpRelayHostTests {

    [RequiresRelayHostFact]
    public async Task An_mcp_client_discovers_and_drives_the_application_through_the_relay() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        var relay = RelayHost.LocateAssembly()!;

        var stderr = new List<string>();
        var (command, arguments) = RelayHost.LaunchCommand(relay, host.Url, mcp: false);
        var transport = new StdioClientTransport(new StdioClientTransportOptions {
            Command = command,
            Arguments = arguments,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line => stderr.Add(line)
        });

        await using var client = await McpClient.CreateAsync(transport);

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("ason_get_manifest", tools);
        Assert.Contains("ason_execute_script", tools);
        Assert.Contains("ason_invoke_function", tools);

        var manifestText = Text(await client.CallToolAsync("ason_get_manifest", null));
        using var manifest = JsonDocument.Parse(manifestText);
        Assert.Equal("Bridge test app", manifest.RootElement.GetProperty("appName").GetString());
        Assert.Contains(manifest.RootElement.GetProperty("api").GetProperty("operators").EnumerateArray(),
            o => o.GetProperty("typeName").GetString() == "BridgeCalculatorOperator");

        var sum = Text(await client.CallToolAsync("ason_invoke_function", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["operator"] = "BridgeStaticOperator",
            ["method"] = "Add",
            ["argumentsJson"] = "[40,2]"
        }));
        Assert.True(sum.StartsWith("{", StringComparison.Ordinal),
            $"tool returned: {sum}{Environment.NewLine}relay stderr:{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}");
        using var sumResult = JsonDocument.Parse(sum);
        Assert.True(sumResult.RootElement.GetProperty("success").GetBoolean(), sum);
        Assert.Equal(42, sumResult.RootElement.GetProperty("result").GetInt32());

        var script = Text(await client.CallToolAsync("ason_execute_script", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["code"] = "return BridgeStaticOperator.Add(1, 1);"
        }));
        using var scriptResult = JsonDocument.Parse(script);
        Assert.True(scriptResult.RootElement.GetProperty("success").GetBoolean(), script);
    }

    [RequiresRelayHostFact]
    public async Task The_same_relay_serves_an_application_that_only_speaks_mcp() {
        // No gRPC anywhere in this test: the application publishes MCP, and the relay forwards MCP to MCP.
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var application = await BridgeMcpHost.StartAsync(runtime);
        var applicationClient = await McpAsonBridgeClient.ConnectAsync(application.Url);
        var endpoint = new McpAsonBridgeEndpoint(applicationClient);

        // The relay process is the same one; only the channel it uses to reach the application changes.
        await using var relayed = await BridgeMcpHost.StartAsync(endpoint);
        await using var client = await McpAsonBridgeClient.ConnectAsync(relayed.Url);

        var manifest = await client.GetManifestAsync();
        var call = await client.InvokeFunctionAsync(BridgeCalls.Call("BridgeStaticOperator", "Add", 20, 22));

        Assert.Equal("Bridge test app", manifest.AppName);
        Assert.True(call.Success, call.Error);
        Assert.Equal(42, call.Result!.Value.GetInt32());
    }

    [RequiresRelayHostFact]
    public async Task The_relay_process_serves_an_application_that_only_speaks_mcp() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var application = await BridgeMcpHost.StartAsync(runtime);
        var relay = RelayHost.LocateAssembly()!;

        var (command, arguments) = RelayHost.LaunchCommand(relay, application.Url, mcp: true);
        var transport = new StdioClientTransport(new StdioClientTransportOptions {
            Command = command,
            Arguments = arguments,
            ShutdownTimeout = TimeSpan.FromSeconds(10)
        });

        await using var client = await McpClient.CreateAsync(transport);
        var call = Text(await client.CallToolAsync("ason_invoke_function", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["operator"] = "BridgeStaticOperator",
            ["method"] = "Add",
            ["argumentsJson"] = "[40,2]"
        }));

        using var result = JsonDocument.Parse(call);
        Assert.True(result.RootElement.GetProperty("success").GetBoolean(), call);
        Assert.Equal(42, result.RootElement.GetProperty("result").GetInt32());
    }

    [RequiresRelayHostFact]
    public async Task The_relay_fails_fast_when_the_application_is_unreachable() {
        var relay = RelayHost.LocateAssembly()!;
        // Nothing listens on this port, so the relay must report it and stop instead of waiting forever.
        var (exitCode, output, error) = await RelayHost.RunToExitAsync(relay, "http://localhost:1", TimeSpan.FromSeconds(60));

        Assert.Equal(3, exitCode);
        Assert.Contains("cannot reach the bridge", error);
        Assert.DoesNotContain("ason_get_manifest", output);
    }

    [RequiresRelayHostFact]
    public async Task A_tool_failure_inside_the_relay_is_reported_and_does_not_corrupt_the_protocol() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        var relay = RelayHost.LocateAssembly()!;

        var stderr = new List<string>();
        var (command, arguments) = RelayHost.LaunchCommand(relay, host.Url, mcp: false);
        var transport = new StdioClientTransport(new StdioClientTransportOptions {
            Command = command,
            Arguments = arguments,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line => stderr.Add(line)
        });

        await using var client = await McpClient.CreateAsync(transport);

        // An application-level failure, which is a result the relay passes through as text...
        var failure = Text(await client.CallToolAsync("ason_execute_script", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["code"] = "return NoSuchOperator.Do();"
        }));
        using var failureResult = JsonDocument.Parse(failure);
        Assert.False(failureResult.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal(AsonBridgeErrorCodes.ExecutionFailed, failureResult.RootElement.GetProperty("errorCode").GetString());

        // ...and a malformed request, which must come back as a tool error rather than take the process down.
        var malformed = await client.CallToolAsync("ason_invoke_function", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["operator"] = "BridgeStaticOperator",
            ["method"] = "Add",
            ["argumentsJson"] = "not json"
        });
        Assert.True(malformed.IsError == true || Text(malformed).Length > 0);

        // The protocol is still alive after both.
        var manifest = Text(await client.CallToolAsync("ason_get_manifest", null));
        using var manifestResult = JsonDocument.Parse(manifest);
        Assert.Equal("Bridge test app", manifestResult.RootElement.GetProperty("appName").GetString());
    }

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
