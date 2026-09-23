using System.Net;
using System.Net.Sockets;
using Ason.Bridge.Grpc;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Hosts a bridge over gRPC inside the test process, the same way an application would: a runtime built on
/// this thread, registered, and mapped onto a Kestrel endpoint that speaks HTTP/2 without TLS.
/// </summary>
internal sealed class BridgeGrpcHost : IAsyncDisposable {

    WebApplication? _app;

    public string Url { get; private set; } = string.Empty;

    /// <summary>
    /// Starts a bridge. When <paramref name="authorizationPolicy"/> is set, the test authentication scheme is
    /// registered and the endpoint requires that policy.
    /// </summary>
    public static async Task<BridgeGrpcHost> StartAsync(AsonBridgeRuntime runtime, string? authorizationPolicy = null) {
        var port = FreePort();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel => kestrel.ListenLocalhost(port, endpoint => endpoint.Protocols = HttpProtocols.Http2));
        if (authorizationPolicy is not null) builder.Services.AddTestAuth();
        builder.Services.AddAsonGrpcBridge(runtime, authorizationPolicy);

        var app = builder.Build();
        if (authorizationPolicy is not null) {
            app.UseAuthentication();
            app.UseAuthorization();
        }
        app.MapAsonGrpcBridge();
        await app.StartAsync();

        return new BridgeGrpcHost { _app = app, Url = $"http://localhost:{port}" };
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
