using System.Text.Json;
using Ason.Bridge.Tests.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Tests;

/// <summary>
/// The supported shape for a host that cannot go above net6 (other products' legacy shells): the application
/// itself has no embedded MCP server - the official SDK needs net8+ - so it publishes gRPC (and HTTP/OpenAPI)
/// and an MCP agent reaches it through the stdio relay, which is a separate net9 process and therefore does not
/// constrain the application at all.
///
/// This is that shape's acceptance test: the real net6.0 sample, the real relay and a real MCP client. It also
/// pins the honest half - the capability the application does not have must be absent from the tool list rather
/// than silently missing at call time.
/// </summary>
public class Net6ApplicationWithRelayEndToEndTests {

    [RequiresNet6ConsoleSampleFact]
    public async Task A_net6_application_is_driven_over_mcp_through_the_relay() {
        using var application = await ConsoleBridgeHost.StartAsync(framework: ConsoleBridgeHost.Net6Framework);
        // The leg says so itself, which is what makes "no embedded MCP server" observable rather than assumed.
        Assert.Contains("mcp=none", application.Output);
        var relay = RelayHost.LocateAssembly()!;

        var stderr = new List<string>();
        var (command, arguments) = RelayHost.LaunchCommand(relay, application.GrpcUrl, mcp: false);
        var transport = new StdioClientTransport(new StdioClientTransportOptions {
            Command = command,
            Arguments = arguments,
            ShutdownTimeout = TimeSpan.FromSeconds(10),
            StandardErrorLines = line => stderr.Add(line)
        });

        await using var client = await McpClient.CreateAsync(transport);

        var tools = (await client.ListToolsAsync()).Select(t => t.Name).ToList();
        Assert.Contains("ason_get_manifest", tools);
        Assert.Contains("ason_invoke_function", tools);
        Assert.DoesNotContain("ason_invoke_mcp_tool", tools);

        using var manifest = JsonDocument.Parse(Text(await client.CallToolAsync("ason_get_manifest", null)));
        Assert.False(manifest.RootElement.GetProperty("capabilities").GetProperty("invokeMcpTool").GetBoolean(),
            "a net6.0 application has no MCP adapter, so it must not advertise MCP passthrough");

        var sum = Text(await client.CallToolAsync("ason_invoke_function", new Dictionary<string, object?>(StringComparer.Ordinal) {
            ["operator"] = "LibDemoStaticOperator",
            ["method"] = "Add",
            ["argumentsJson"] = "[40,2]"
        }));
        using var sumResult = JsonDocument.Parse(sum);
        Assert.True(sumResult.RootElement.GetProperty("success").GetBoolean(),
            $"tool returned: {sum}{Environment.NewLine}relay stderr:{Environment.NewLine}{string.Join(Environment.NewLine, stderr)}");
        Assert.Equal(42, sumResult.RootElement.GetProperty("result").GetInt32());
    }

    static string Text(CallToolResult result) =>
        result.Content.OfType<TextContentBlock>().FirstOrDefault()?.Text
        ?? throw new InvalidOperationException("the tool returned no text content");
}
