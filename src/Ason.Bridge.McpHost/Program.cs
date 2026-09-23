using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Relay: republishes an application's gRPC bridge as MCP over stdio.
//
// Some agents (Claude Desktop, Claude Code, ...) can only speak MCP by starting a process, while the
// application they should drive is already running and owns the operators. This host is the adapter between
// the two: it connects to the application's gRPC bridge, reads its manifest, and serves the very same tool
// surface over stdin/stdout. Nothing about the application's operators is duplicated here.
//
// Usage:  Ason.Bridge.McpHost --url http://localhost:5222
//         (or set ASON_BRIDGE_URL)

var url = Argument("--url") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_URL");
if (string.IsNullOrWhiteSpace(url)) {
    Console.Error.WriteLine("usage: Ason.Bridge.McpHost --url <application gRPC bridge url>");
    Console.Error.WriteLine("       ASON_BRIDGE_URL may be used instead of --url");
    return 2;
}

await using var client = GrpcAsonBridgeClient.Connect(url);
var endpoint = new GrpcAsonBridgeEndpoint(client);
try {
    // Fail fast and loudly: an MCP client that starts this process needs to know immediately whether the
    // application is reachable, instead of seeing tools that always fail.
    var manifest = await endpoint.GetManifestAsync();
    Console.Error.WriteLine($"[ason-bridge-mcphost] connected to '{manifest.AppName}' over gRPC (execution: {manifest.Execution})");
}
catch (Exception ex) {
    Console.Error.WriteLine($"[ason-bridge-mcphost] cannot reach the bridge at {url}: {ex.Message}");
    return 3;
}

var builder = Host.CreateApplicationBuilder(args);
// stdout carries the MCP protocol; any logging there would corrupt it.
builder.Logging.ClearProviders();
builder.Services.AddAsonMcpStdioBridge(endpoint);

await builder.Build().RunAsync();
return 0;

string? Argument(string name) {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}
