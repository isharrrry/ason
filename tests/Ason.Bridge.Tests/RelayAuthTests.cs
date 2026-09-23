using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Tests;

/// <summary>
/// A relay in front of a keyed application: the key travels as a header the caller passes to the relay, and
/// without it the relay must fail fast rather than serve tools that cannot work.
/// </summary>
public class RelayAuthTests {

    [RequiresRelayHostFact]
    public async Task The_relay_forwards_its_headers_to_a_keyed_application() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy);
        var relay = RelayHost.LocateAssembly()!;

        var (command, arguments) = RelayHost.LaunchCommand(relay, host.Url, mcp: false, headers: TestAuth.Authorized);
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
    public async Task The_relay_fails_fast_when_the_application_requires_a_key_it_was_not_given() {
        await using var runtime = new AsonBridgeRuntime(BridgeTestApp.Options());
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy);
        var relay = RelayHost.LocateAssembly()!;

        var (exitCode, output, error) = await RelayHost.RunToExitAsync(relay, host.Url, TimeSpan.FromSeconds(60));

        Assert.Equal(3, exitCode);
        Assert.Contains("cannot reach the bridge", error);
        Assert.DoesNotContain("ason_get_manifest", output);
    }

    static string Text(CallToolResult result) =>
        string.Concat(result.Content.OfType<TextContentBlock>().Select(c => c.Text));
}
