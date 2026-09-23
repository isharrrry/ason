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

    /// <summary>Publishes the bridge over Streamable HTTP; map it with <see cref="MapAsonMcpBridge"/>.</summary>
    public static IServiceCollection AddAsonMcpBridge(this IServiceCollection services, IAsonBridgeEndpoint endpoint) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));
        services.AddSingleton(endpoint);
        services.AddMcpServer()
            .WithHttpTransport(_ => { })
            .WithTools(AsonBridgeMcpTools.Create(endpoint));
        return services;
    }

    /// <summary>Maps the MCP endpoint (Streamable HTTP).</summary>
    public static IEndpointRouteBuilder MapAsonMcpBridge(this IEndpointRouteBuilder endpoints, string pattern = "/mcp") {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        endpoints.MapMcp(pattern);
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
