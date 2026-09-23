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
    ///
    /// <paramref name="authorizationPolicy"/> is opt-in: when it names an ASP.NET Core authorization policy,
    /// every call must satisfy it (an unauthorized caller gets <c>Unauthenticated</c>, never
    /// <c>Unimplemented</c> — that status means "capability disabled" on every adapter). Leave it
    /// <see langword="null"/> for the loopback development setup.
    /// </summary>
    public static IServiceCollection AddAsonGrpcBridge(this IServiceCollection services, AsonBridgeRuntime runtime, string? authorizationPolicy = null) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));
        services.AddSingleton(runtime);
        services.AddSingleton(new AsonGrpcBridgeOptions { AuthorizationPolicy = authorizationPolicy });
        services.AddGrpc();
        return services;
    }

    /// <summary>Maps the bridge service onto the endpoint route builder, applying the configured policy.</summary>
    public static IEndpointRouteBuilder MapAsonGrpcBridge(this IEndpointRouteBuilder endpoints) {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        var builder = endpoints.MapGrpcService<GrpcAsonBridgeService>();
        var policy = endpoints.ServiceProvider.GetService<AsonGrpcBridgeOptions>()?.AuthorizationPolicy;
        if (!string.IsNullOrWhiteSpace(policy)) builder.RequireAuthorization(policy);
        return endpoints;
    }
}

/// <summary>Registration-time settings of the gRPC adapter.</summary>
public sealed class AsonGrpcBridgeOptions {
    /// <summary>ASP.NET Core authorization policy every call must satisfy; <see langword="null"/> = open.</summary>
    public string? AuthorizationPolicy { get; set; }
}
