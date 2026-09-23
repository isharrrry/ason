using System.Text.Json;
using System.Text.Json.Serialization;
using Ason.Bridge;
using Ason.Bridge.Grpc;

// A command-line client for an application's gRPC bridge: it is the "external request side" of the split
// deployment, and the shortest way to see the single-function interface work without any agent in the loop.
//
// Usage:
//   ConsoleBridgeCallerSample --url http://localhost:5222                    # manifest summary + live instances
//   ConsoleBridgeCallerSample --url ... --manifest                           # full manifest JSON
//   ConsoleBridgeCallerSample --url ... --instances                          # live instances only
//   ConsoleBridgeCallerSample --url ... --func BridgeStaticOperator.Add --args "[2,3]"
//   ConsoleBridgeCallerSample --url ... --func EmployeesViewOperator.GetEmployees
//   ConsoleBridgeCallerSample --url ... --script "return BridgeStaticOperator.Add(40, 2);"
//   ConsoleBridgeCallerSample --url ... --script "..." --stream              # stream the application's logs

var url = Argument("--url") ?? Environment.GetEnvironmentVariable("ASON_BRIDGE_URL");
if (string.IsNullOrWhiteSpace(url)) {
    Console.Error.WriteLine("usage: ConsoleBridgeCallerSample --url <application gRPC bridge url> [--manifest|--instances|--func Operator.Method [--args \"[..]\"]|--script \"code\" [--stream]]");
    return 2;
}

await using var client = GrpcAsonBridgeClient.Connect(url);
var manifest = await client.GetManifestAsync();
Console.WriteLine($"Connected to '{manifest.AppName}' (protocol {manifest.ProtocolVersion}, execution {manifest.Execution}).");
Console.WriteLine($"Capabilities: script={manifest.Capabilities.ExecuteScript} function={manifest.Capabilities.InvokeFunction} mcpTool={manifest.Capabilities.InvokeMcpTool} logs={manifest.Capabilities.LogStream}");

var script = Argument("--script");
var function = Argument("--func");

if (script is not null) {
    if (Flag("--stream")) {
        Console.WriteLine("--- execution ---");
        AsonBridgeCallResult? completion = null;
        await foreach (var evt in client.StreamExecutionAsync(script)) {
            if (evt.Type == "log") Console.WriteLine($"  [{evt.Level}] {evt.Message}");
            else completion = ToDomain(evt.Result);
        }
        return Report(completion);
    }
    return Report(await client.ExecuteScriptAsync(script));
}

if (function is not null) {
    var separator = function.LastIndexOf('.');
    if (separator <= 0 || separator == function.Length - 1) {
        Console.Error.WriteLine("--func expects 'Operator.Method', for example BridgeStaticOperator.Add");
        return 2;
    }
    var call = new AsonBridgeFunctionCall(
        function[..separator],
        function[(separator + 1)..],
        Argument("--handle"),
        ParseArguments(Argument("--args")));
    return Report(await client.InvokeFunctionAsync(call));
}

if (Flag("--manifest")) {
    Console.WriteLine(JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true }));
    return 0;
}

Console.WriteLine();
Console.WriteLine(Flag("--instances") ? "Live instances:" : $"{manifest.Api.Operators.Count} operators, {manifest.Api.MethodCount} methods, {manifest.Instances.Count} live instances:");
foreach (var instance in manifest.Instances) Console.WriteLine($"  {instance.Handle} ({instance.TypeName}, initialized={instance.Initialized})");
if (!Flag("--instances")) {
    Console.WriteLine();
    foreach (var op in manifest.Api.Operators) {
        Console.WriteLine($"  {(op.IsStatic ? "static " : string.Empty)}{op.TypeName}");
        foreach (var method in op.Methods) {
            var parameters = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
            Console.WriteLine($"      {method.ReturnType} {method.Name}({parameters})");
        }
    }
    Console.WriteLine();
    Console.WriteLine("Pass --manifest for the full payload, --func to call one function, --script to run a script.");
}
return 0;

string? Argument(string name) {
    var args = Environment.GetCommandLineArgs();
    var index = Array.IndexOf(args, name);
    return index >= 0 && index + 1 < args.Length ? args[index + 1] : null;
}

bool Flag(string name) => Array.IndexOf(Environment.GetCommandLineArgs(), name) >= 0;

IReadOnlyList<JsonElement> ParseArguments(string? json) {
    if (string.IsNullOrWhiteSpace(json)) return Array.Empty<JsonElement>();
    try {
        using var document = JsonDocument.Parse(json);
        if (document.RootElement.ValueKind != JsonValueKind.Array) {
            Console.Error.WriteLine("--args expects a JSON array, for example \"[2,3]\"");
            Environment.Exit(2);
        }
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }
    catch (JsonException ex) {
        Console.Error.WriteLine($"--args is not valid JSON: {ex.Message}");
        Environment.Exit(2);
        return Array.Empty<JsonElement>();
    }
}

int Report(AsonBridgeCallResult? result) {
    if (result is null) {
        Console.Error.WriteLine("The application ended the stream without a result.");
        return 1;
    }
    if (!result.Success) {
        Console.Error.WriteLine($"FAILED [{result.ErrorCode}] {result.Error}");
        return 1;
    }
    Console.WriteLine("OK " + (result.Result is { } element ? element.GetRawText() : "null"));
    return 0;
}

static AsonBridgeCallResult? ToDomain(Ason.Bridge.Grpc.ExecuteResult? result) {
    if (result is null) return null;
    if (!result.Success) return AsonBridgeCallResult.Fail(string.IsNullOrEmpty(result.ErrorCode) ? AsonBridgeErrorCodes.ExecutionFailed : result.ErrorCode, result.Error);
    if (string.IsNullOrEmpty(result.Json)) return AsonBridgeCallResult.Ok(null);
    using var document = JsonDocument.Parse(result.Json);
    return AsonBridgeCallResult.Ok(document.RootElement.Clone());
}
