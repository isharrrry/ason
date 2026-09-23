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
    ///
    /// <paramref name="enableReflection"/> is off by default and is the less safe switch of the two: server
    /// reflection lets a caller discover the whole contract with no local <c>.proto</c> file, which is exactly
    /// what <c>grpcurl</c> and generated stubs want - and also a second publication of the callable surface.
    /// Turn it on for development or for callers you trust, and pair it with
    /// <paramref name="authorizationPolicy"/> when the bridge is not loopback-only. What a caller may *call* is
    /// still decided by the manifest and the capabilities.
    /// </summary>
    public static IServiceCollection AddAsonGrpcBridge(this IServiceCollection services, AsonBridgeRuntime runtime, string? authorizationPolicy = null, bool enableReflection = false) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));
        services.AddSingleton(runtime);
        services.AddSingleton(new AsonGrpcBridgeOptions { AuthorizationPolicy = authorizationPolicy, EnableReflection = enableReflection });
        services.AddGrpc();
        // Cheap and inert on its own; the service is only reachable once the mapping below publishes it.
        services.AddGrpcReflection();
        return services;
    }

    /// <summary>
    /// Maps the bridge service onto the endpoint route builder, applying the configured policy.
    ///
    /// <paramref name="enableReflection"/> overrides what registration decided; <see langword="null"/> keeps it.
    /// When reflection is on it carries the same authorization policy as the service, so a caller that may not
    /// call the bridge may not read its contract either.
    /// </summary>
    public static IEndpointRouteBuilder MapAsonGrpcBridge(this IEndpointRouteBuilder endpoints, bool? enableReflection = null) {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        var options = endpoints.ServiceProvider.GetService<AsonGrpcBridgeOptions>();
        var hasPolicy = !string.IsNullOrWhiteSpace(options?.AuthorizationPolicy);

        var builder = endpoints.MapGrpcService<GrpcAsonBridgeService>();
        if (hasPolicy) builder.RequireAuthorization(options!.AuthorizationPolicy!);

        if (enableReflection ?? options?.EnableReflection ?? false) {
            var reflection = endpoints.MapGrpcReflectionService();
            if (hasPolicy) reflection.RequireAuthorization(options!.AuthorizationPolicy!);
        }

        return endpoints;
    }
}

/// <summary>Registration-time settings of the gRPC adapter.</summary>
public sealed class AsonGrpcBridgeOptions {
    /// <summary>ASP.NET Core authorization policy every call must satisfy; <see langword="null"/> = open.</summary>
    public string? AuthorizationPolicy { get; set; }

    /// <summary>Whether the gRPC reflection service is published. Off by default.</summary>
    public bool EnableReflection { get; set; }
}
