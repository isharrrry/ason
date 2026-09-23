using Ason.Bridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ason.Bridge.Grpc;

/// <summary>
/// Wiring for the gRPC adapter. The host owns the runtime (it holds the operators), so it builds it first and
/// hands it over - that is what keeps operator instances and UI-thread affinity with the application.
/// </summary>
public static class AsonBridgeGrpcExtensions {

    /// <summary>
    /// Registers <paramref name="runtime"/> for the gRPC service and adds gRPC. The endpoint must be
    /// configured for HTTP/2 without TLS when the bridge is served over plain HTTP.
    /// </summary>
    public static IServiceCollection AddAsonGrpcBridge(this IServiceCollection services, AsonBridgeRuntime runtime) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));
        services.AddSingleton(runtime);
        services.AddGrpc();
        return services;
    }

    /// <summary>Maps the bridge service onto the endpoint route builder.</summary>
    public static IEndpointRouteBuilder MapAsonGrpcBridge(this IEndpointRouteBuilder endpoints) {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        endpoints.MapGrpcService<GrpcAsonBridgeService>();
        return endpoints;
    }
}
