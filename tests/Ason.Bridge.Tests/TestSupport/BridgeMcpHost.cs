using System.Net;
using System.Net.Sockets;
using Ason.Bridge.Mcp;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Hosts a bridge over MCP (Streamable HTTP) inside the test process, the way an application would when it
/// wants an MCP-speaking agent - Claude Desktop, Claude Code, any MCP client - to drive it.
/// </summary>
internal sealed class BridgeMcpHost : IAsyncDisposable {

    WebApplication? _app;

    /// <summary>The MCP endpoint, including the path.</summary>
    public string Url { get; private set; } = string.Empty;

    public static async Task<BridgeMcpHost> StartAsync(AsonBridgeRuntime runtime) {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Services.AddAsonMcpBridge(runtime);

        var app = builder.Build();
        app.MapAsonMcpBridge();
        await app.StartAsync();

        return new BridgeMcpHost { _app = app, Url = $"http://localhost:{port}/mcp" };
    }

    public async ValueTask DisposeAsync() {
        if (_app is null) return;
        await _app.StopAsync();
        await _app.DisposeAsync();
    }

    static int FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
