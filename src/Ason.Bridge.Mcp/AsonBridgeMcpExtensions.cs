using Ason.Bridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ason.Bridge.Mcp;

/// <summary>
/// Wiring for the MCP adapter. Two hostings are supported, both using the same tool surface:
/// Streamable HTTP inside the application (see <see cref="AddAsonMcpBridge"/>) and stdio for agents that can
/// only launch a process (see <see cref="AddAsonMcpStdioBridge"/>, used by the relay host).
///
/// Both take an <see cref="IAsonBridgeEndpoint"/>, so the same adapter serves an application that owns its
/// operators and a relay that forwards to another bridge.
/// </summary>
public static class AsonBridgeMcpExtensions {

    /// <summary>
    /// Publishes the bridge over Streamable HTTP; map it with <see cref="MapAsonMcpBridge"/>.
    /// <paramref name="requireAuthorization"/> is opt-in: when set, the endpoint requires an authenticated
    /// caller (401 otherwise). Leave it <see langword="false"/> for the loopback development setup.
    /// </summary>
    public static IServiceCollection AddAsonMcpBridge(this IServiceCollection services, IAsonBridgeEndpoint endpoint, bool requireAuthorization = false) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        services.AddSingleton(endpoint);
        services.AddSingleton(new AsonMcpBridgeOptions { RequireAuthorization = requireAuthorization });
        services.AddMcpServer()
            .WithHttpTransport(_ => { })
            .WithTools(AsonBridgeMcpTools.Create(endpoint));
        return services;
    }

    /// <summary>Maps the MCP endpoint (Streamable HTTP), applying the configured authorization requirement.</summary>
    public static IEndpointRouteBuilder MapAsonMcpBridge(this IEndpointRouteBuilder endpoints, string pattern = "/mcp") {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        var builder = endpoints.MapMcp(pattern);
        if (endpoints.ServiceProvider.GetService<AsonMcpBridgeOptions>()?.RequireAuthorization == true) builder.RequireAuthorization();
        return endpoints;
    }

    /// <summary>
    /// Serves the bridge over stdin/stdout, for agents that start the bridge themselves. Intended for the
    /// relay host, which connects to an application's gRPC bridge and republishes it as MCP.
    /// </summary>
    public static IServiceCollection AddAsonMcpStdioBridge(this IServiceCollection services, IAsonBridgeEndpoint endpoint) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        services.AddSingleton(endpoint);
        services.AddMcpServer()
            .WithStdioServerTransport()
            .WithTools(AsonBridgeMcpTools.Create(endpoint));
        return services;
    }
}

/// <summary>Registration-time settings of the MCP adapter.</summary>
public sealed class AsonMcpBridgeOptions {
    /// <summary>When set, the HTTP endpoint requires an authenticated caller.</summary>
    public bool RequireAuthorization { get; set; }
}
