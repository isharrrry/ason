using System.Text.Json;
using System.Text.Json.Nodes;
using Ason.Bridge;

namespace Ason.Bridge.OpenApi;

/// <summary>
/// Options for the HTTP adapter. An endpoint is reachable by anything that can send an HTTP request, so the
/// adapter can require a shared key on top of the loopback binding.
/// </summary>
public sealed class AsonOpenApiBridgeOptions {

    /// <summary>Base path every endpoint is mapped under.</summary>
    public string BasePath { get; set; } = "/ason";

    /// <summary>
    /// When set, every request must carry this value in <see cref="ApiKeyHeader"/>; otherwise the endpoint
    /// answers 401. A shared key is a blunt instrument and no replacement for putting the whole service behind
    /// real authentication, but it keeps a local HTTP surface from being open to any process on the machine.
    /// </summary>
    public string? ApiKey { get; set; }

    public string ApiKeyHeader { get; set; } = "X-Ason-Bridge-Key";

    /// <summary>The HTTP verb used for function calls; POST carries arguments in a body.</summary>
    public bool EnableFunctionPathEndpoint { get; set; } = true;
}

/// <summary>
/// The OpenAPI document, generated from the manifest: a generic HTTP client - Swagger UI, Postman, curl - can
/// discover the application's operator API without knowing anything about ASON. It is produced from the same
/// manifest the gRPC and MCP adapters publish, so the three never disagree.
/// </summary>
public static class AsonOpenApiDocument {

    public static JsonObject Build(AsonBridgeManifest manifest, AsonOpenApiBridgeOptions options) {
        var paths = new JsonObject();

        if (manifest.Capabilities.ListApis) {
            paths["/manifest"] = new JsonObject {
                ["get"] = Operation("Returns the application's ASON manifest: operator API, live instances, capabilities and where scripts run.", Response("The manifest."))
            };
            paths["/instances"] = new JsonObject {
                ["get"] = Operation("Lists the operator instances that are alive right now, with the handle that addresses each one.", Response("The live instances."))
            };
        }

        if (manifest.Capabilities.ExecuteScript) {
            paths["/script"] = new JsonObject {
                ["post"] = Operation(
                    "Runs a complete ASON script body against the application and returns its result.",
                    Response("The outcome of the call.", "AsonBridgeCallResult"),
                    RequestBody("AsonBridgeScriptRequest"))
            };
        }

        if (manifest.Capabilities.InvokeFunction) {
            paths["/functions/invoke"] = new JsonObject {
                ["post"] = Operation(
                    "Calls exactly one operator method with JSON arguments.",
                    Response("The outcome of the call.", "AsonBridgeCallResult"),
                    RequestBody("AsonBridgeInvokeRequest"))
            };
            if (options.EnableFunctionPathEndpoint) {
                paths["/functions/{operator}/{method}"] = new JsonObject {
                    ["post"] = Operation(
                        "Calls one operator method. 'arguments' is a JSON array such as [2, 3]; 'handle' is only needed when several live instances of the operator exist.",
                        Response("The outcome of the call.", "AsonBridgeCallResult"),
                        RequestBody("AsonBridgeArguments"),
                        ("operator", "Name of the operator as listed in the manifest."),
                        ("method", "Method name as listed in the manifest."),
                        ("handle", "Optional handle of the live instance to use."))
                };
            }
        }

        var document = new JsonObject {
            ["openapi"] = "3.0.3",
            ["info"] = new JsonObject {
                ["title"] = manifest.AppName,
                ["version"] = manifest.ProtocolVersion,
                ["description"] = $"ASON bridge for '{manifest.AppName}'. Scripts are evaluated {manifest.Execution}; operator calls are resolved inside the application."
            },
            ["servers"] = new JsonArray { new JsonObject { ["url"] = options.BasePath } },
            ["paths"] = paths,
            ["components"] = new JsonObject {
                ["schemas"] = new JsonObject {
                    ["AsonBridgeScriptRequest"] = new JsonObject {
                        ["type"] = "object",
                        ["required"] = new JsonArray { "code" },
                        ["properties"] = new JsonObject {
                            ["code"] = new JsonObject { ["type"] = "string", ["description"] = "The script body." },
                            ["includeProxyPreamble"] = new JsonObject { ["type"] = "boolean", ["default"] = true, ["description"] = "Prepend the generated proxy layer." }
                        }
                    },
                    ["AsonBridgeInvokeRequest"] = new JsonObject {
                        ["type"] = "object",
                        ["required"] = new JsonArray { "operator", "method" },
                        ["properties"] = new JsonObject {
                            ["operator"] = new JsonObject { ["type"] = "string" },
                            ["method"] = new JsonObject { ["type"] = "string" },
                            ["handle"] = new JsonObject { ["type"] = "string", ["nullable"] = true },
                            ["arguments"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject(), ["description"] = "Arguments in parameter order." }
                        }
                    },
                    ["AsonBridgeArguments"] = new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject {
                            ["arguments"] = new JsonObject { ["type"] = "array", ["items"] = new JsonObject(), ["description"] = "Arguments in parameter order." },
                            ["handle"] = new JsonObject { ["type"] = "string", ["nullable"] = true }
                        }
                    },
                    ["AsonBridgeCallResult"] = new JsonObject {
                        ["type"] = "object",
                        ["properties"] = new JsonObject {
                            ["success"] = new JsonObject { ["type"] = "boolean" },
                            ["result"] = new JsonObject { ["nullable"] = true, ["description"] = "The result as JSON." },
                            ["error"] = new JsonObject { ["type"] = "string", ["nullable"] = true },
                            ["errorCode"] = new JsonObject { ["type"] = "string", ["nullable"] = true, ["description"] = "One of the ASON bridge error codes." }
                        }
                    }
                }
            },
            // The callable surface, as data: a client that prefers a flat list of functions over a manifest can
            // read it straight from the document.
            ["x-ason-execution"] = manifest.Execution,
            ["x-ason-capabilities"] = new JsonObject {
                ["listApis"] = manifest.Capabilities.ListApis,
                ["executeScript"] = manifest.Capabilities.ExecuteScript,
                ["invokeFunction"] = manifest.Capabilities.InvokeFunction,
                ["invokeMcpTool"] = manifest.Capabilities.InvokeMcpTool,
                ["logStream"] = manifest.Capabilities.LogStream
            },
            ["x-ason-operators"] = new JsonArray(manifest.Api.Operators.Select(op => (JsonNode)new JsonObject {
                ["typeName"] = op.TypeName,
                ["description"] = op.Description,
                ["isStatic"] = op.IsStatic,
                ["methods"] = new JsonArray(op.Methods.Select(m => (JsonNode)new JsonObject {
                    ["name"] = m.Name,
                    ["description"] = m.Description,
                    ["returnType"] = m.ReturnType,
                    ["parameters"] = new JsonArray(m.Parameters.Select(p => (JsonNode)new JsonObject {
                        ["name"] = p.Name,
                        ["type"] = p.Type
                    }).ToArray())
                }).ToArray())
            }).ToArray()),
            ["x-ason-models"] = new JsonArray(manifest.Api.Models.Select(m => (JsonNode)new JsonObject {
                ["name"] = m.Name,
                ["description"] = m.Description,
                ["fields"] = new JsonArray(m.Fields.Select(f => (JsonNode)new JsonObject {
                    ["name"] = f.Name,
                    ["type"] = f.Type
                }).ToArray())
            }).ToArray())
        };

        return document;
    }

    public static string BuildJson(AsonBridgeManifest manifest, AsonOpenApiBridgeOptions options) =>
        Build(manifest, options).ToJsonString(new JsonSerializerOptions { WriteIndented = true });

    static JsonObject Operation(string summary, JsonObject response, JsonObject? requestBody = null, params (string Name, string Description)[] parameters) {
        var operation = new JsonObject {
            ["summary"] = summary,
            ["operationId"] = summary.Split('.')[0].Replace(" ", string.Empty),
            ["responses"] = response
        };
        if (parameters.Length > 0) {
            operation["parameters"] = new JsonArray(parameters.Select(p => (JsonNode)new JsonObject {
                ["name"] = p.Name,
                ["in"] = "path",
                ["required"] = p.Name != "handle",
                ["schema"] = new JsonObject { ["type"] = "string" },
                ["description"] = p.Description
            }).ToArray());
        }
        if (requestBody is not null) operation["requestBody"] = requestBody;
        return operation;
    }

    static JsonObject Response(string description, string? schema = null) => new() {
        ["200"] = new JsonObject {
            ["description"] = description,
            ["content"] = new JsonObject {
                ["application/json"] = new JsonObject {
                    ["schema"] = schema is null ? new JsonObject() : new JsonObject { ["$ref"] = $"#/components/schemas/{schema}" }
                }
            }
        },
        ["400"] = new JsonObject { ["description"] = "The call was refused or the operator failed; the body carries an ASON error code." },
        ["401"] = new JsonObject { ["description"] = "The bridge key is missing or wrong." }
    };

    static JsonObject RequestBody(string schema) => new() {
        ["required"] = true,
        ["content"] = new JsonObject {
            ["application/json"] = new JsonObject {
                ["schema"] = new JsonObject { ["$ref"] = $"#/components/schemas/{schema}" }
            }
        }
    };
}
