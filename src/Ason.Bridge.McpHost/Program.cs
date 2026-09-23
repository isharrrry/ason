using Ason.Bridge;
using Ason.Bridge.Grpc;
using Ason.Bridge.Mcp;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

// Relay: republishes an application's bridge as MCP over stdio.
//
// Some agents (Claude Desktop, Claude Code, ...) can only speak MCP by starting a process, while the
// application they should drive is already running and owns the operators. This host is the adapter between
// the two: it connects to the application, reads its manifest, and serves the very same tool surface over
// stdin/stdout. Nothing about the application's operators is duplicated here.
//
// The channel to the application is the caller's choice:
//   gRPC (default)   Ason.Bridge.McpHost --url http://localhost:5222
//   MCP              Ason.Bridge.McpHost --url http://localhost:5223/mcp --transport mcp
//   (ASON_BRIDGE_URL and ASON_BRIDGE_TRANSPORT may be used instead of --url / --transport)
//
// When the application requires authorization, the relay carries the caller's credentials itself:
//   --key <value>              shorthand for "--header X-Ason-Bridge-Key=<value>" (ASON_BRIDGE_KEY)
//   --header <Name>=<value>    repeatable; any header the application's policy expects

var url = Argument("--url") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_URL");
if (string.IsNullOrWhiteSpace(url)) {
    Console.Error.WriteLine("usage: Ason.Bridge.McpHost --url <application bridge url> [--transport grpc|mcp] [--key <value> | --header Name=Value ...]");
    Console.Error.WriteLine("       ASON_BRIDGE_URL, ASON_BRIDGE_TRANSPORT and ASON_BRIDGE_KEY may be used instead");
    return 2;
}

var transport = (Argument("--transport") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_TRANSPORT") ?? "grpc").Trim().ToLowerInvariant();
if (transport is not ("grpc" or "mcp")) {
    Console.Error.WriteLine($"unknown transport '{transport}'; expected 'grpc' or 'mcp'");
    return 2;
}

var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
var key = Argument("--key") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_KEY");
if (!string.IsNullOrWhiteSpace(key)) headers["X-Ason-Bridge-Key"] = key;
foreach (var header in Headers("--header")) {
    var separator = header.IndexOf('=');
    if (separator <= 0) {
        Console.Error.WriteLine($"--header expects Name=Value, got '{header}'");
        return 2;
    }
    headers[header[..separator]] = header[(separator + 1)..];
}
var effectiveHeaders = headers.Count > 0 ? headers : null;
if (effectiveHeaders is not null) {
    Console.Error.WriteLine($"[ason-bridge-mcphost] sending {effectiveHeaders.Count} header(s) with every request");
}

IAsonBridgeEndpoint endpoint;
IAsyncDisposable? connection = null;
try {
    if (transport == "mcp") {
        var client = await McpAsonBridgeClient.ConnectAsync(url, effectiveHeaders);
        connection = client;
        endpoint = new McpAsonBridgeEndpoint(client);
    }
    else {
        var client = GrpcAsonBridgeClient.Connect(url, effectiveHeaders);
        connection = client;
        endpoint = new GrpcAsonBridgeEndpoint(client);
    }
}
catch (Exception ex) {
    Console.Error.WriteLine($"[ason-bridge-mcphost] cannot reach the bridge at {url}: {ex.Message}");
    return 3;
}

try {
    // Fail fast and loudly: an MCP client that starts this process needs to know immediately whether the
    // application is reachable, instead of seeing tools that always fail.
    var manifest = await endpoint.GetManifestAsync();
    Console.Error.WriteLine($"[ason-bridge-mcphost] connected to '{manifest.AppName}' over {transport} (execution: {manifest.Execution})");
}
catch (Exception ex) {
    Console.Error.WriteLine($"[ason-bridge-mcphost] cannot reach the bridge at {url}: {ex.Message}");
    if (connection is not null) await connection.DisposeAsync();
    return 3;
}

var builder = Host.CreateApplicationBuilder(args);
// stdout carries the MCP protocol and nothing else. Diagnostics therefore go to stderr, where the agent shows
// them as this server's log - and where a failing tool call is finally visible instead of being reduced to
// "An error occurred invoking ..." on the client side.
builder.Logging.ClearProviders();
builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddAsonMcpStdioBridge(endpoint);

await builder.Build().RunAsync();
if (connection is not null) await connection.DisposeAsync();
return 0;

string? Argument(string name) {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

// Every occurrence of the switch, so headers can be repeated.
IEnumerable<string> Headers(string name) {
    var args = Environment.GetCommandLineArgs();
    for (var index = 0; index < args.Length - 1; index++) {
        if (string.Equals(args[index], name, StringComparison.Ordinal)) yield return args[index + 1];
    }
}
