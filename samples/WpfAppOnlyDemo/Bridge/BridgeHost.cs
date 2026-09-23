using Ason.Bridge;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Ason.Bridge.OpenApi;
using LibDemo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace WpfAppOnlyDemo.Bridge;

/// <summary>
/// Hosts the two bridge services inside the application process.
///
/// Everything that matters about the split deployment is decided here: the operators stay in this process,
/// the runtime is built on the UI thread (so operator calls are marshalled to the dispatcher), and the agent
/// only ever sees the services.
/// </summary>
internal sealed class BridgeHost : IDisposable {

    readonly WebApplication _app;
    readonly AsonBridgeRuntime _runtime;

    BridgeHost(WebApplication app, AsonBridgeRuntime runtime, string grpcUrl, string mcpUrl, string openApiUrl) {
        _app = app;
        _runtime = runtime;
        GrpcUrl = grpcUrl;
        McpUrl = mcpUrl;
        OpenApiUrl = openApiUrl;
    }

    public string GrpcUrl { get; }

    public string McpUrl { get; }

    public static BridgeHost Start(MainWindow window, int grpcPort, int mcpPort, AsonBridgeExecution execution = AsonBridgeExecution.InProcess) {
        // The application's own assembly carries the view operators; LibDemo proves that a class library which
        // only uses the markers participates in the same API.
        var assemblies = new[] { typeof(EmployeesOperator).Assembly, typeof(LibDemoOperator).Assembly };

        var runtime = new AsonBridgeRuntime(new AsonBridgeOptions {
            AppName = "WpfAppOnlyDemo",
            Assemblies = assemblies,
            // In-process is the default because these operators touch UI-bound state, which the runtime marshals
            // to the captured synchronization context - the dispatcher, captured right here. ExternalProcess is
            // offered too: a desktop application is exactly the case where generated code should not run inside
            // its own process, and operator calls still come back here, so UI affinity is unaffected.
            Execution = execution,
            CaptureSynchronizationContext = true,
            OperatorInstances = window.Operator.OperatorInstances,
            SingletonOperators = AsonBridgeOperators.MaterializeMarkerOnly(assemblies),
            ForbiddenScriptKeywords = new[] {
                "System.IO", "System.Reflection", "Process.Start", "DllImport",
                "System.Runtime.InteropServices", "Environment.GetEnvironmentVariable", "System.Net.Http"
            }
        });

        runtime.Log += (_, e) => window.AppendActivity($"[{e.Level}] {e.Message}");

        var builder = WebApplication.CreateBuilder();
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.ConfigureKestrel(kestrel => {
            // gRPC needs HTTP/2, which without TLS requires a dedicated listener.
            kestrel.ListenLocalhost(grpcPort, endpoint => endpoint.Protocols = HttpProtocols.Http2);
            // MCP speaks Streamable HTTP, which is ordinary HTTP.
            kestrel.ListenLocalhost(mcpPort, endpoint => endpoint.Protocols = HttpProtocols.Http1);
        });
        builder.Services.AddAsonGrpcBridge(runtime);
        builder.Services.AddAsonMcpBridge(runtime);
        // A third transport for generic HTTP clients and Swagger UI, mapped on the MCP listener below.
        builder.Services.AddAsonOpenApiBridge(runtime);

        var app = builder.Build();
        app.MapAsonGrpcBridge();
        app.MapAsonMcpBridge();
        app.MapAsonOpenApiBridge();
        app.Start();

        var host = new BridgeHost(app, runtime, $"http://localhost:{grpcPort}", $"http://localhost:{mcpPort}/mcp", $"http://localhost:{mcpPort}/ason/openapi.json") {
            Execution = execution
        };
        return host;
    }

    public string OpenApiUrl { get; }

    /// <summary>Where scripts are evaluated; the manifest reports it, so a caller reads it there.</summary>
    public AsonBridgeExecution Execution { get; private set; }

    public void Dispose() {
        try { _app.StopAsync().GetAwaiter().GetResult(); } catch { }
        try { _app.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
        try { _runtime.DisposeAsync().AsTask().GetAwaiter().GetResult(); } catch { }
    }
}
