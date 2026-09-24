using System.Reflection;
using Ason;
using Ason.Bridge;
using Ason.Bridge.Grpc;
#if ASON_MCP
using Ason.Bridge.Mcp;
#endif
using AsonRunner;
using Microsoft.SemanticKernel.ChatCompletion;

// The agent side of a split deployment, as a console program.
//
// It owns no operators and scans no assemblies: the operator API comes from the application, over gRPC or MCP.
// That makes it the cross-platform counterpart of the WPF agent sample - it runs on Linux and in CI as well.
//
//   ConsoleAgentSample --url http://localhost:5222 --list                    # no model key needed
//   ConsoleAgentSample --url http://localhost:5223/mcp --transport mcp --list
//   ConsoleAgentSample --url http://localhost:5222 --send "add 20 and 22"    # needs MY_OPEN_AI_KEY
//   ConsoleAgentSample --url http://localhost:5222 --send "rename employee 1 to Ada"
//
// The net6.0 leg has no MCP client (the official SDK needs net8+, see src/Ason.Bridge.Mcp). It says so instead
// of pretending: --transport mcp stops with a message naming the two working alternatives.
const string McpUnavailable = "this build has no MCP client (the official MCP SDK requires net8+; this is the net6.0 leg). Use --transport grpc, or drive the application through the stdio relay (Ason.Bridge.McpHost).";

var url = Value("--url") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_URL") ?? "http://localhost:5222";
var transport = (Value("--transport") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_TRANSPORT") ?? "grpc").ToLowerInvariant();
var prompt = Value("--send");
var listOnly = Command("--list") || prompt is null;

if (transport is not ("grpc" or "mcp")) {
    Console.Error.WriteLine($"unknown transport '{transport}'; expected 'grpc' or 'mcp'");
    return 2;
}
if (transport == "mcp" && !McpSupported()) {
    Console.Error.WriteLine(McpUnavailable);
    return 2;
}

GrpcAsonBridgeClient? grpc = null;
#if ASON_MCP
McpAsonBridgeClient? mcp = null;
#endif
AsonBridgeManifest manifest;
try {
    if (transport == "mcp") {
#if ASON_MCP
        mcp = await McpAsonBridgeClient.ConnectAsync(url);
        manifest = await mcp.GetManifestAsync();
#else
        throw new PlatformNotSupportedException(McpUnavailable);
#endif
    }
    else {
        grpc = GrpcAsonBridgeClient.Connect(url);
        manifest = await grpc.GetManifestAsync();
    }
}
catch (Exception ex) {
    Console.Error.WriteLine($"cannot reach a bridge at {url} over {transport}: {ex.Message}");
    return 3;
}

try {
    Console.WriteLine($"app={manifest.AppName} transport={transport} execution={manifest.Execution} capabilities=script:{manifest.Capabilities.ExecuteScript},function:{manifest.Capabilities.InvokeFunction}");
    Console.WriteLine($"operators={string.Join(",", manifest.Api.Operators.Select(o => o.TypeName))}");
    Console.WriteLine($"methods={manifest.Api.MethodCount} instances={string.Join(",", manifest.Instances.Select(i => i.Handle))}");

    // The whole agent-side integration: the manifest becomes the operator library this client works against.
    var library = manifest.ToOperatorsLibrary();
    var (proxies, signatures, _) = await library.BuildTask;
    var declaredHere = typeof(Program).Assembly.GetTypes().Count(t => t.GetCustomAttribute<AsonOperatorAttribute>() is not null);
    Console.WriteLine($"agent-operators={declaredHere} library=manifest proxies={proxies.Length} signatures={signatures.Length}");

    if (listOnly) {
        Console.WriteLine($"hint: this side can now send a task with --send \"...\" (needs MY_OPEN_AI_KEY); the application evaluates it.");
        return 0;
    }

    IChatCompletionService chat;
    try {
        chat = OpenAiCompatibleChatServiceFactory.FromEnvironment();
    }
    catch (InvalidOperationException ex) when (ex.Message.Contains("MY_OPEN_AI_KEY", StringComparison.Ordinal)) {
        Console.Error.WriteLine("no model configured: set MY_OPEN_AI_KEY (and MY_OPEN_AI_BASE_URL / MY_OPEN_AI_MODEL).");
        Console.Error.WriteLine("Without a key this sample still works in --list mode, which needs no model.");
        return 3;
    }

    var options = new AsonClientOptions {
        // The isolation belongs to the application; this side only says how to reach it.
        ExecutionMode = ExecutionMode.ExternalProcess,
        TransportFactory = grpc is not null
            ? () => new GrpcAsonBridgeTransport(grpc)
#if ASON_MCP
            : () => new McpAsonBridgeTransport(mcp!),
#else
            : () => throw new PlatformNotSupportedException(McpUnavailable),
#endif
        ForbiddenScriptKeywords = new[] { "System.IO", "System.Reflection", "Process.Start", "DllImport" }
    };
    var agent = new AsonClient(chat, new RootOperator(new object()), library, options);
    agent.Log += (_, log) => Console.Error.WriteLine($"[{log.Level}] {log.Message}");

    Console.WriteLine($"--- task: {prompt}");
    await foreach (var chunk in agent.SendStreamingAsync(prompt!)) Console.Write(chunk);
    Console.WriteLine();
    return 0;
}
finally {
    if (grpc is not null) await grpc.DisposeAsync();
#if ASON_MCP
    if (mcp is not null) await mcp.DisposeAsync();
#endif
}

// Declared next to the other helpers so the transport check at the top can call it: on the net6.0 leg the MCP
// client is compiled out, and everything user-facing stays the same.
static bool McpSupported() {
#if ASON_MCP
    return true;
#else
    return false;
#endif
}

string? Value(string name) {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool Command(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;
