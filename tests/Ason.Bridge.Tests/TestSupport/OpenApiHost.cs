using System.Net;
using System.Net.Sockets;
using Ason.Bridge.OpenApi;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>Hosts the HTTP/OpenAPI adapter in the test process, the way an application would.</summary>
internal sealed class OpenApiHost : IAsyncDisposable {

    WebApplication? _app;

    public string Url { get; private set; } = string.Empty;

    public static async Task<OpenApiHost> StartAsync(IAsonBridgeEndpoint endpoint, Action<AsonOpenApiBridgeOptions>? configure = null) {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls($"http://localhost:{port}");
        builder.Services.AddAsonOpenApiBridge(endpoint, configure);

        var app = builder.Build();
        app.MapAsonOpenApiBridge();
        await app.StartAsync();

        return new OpenApiHost { _app = app, Url = $"http://localhost:{port}" };
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
