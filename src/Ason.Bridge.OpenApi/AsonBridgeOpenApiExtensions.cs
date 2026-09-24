using System.Text.Json;
using Ason.Bridge;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Ason.Bridge.OpenApi;

/// <summary>
/// The HTTP face of the bridge: the same contract the gRPC and MCP adapters serve, reachable by any HTTP
/// client, plus an OpenAPI document generated from the manifest.
///
/// This adapter is also the proof that adding a transport is cheap: it depends on
/// <see cref="IAsonBridgeEndpoint"/> and nothing else, and it was added without touching the application, the
/// runtime or the other adapters.
/// </summary>
public static class AsonBridgeOpenApiExtensions {

    /// <summary>Registers the endpoint and its options; map the routes with <see cref="MapAsonOpenApiBridge"/>.</summary>
    public static IServiceCollection AddAsonOpenApiBridge(this IServiceCollection services, IAsonBridgeEndpoint endpoint, Action<AsonOpenApiBridgeOptions>? configure = null) {
        if (services is null) throw new ArgumentNullException(nameof(services));
        if (endpoint is null) throw new ArgumentNullException(nameof(endpoint));

        var options = new AsonOpenApiBridgeOptions();
        configure?.Invoke(options);

        services.AddSingleton(endpoint);
        services.AddSingleton(options);
        return services;
    }

    /// <summary>Maps the bridge routes under <paramref name="basePath"/>.</summary>
    public static IEndpointRouteBuilder MapAsonOpenApiBridge(this IEndpointRouteBuilder endpoints, string basePath = "/ason") {
        if (endpoints is null) throw new ArgumentNullException(nameof(endpoints));
        var root = basePath.TrimEnd('/');

        endpoints.MapGet($"{root}/openapi.json", async (HttpContext context, IAsonBridgeEndpoint endpoint, AsonOpenApiBridgeOptions options) => {
            if (!IsAuthorized(context, options)) return Results.Unauthorized();
            var manifest = await endpoint.GetManifestAsync(context.RequestAborted).ConfigureAwait(false);
            return Results.Text(AsonOpenApiDocument.BuildJson(manifest, options), "application/json");
        });

        endpoints.MapGet($"{root}/manifest", async (HttpContext context, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            var manifest = await endpoint.GetManifestAsync(context.RequestAborted).ConfigureAwait(false);
            return manifest.Capabilities.ListApis
                ? Results.Json(manifest)
                : NotSupported("listApis");
        });

        endpoints.MapGet($"{root}/instances", async (HttpContext context, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            var manifest = await endpoint.GetManifestAsync(context.RequestAborted).ConfigureAwait(false);
            if (!manifest.Capabilities.ListApis) return NotSupported("listApis");
            return Results.Json(await endpoint.ListInstancesAsync(context.RequestAborted).ConfigureAwait(false));
        });

        endpoints.MapPost($"{root}/script", async (HttpContext context, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            if (!endpoint.Options.Capabilities.ExecuteScript) return NotSupported("executeScript");

            var request = await ReadAsync<AsonOpenApiScriptRequest>(context).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.Code)) {
                return Results.BadRequest(new { success = false, errorCode = AsonBridgeErrorCodes.InvalidArguments, error = "A 'code' string is required." });
            }

            var result = await endpoint.ExecuteScriptAsync(request.Code, request.IncludeProxyPreamble ?? true, request.IncludeInstanceDeclarations ?? false, context.RequestAborted).ConfigureAwait(false);
            return Result(result);
        });

        endpoints.MapPost($"{root}/script/stream", async (HttpContext context, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            if (!endpoint.Options.Capabilities.ExecuteScript) return NotSupported("executeScript");
            if (!endpoint.Options.Capabilities.LogStream) return NotSupported("logStream");

            var request = await ReadAsync<AsonOpenApiScriptRequest>(context).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.Code)) {
                return Results.BadRequest(new { success = false, errorCode = AsonBridgeErrorCodes.InvalidArguments, error = "A 'code' string is required." });
            }

            // Server-sent events: one 'log' event per line the application produces while the script runs, then
            // exactly one 'result' or 'error'. The logs are pushed into a channel by the executor's event and
            // drained concurrently, so a log written while the script is still running is delivered then -
            // not batched after it finished.
            context.Response.Headers.ContentType = "text/event-stream";
            context.Response.Headers.CacheControl = "no-cache";
            context.Response.Headers["X-Accel-Buffering"] = "no";

            var channel = System.Threading.Channels.Channel.CreateUnbounded<AsonBridgeLogEventArgs>();
            void OnLog(object? sender, AsonBridgeLogEventArgs entry) => channel.Writer.TryWrite(entry);

            endpoint.Log += OnLog;
            var pump = Task.Run(async () => {
                await foreach (var entry in channel.Reader.ReadAllAsync(context.RequestAborted).ConfigureAwait(false)) {
                    await WriteEventAsync(context, "log", new { level = entry.Level, message = entry.Message, source = entry.Source, timestampUtc = entry.TimestampUtc }).ConfigureAwait(false);
                }
            }, CancellationToken.None);

            try {
                var result = await endpoint.ExecuteScriptAsync(request.Code, request.IncludeProxyPreamble ?? true, request.IncludeInstanceDeclarations ?? false, context.RequestAborted).ConfigureAwait(false);
                channel.Writer.TryComplete();
                await pump.ConfigureAwait(false);
                await WriteEventAsync(context, result.Success ? "result" : "error", result).ConfigureAwait(false);
            }
            finally {
                endpoint.Log -= OnLog;
                channel.Writer.TryComplete();
            }

            // Results.Empty is net7+; this is the same thing (an IResult that does nothing) and keeps one code path
            // for net6.0/net9.0/net10.0. It must be a no-op: the SSE body has already been written, so setting a
            // status code here throws "response has already started" and cuts the stream off mid-body.
            return EmptyResult.Instance;
        });

        endpoints.MapPost($"{root}/functions/invoke", async (HttpContext context, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            if (!endpoint.Options.Capabilities.InvokeFunction) return NotSupported("invokeFunction");

            var request = await ReadAsync<AsonOpenApiInvokeRequest>(context).ConfigureAwait(false);
            if (request is null || string.IsNullOrWhiteSpace(request.Operator) || string.IsNullOrWhiteSpace(request.Method)) {
                return Results.BadRequest(new { success = false, errorCode = AsonBridgeErrorCodes.InvalidArguments, error = "'operator' and 'method' are required." });
            }

            var result = await endpoint.InvokeFunctionAsync(ToCall(request.Operator, request.Method, request.Handle, request.Arguments), context.RequestAborted).ConfigureAwait(false);
            return Result(result);
        });

        endpoints.MapPost($"{root}/functions/{{operator}}/{{method}}", async (HttpContext context, string @operator, string method, IAsonBridgeEndpoint endpoint) => {
            if (!IsAuthorized(context, context.RequestServices.GetRequiredService<AsonOpenApiBridgeOptions>())) return Results.Unauthorized();
            if (!endpoint.Options.Capabilities.InvokeFunction) return NotSupported("invokeFunction");

            var request = await ReadAsync<AsonOpenApiArguments>(context).ConfigureAwait(false);
            var handle = request?.Handle ?? (context.Request.Query.TryGetValue("handle", out var query) ? query.ToString() : null);
            var result = await endpoint.InvokeFunctionAsync(ToCall(@operator, method, handle, request?.Arguments), context.RequestAborted).ConfigureAwait(false);
            return Result(result);
        });

        return endpoints;
    }

    static AsonBridgeFunctionCall ToCall(string @operator, string method, string? handle, JsonElement[]? arguments) =>
        new(@operator, method, string.IsNullOrEmpty(handle) ? null : handle, arguments ?? Array.Empty<JsonElement>());

    static IResult Result(AsonBridgeCallResult result) =>
        Results.Json(result, statusCode: result.Success ? StatusCodes.Status200OK : StatusCodes.Status400BadRequest);

    /// <summary>
    /// The net6.0 equivalent of net7's <c>Results.Empty</c>: a handler that has already written the response (the
    /// SSE stream) still has to return something, and that something must do nothing at all. Acting on the
    /// response here - even just setting the status code - throws "response has already started" and truncates
    /// the stream, which is what the HTTP log-streaming tests caught.
    /// </summary>
    sealed class EmptyResult : IResult {
        public static readonly EmptyResult Instance = new();
        public Task ExecuteAsync(HttpContext httpContext) => Task.CompletedTask;
    }

    /// <summary>Writes one server-sent event, flushing so a caller sees logs as they happen.</summary>
    static async Task WriteEventAsync(HttpContext context, string name, object payload) {
        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        await context.Response.WriteAsync($"event: {name}\ndata: {json}\n\n", context.RequestAborted).ConfigureAwait(false);
        await context.Response.Body.FlushAsync(context.RequestAborted).ConfigureAwait(false);
    }

    static IResult NotSupported(string capability) =>
        Results.Json(new { success = false, errorCode = AsonBridgeErrorCodes.NotSupported, error = $"The '{capability}' capability is disabled on this bridge." },
            statusCode: StatusCodes.Status404NotFound);

    static bool IsAuthorized(HttpContext context, AsonOpenApiBridgeOptions options) {
        if (string.IsNullOrEmpty(options.ApiKey)) return true;
        return context.Request.Headers.TryGetValue(options.ApiKeyHeader, out var provided) && provided == options.ApiKey;
    }

    static async Task<T?> ReadAsync<T>(HttpContext context) where T : class {
        if (context.Request.ContentLength is null or 0 && !context.Request.Headers.ContainsKey("Transfer-Encoding")) return null;
        try {
            return await JsonSerializer.DeserializeAsync<T>(context.Request.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web), context.RequestAborted).ConfigureAwait(false);
        }
        catch (JsonException) {
            return null;
        }
    }
}

/// <summary>Body of <c>POST /ason/script</c>.</summary>
public sealed class AsonOpenApiScriptRequest {
    public string? Code { get; set; }
    public bool? IncludeProxyPreamble { get; set; }
    /// <summary>Body-only mode: the application supplies its own proxy layer and current instance declarations.</summary>
    public bool? IncludeInstanceDeclarations { get; set; }
}

/// <summary>Body of <c>POST /ason/functions/invoke</c>.</summary>
public sealed class AsonOpenApiInvokeRequest {
    public string? Operator { get; set; }
    public string? Method { get; set; }
    public string? Handle { get; set; }
    public JsonElement[]? Arguments { get; set; }
}

/// <summary>Body of <c>POST /ason/functions/{operator}/{method}</c>.</summary>
public sealed class AsonOpenApiArguments {
    public string? Handle { get; set; }
    public JsonElement[]? Arguments { get; set; }
}
