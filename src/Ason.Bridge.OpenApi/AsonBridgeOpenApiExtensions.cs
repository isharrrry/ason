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

            var result = await endpoint.ExecuteScriptAsync(request.Code, request.IncludeProxyPreamble ?? true, context.RequestAborted).ConfigureAwait(false);
            return Result(result);
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
