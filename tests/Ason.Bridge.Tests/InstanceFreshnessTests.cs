using System.Net.Http.Json;
using System.Text.Json;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using ModelContextProtocol.Protocol;

namespace Ason.Bridge.Tests;

/// <summary>
/// Instance freshness (T4). A caller that keeps a manifest snapshot holds declarations for the instances that
/// existed when it read it, while views keep opening and closing. The bridge answers three things: a revision
/// that changes with the instance set, a body-only mode in which the application supplies its current proxy
/// layer and instance declarations, and a transport switch that hands the caller's snapshot layer back to the
/// application so the declarations are rebuilt per call.
/// </summary>
public class InstanceFreshnessTests {

    const string Body = "return threadProbeOperator.ProbeThread();";

    [Fact]
    public async Task The_manifest_reports_an_instances_revision_that_changes_with_the_instance_set() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        var first = await runtime.GetManifestAsync();
        var again = await runtime.GetManifestAsync();
        Assert.False(string.IsNullOrWhiteSpace(first.InstancesRevision));
        Assert.Equal(first.InstancesRevision, again.InstancesRevision);   // stable while nothing changes

        BridgeTestApp.Attach<BridgeCalculatorOperator>(root, "2");

        var afterChange = await runtime.GetManifestAsync();
        Assert.NotEqual(first.InstancesRevision, afterChange.InstancesRevision);
    }

    [Fact]
    public async Task A_snapshot_is_stale_when_an_instance_appears_after_it_was_read() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        var snapshot = await runtime.GetManifestAsync();

        BridgeTestApp.Attach<ThreadProbeOperator>(root);

        // The snapshot's own layer declares only the instance that existed when it was read.
        var stale = await runtime.ExecuteScriptAsync(snapshot.Proxies + "\n" + Body, includeProxyPreamble: false);

        Assert.False(stale.Success);
        Assert.Equal(AsonBridgeErrorCodes.ExecutionFailed, stale.ErrorCode);
    }

    [Fact]
    public async Task A_body_only_caller_gets_the_applications_current_declarations() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);

        BridgeTestApp.Attach<ThreadProbeOperator>(root);   // the caller never saw this one

        var rescued = await runtime.ExecuteScriptAsync(Body, includeProxyPreamble: false, includeInstanceDeclarations: true);

        Assert.True(rescued.Success, rescued.Error);
        Assert.True(rescued.Result!.Value.GetInt32() > 0);
    }

    [Fact]
    public async Task The_requested_declarations_reach_every_adapter() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var grpcHost = await BridgeGrpcHost.StartAsync(runtime);
        await using var mcpHost = await BridgeMcpHost.StartAsync(runtime);
        await using var httpHost = await OpenApiHost.StartAsync(runtime);

        BridgeTestApp.Attach<ThreadProbeOperator>(root);

        await using var grpc = GrpcAsonBridgeClient.Connect(grpcHost.Url);
        var viaGrpc = await grpc.ExecuteScriptAsync(Body, includeProxyPreamble: false, includeInstanceDeclarations: true);

        var mcp = await McpAsonBridgeClient.ConnectAsync(mcpHost.Url);
        var viaMcp = await mcp.ExecuteScriptAsync(Body, includeProxyPreamble: false, includeInstanceDeclarations: true);

        using var http = new HttpClient { BaseAddress = new Uri(httpHost.Url) };
        var response = await http.PostAsJsonAsync("/ason/script", new { code = Body, includeProxyPreamble = false, includeInstanceDeclarations = true });
        var viaHttp = await response.Content.ReadFromJsonAsync<AsonBridgeCallResult>();

        Assert.True(viaGrpc.Success, viaGrpc.Error);
        Assert.True(viaMcp.Success, viaMcp.Error);
        Assert.True(viaHttp!.Success, viaHttp.Error);
        Assert.True(viaGrpc.Result!.Value.GetInt32() > 0);
        Assert.True(viaMcp.Result!.Value.GetInt32() > 0);
        Assert.True(viaHttp.Result!.Value.GetInt32() > 0);
    }

    [Fact]
    public async Task The_runner_transport_hands_the_callers_snapshot_layer_back_to_the_application() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        var snapshot = await runtime.GetManifestAsync();

        BridgeTestApp.Attach<ThreadProbeOperator>(root);   // appears after the snapshot
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);
        var code = snapshot.Proxies + "\n" + Body;

        // Wave 1 behaviour: the agent's own (stale) proxy layer travels as the code and is executed as sent.
        var asSent = await SendAsync(new GrpcAsonBridgeTransport(client), code);
        Assert.False(asSent.Success);

        // With the snapshot layer named, the application rebuilds its declarations for every call.
        var refreshed = await SendAsync(new GrpcAsonBridgeTransport(client) { Proxies = snapshot.Proxies }, code);
        Assert.True(refreshed.Success, refreshed.Error);
        Assert.True(refreshed.Result!.Value.GetInt32() > 0);
    }

    [Fact]
    public async Task The_manifest_carries_the_revision_over_the_wire() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        await using var runtime = BridgeTestApp.CreateRuntime(root);
        await using var host = await BridgeGrpcHost.StartAsync(runtime);
        await using var client = GrpcAsonBridgeClient.Connect(host.Url);

        var manifest = await client.GetManifestAsync();
        var raw = await client.Raw.GetManifestAsync(new GetManifestRequest());
        using var document = JsonDocument.Parse(raw.ManifestJson);

        Assert.False(string.IsNullOrWhiteSpace(manifest.InstancesRevision));
        Assert.Equal(manifest.InstancesRevision, raw.InstancesRevision);
        Assert.Equal(manifest.InstancesRevision, document.RootElement.GetProperty("instancesRevision").GetString());
        Assert.Equal(AsonBridgeProtocol.Version, manifest.ProtocolVersion);
    }

    static async Task<AsonBridgeCallResult> SendAsync(GrpcAsonBridgeTransport transport, string code) {
        string? line = null;
        transport.LineReceived += received => line = received;
        await transport.SendAsync(JsonSerializer.Serialize(new { id = "1", type = "exec", code }));

        Assert.NotNull(line);
        using var document = JsonDocument.Parse(line!);
        Assert.Equal("execResult", document.RootElement.GetProperty("type").GetString());
        if (document.RootElement.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.String) {
            return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, error.GetString() ?? "failed");
        }
        return AsonBridgeCallResult.Ok(document.RootElement.GetProperty("result").Clone());
    }
}
