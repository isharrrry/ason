using Ason.Bridge.Grpc;
using Ason.Bridge.Tests.Operators;
using Ason.Bridge.Tests.TestSupport;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;

namespace Ason.Bridge.Tests;

/// <summary>
/// Non-.NET callers (T10): the contract has to be obtainable without reading this repository, and the
/// reflection service that makes <c>grpcurl</c> and generated stubs possible is an opt-in that must not widen
/// the exposed surface by accident.
/// </summary>
public class ReflectionAndContractTests {

    static AsonBridgeRuntime Runtime() {
        var root = BridgeTestApp.NewRoot();
        BridgeTestApp.Attach<BridgeCalculatorOperator>(root);
        return BridgeTestApp.CreateRuntime(root);
    }

    [Fact]
    public async Task Reflection_is_not_published_by_default() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime);

        Assert.DoesNotContain(host.Endpoints, e => IsReflection(e));

        // The bridge itself is mapped either way, so the absence above is about reflection, not about a
        // host that failed to start.
        Assert.Contains(host.Endpoints, e => e.DisplayName?.Contains("GetManifest", StringComparison.Ordinal) == true);
    }

    [Fact]
    public async Task Reflection_is_published_when_registration_asks_for_it() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime, enableReflection: true);

        Assert.Contains(host.Endpoints, e => IsReflection(e));
    }

    [Fact]
    public async Task Reflection_is_published_when_mapping_asks_for_it() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime, mapReflection: true);

        Assert.Contains(host.Endpoints, e => IsReflection(e));
    }

    [Fact]
    public async Task Reflection_obeys_the_policy_the_bridge_requires() {
        await using var runtime = Runtime();
        await using var host = await BridgeGrpcHost.StartAsync(runtime, TestAuth.Policy, enableReflection: true);

        var reflections = host.Endpoints.Where(IsReflection).ToList();
        Assert.NotEmpty(reflections);

        // Reflection republishes the whole callable surface, so an unauthorized caller must not be able to
        // read the contract either: every part of it carries the same policy the service does.
        // (The service is published twice, as grpc.reflection.v1 and grpc.reflection.v1alpha.)
        Assert.All(reflections, endpoint => {
            var authorize = Assert.Single(endpoint.Metadata.OfType<IAuthorizeData>());
            Assert.Equal(TestAuth.Policy, authorize.Policy);
        });
    }

    static bool IsReflection(Endpoint endpoint) =>
        endpoint.DisplayName?.Contains("ServerReflection", StringComparison.OrdinalIgnoreCase) == true
        || (endpoint as Microsoft.AspNetCore.Routing.RouteEndpoint)?.RoutePattern.RawText?.Contains("reflection", StringComparison.OrdinalIgnoreCase) == true;
}
