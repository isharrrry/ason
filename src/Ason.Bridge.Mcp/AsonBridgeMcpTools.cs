using System.Text.Json;
using ModelContextProtocol.Server;

namespace Ason.Bridge.Mcp;

/// <summary>The script prompt layer and the Markdown listing, as one payload.</summary>
public sealed record AsonBridgeScriptApi(string Proxies, string Signatures, string Markdown);

/// <summary>
/// The MCP tool surface of a bridge. Tools are registered only for the capabilities that are enabled, so an
/// MCP client that lists tools sees the truth: switch the single-function interface off and the tool simply
/// is not there.
///
/// Every tool answers with the same JSON envelope <see cref="AsonBridgeCallResult"/> carries, which keeps the
/// failure vocabulary identical to the gRPC adapter.
/// </summary>
public static class AsonBridgeMcpTools {

    public const string GetManifest = "ason_get_manifest";
    public const string GetScriptApi = "ason_get_script_api";
    public const string ListInstances = "ason_list_instances";
    public const string ExecuteScript = "ason_execute_script";
    public const string InvokeFunction = "ason_invoke_function";

    static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>Builds the tool list for <paramref name="runtime"/> from its effective capabilities.</summary>
    public static IReadOnlyList<McpServerTool> Create(IAsonBridgeEndpoint runtime) {
        if (runtime is null) throw new ArgumentNullException(nameof(runtime));
        var capabilities = runtime.Options.Capabilities;
        var tools = new List<McpServerTool>();

        if (capabilities.ListApis) {
            tools.Add(McpServerTool.Create(
                async (CancellationToken cancellationToken) => JsonSerializer.Serialize(await runtime.GetManifestAsync(cancellationToken).ConfigureAwait(false), Json),
                new McpServerToolCreateOptions {
                    Name = GetManifest,
                    ReadOnly = true,
                    Description = "Reports the application's ASON contract as JSON: protocol version, execution location, enabled capabilities, the operator API and the live operator instances."
                }));

            tools.Add(McpServerTool.Create(
                async (CancellationToken cancellationToken) => JsonSerializer.Serialize(await runtime.ListInstancesAsync(cancellationToken).ConfigureAwait(false), Json),
                new McpServerToolCreateOptions {
                    Name = ListInstances,
                    ReadOnly = true,
                    Description = "Lists the operator instances that are alive right now, with the handle to address each one."
                }));

            tools.Add(McpServerTool.Create(
                async (CancellationToken cancellationToken) => {
                    var manifest = await runtime.GetManifestAsync(cancellationToken).ConfigureAwait(false);
                    return JsonSerializer.Serialize(new AsonBridgeScriptApi(manifest.Proxies, manifest.Signatures, manifest.Markdown), Json);
                },
                new McpServerToolCreateOptions {
                    Name = GetScriptApi,
                    ReadOnly = true,
                    Description = "Returns the generated proxy layer ('proxies'), the signature listing ('signatures') and the Markdown listing ('markdown') a caller needs in order to write a script for this application."
                }));
        }

        if (capabilities.ExecuteScript) {
            tools.Add(McpServerTool.Create(
                // See the note on ason_invoke_function: a parameter without a default value is required.
                async (string code, bool? includeProxyPreamble = null, CancellationToken cancellationToken = default) => JsonSerializer.Serialize(
                    await runtime.ExecuteScriptAsync(code, includeProxyPreamble ?? true, cancellationToken).ConfigureAwait(false), Json),
                new McpServerToolCreateOptions {
                    Name = ExecuteScript,
                    Destructive = true,
                    Description = "Runs a complete ASON script body against the application and returns its result. The proxy layer is prepended unless includeProxyPreamble is false. Answers with { success, result, error, errorCode }."
                }));
        }

        if (capabilities.InvokeFunction) {
            tools.Add(McpServerTool.Create(
                // The optional parameters carry default values on purpose: a tool parameter without one is
                // required, and a client that omits 'handle' (or sends it as null, which some transports
                // normalise to "absent") would then be rejected before the call reaches the application.
                async (string @operator, string method, string? handle = null, string? argumentsJson = null, CancellationToken cancellationToken = default) => JsonSerializer.Serialize(
                    await runtime.InvokeFunctionAsync(
                        new AsonBridgeFunctionCall(@operator, method, string.IsNullOrEmpty(handle) ? null : handle, ParseArguments(argumentsJson)),
                        cancellationToken).ConfigureAwait(false), Json),
                new McpServerToolCreateOptions {
                    Name = InvokeFunction,
                    Destructive = true,
                    Description = "Calls exactly one operator method, without generating a script. 'argumentsJson' is a JSON array such as \"[2, 3]\"; 'handle' is only needed when several live instances of the operator exist."
                }));
        }

        return tools;
    }

    static IReadOnlyList<JsonElement> ParseArguments(string? argumentsJson) {
        if (string.IsNullOrWhiteSpace(argumentsJson)) return Array.Empty<JsonElement>();
        using var document = JsonDocument.Parse(argumentsJson);
        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<JsonElement>();
        return document.RootElement.EnumerateArray().Select(e => e.Clone()).ToArray();
    }
}
