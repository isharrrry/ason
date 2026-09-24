using System.Net;
using System.Net.Sockets;
using Ason.Bridge;
using Ason.Bridge.Grpc;
#if ASON_MCP
using Ason.Bridge.Mcp;
#endif
using Ason.Bridge.OpenApi;
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
//   gRPC  -> http://localhost:<port>       (ConsoleBridgeCallerSample, or any gRPC client)
//   MCP   -> http://localhost:<port+1>/mcp (an MCP-speaking agent, or the stdio relay host)
//
// Usage: ConsoleBridgeAppSample [--port 5222] [--execution inprocess|external|remote] [--remote-url <url>] [--reflection]
//
//   inprocess (default) - scripts are evaluated in this process, so they can touch the operators directly
//   external            - scripts are evaluated in an Ason.ExternalExecutor child process, which calls back
//                         here for every operator invocation; use it when generated code must not run inside
//                         the application process. The manifest reports which one is in use.
//   remote              - that child process runs on a runner host instead of next to the application
//                         (--remote-url / ASON_BRIDGE_REMOTE_URL; see samples/RemoteRunnerService)
//   --reflection        - publish the gRPC reflection service, so grpcurl and generated stubs work with no
//                         local .proto file. Off by default: reflection republishes the callable surface.

var port = ParsePort() ?? 5222;
var mcpPort = port + 1;

var execution = (Value("--execution") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_EXECUTION") ?? "inprocess").ToLowerInvariant();
if (execution is not ("inprocess" or "external" or "remote")) {
    Console.Error.WriteLine($"unknown execution '{execution}'; expected 'inprocess', 'external' or 'remote'");
    return 2;
}

var remoteUrl = Value("--remote-url") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_REMOTE_URL");
if (execution == "remote" && string.IsNullOrWhiteSpace(remoteUrl)) {
    Console.Error.WriteLine("--execution remote needs --remote-url <runner url> (or ASON_BRIDGE_REMOTE_URL)");
    return 2;
}

var runtime = new AsonBridgeRuntime(new AsonBridgeOptions {
    AppName = "LibDemo application",
    Assemblies = new[] { typeof(LibDemoOperator).Assembly },
    Execution = execution switch {
        "external" => AsonBridgeExecution.ExternalProcess,
        "remote" => AsonBridgeExecution.RemoteRunner,
        _ => AsonBridgeExecution.InProcess
    },
    RemoteRunnerBaseUrl = remoteUrl,
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
builder.Services.AddAsonGrpcBridge(runtime, enableReflection: Has("--reflection"));
#if ASON_MCP
builder.Services.AddAsonMcpBridge(runtime);
#endif
// The same contract, for generic HTTP clients and Swagger UI. Adding a transport is one line per side.
builder.Services.AddAsonOpenApiBridge(runtime);

var app = builder.Build();
app.MapAsonGrpcBridge();
#if ASON_MCP
app.MapAsonMcpBridge();
#endif
app.MapAsonOpenApiBridge();

await app.StartAsync();

var manifest = await runtime.GetManifestAsync();
Console.WriteLine($"ASON application bridge listening.");
Console.WriteLine($"  gRPC : http://localhost:{port}");
#if ASON_MCP
Console.WriteLine($"  MCP  : http://localhost:{mcpPort}/mcp");
#else
Console.WriteLine($"  MCP  : not embedded in this build (no official MCP SDK below net8) - an MCP agent can still");
Console.WriteLine($"         drive this application through the stdio relay: Ason.Bridge.McpHost --url http://localhost:{port}");
#endif
Console.WriteLine($"  HTTP : http://localhost:{mcpPort}/ason/openapi.json");
Console.WriteLine($"  {manifest.Api.Operators.Count} operators, {manifest.Api.MethodCount} methods, execution {manifest.Execution}");
// One line that says "the bridge is up and this is what it is", so a script or a test can wait for it.
#if ASON_MCP
Console.WriteLine($"ASON_BRIDGE_READY grpc=http://localhost:{port} mcp=http://localhost:{mcpPort}/mcp execution={manifest.Execution} app={manifest.AppName} protocol={manifest.ProtocolVersion}");
#else
Console.WriteLine($"ASON_BRIDGE_READY grpc=http://localhost:{port} mcp=none execution={manifest.Execution} app={manifest.AppName} protocol={manifest.ProtocolVersion}");
#endif
Console.WriteLine("Press Ctrl+C to stop.");

await app.WaitForShutdownAsync();
await runtime.DisposeAsync();
return 0;

string? Value(string name) {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool Has(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

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
