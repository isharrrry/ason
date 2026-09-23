using System.Net;
using System.Net.Sockets;
using Ason.Bridge;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using LibDemo;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// The application side of a split deployment, in its smallest useful form: no agent, no model, no chat -
// only [Ason*] marked operators (here from the LibDemo class library) and the services an agent connects to.
//
//   gRPC  -> http://localhost:<port>       (ConsoleGrpcBridgeDemo, or any gRPC client)
//   MCP   -> http://localhost:<port+1>/mcp (an MCP-speaking agent, or the stdio relay host)
//
// Usage: ConsoleGrpcBridgeHost [--port 5222]

var port = ParsePort() ?? 5222;
var mcpPort = port + 1;

var runtime = new AsonBridgeRuntime(new AsonBridgeOptions {
    AppName = "LibDemo application",
    Assemblies = new[] { typeof(LibDemoOperator).Assembly },
    Execution = AsonBridgeExecution.InProcess,
    // LibDemoOperator is a marker-only operator: it has no view to attach to, so the host materialises it
    // once and the bridge addresses it by type name.
    SingletonOperators = AsonBridgeOperators.MaterializeMarkerOnly(typeof(LibDemoOperator).Assembly),
    // Scripts arriving over a transport get the same keyword filter a local client would apply. Passing the
    // list explicitly opts in; leaving it null would only reject empty scripts.
    ForbiddenScriptKeywords = new[] { "System.IO", "System.Reflection", "Process.Start", "DllImport", "Environment.GetEnvironmentVariable" }
});

var builder = WebApplication.CreateBuilder(args);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.WebHost.ConfigureKestrel(kestrel => {
    // gRPC needs HTTP/2, which without TLS means a dedicated HTTP/2-only listener (h2c).
    kestrel.ListenLocalhost(port, endpoint => endpoint.Protocols = HttpProtocols.Http2);
    // MCP speaks Streamable HTTP, which is ordinary HTTP request/response, so HTTP/1.1 is enough - and
    // asking for both protocols on a cleartext port would only make Kestrel warn that it cannot do that.
    kestrel.ListenLocalhost(mcpPort, endpoint => endpoint.Protocols = HttpProtocols.Http1);
});
builder.Services.AddAsonGrpcBridge(runtime);
builder.Services.AddAsonMcpBridge(runtime);

var app = builder.Build();
app.MapAsonGrpcBridge();
app.MapAsonMcpBridge();

await app.StartAsync();

var manifest = await runtime.GetManifestAsync();
Console.WriteLine($"ASON application bridge listening.");
Console.WriteLine($"  gRPC : http://localhost:{port}");
Console.WriteLine($"  MCP  : http://localhost:{mcpPort}/mcp");
Console.WriteLine($"  {manifest.Api.Operators.Count} operators, {manifest.Api.MethodCount} methods, execution {manifest.Execution}");
Console.WriteLine("Press Ctrl+C to stop.");

await app.WaitForShutdownAsync();
await runtime.DisposeAsync();
return 0;

int? ParsePort() {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, "--port");
    if (index < 0 || index + 1 >= args.Length) return null;
    if (!int.TryParse(args[index + 1], out var value) || value is < 0 or > 65535) {
        Console.Error.WriteLine("--port expects a number between 0 and 65535");
        return null;
    }
    return value == 0 ? FreePort() : value;
}

static int FreePort() {
    var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    var free = ((IPEndPoint)listener.LocalEndpoint).Port;
    listener.Stop();
    return free;
}
