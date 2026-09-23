# Changelog

All notable changes to this repository are recorded here. The version applies to every package it publishes.

## 0.9.0

The application / agent separation: an application can now expose only its `[Ason*]` operators plus a bridge,
and an agent (or any other program) drives it over gRPC, MCP or HTTP without owning a single operator.

### New packages

| Package | What it is |
|---|---|
| `Ason.Bridge` | The transport-neutral core: the manifest, the two execution interfaces, capability gating, error codes, the runner-client executor and the agent-side library adapter. Targets `net6.0` and `net9.0`; references no transport library. |
| `Ason.Bridge.Grpc` | The gRPC adapter: service, typed client, runner-protocol transport, forwarding endpoint, plus `protos/ason_bridge.proto` (contract version `1.1`). |
| `Ason.Bridge.Mcp` | The MCP adapter: tool surface, typed client, runner-protocol transport, forwarding endpoint, Streamable HTTP and stdio hosting. |
| `Ason.Bridge.OpenApi` | The HTTP + OpenAPI adapter: the same contract for generic HTTP clients and Swagger UI, plus `POST /script/stream` (server-sent events). |

`Ason.Bridge.McpHost` is a sample relay executable, not a package. `Ason`, `Ason.Abstractions`,
`Ason.Runner.Core`, `Ason.RemoteBridge` and `Ason.ExternalExecutor` are published at the same version.

### The bridge

- **One contract, several transports.** A manifest (`protocolVersion`, `appName`, `execution`, `capabilities`,
  the operator API, Markdown, the generated proxy layer, the live instances and `instancesRevision`) is
  published identically by every adapter.
- **Two independent execution interfaces**, each switchable on its own: a whole script (`ExecuteScript`) and a
  single operator method (`InvokeFunction`).
- **Execution location is a deployment decision**: in-process, an `Ason.ExternalExecutor` child process, a
  container, or a remote runner host — the application decides, the manifest reports it.
- **MCP pass-through** (`invokeMcpTool`, off by default): the application's own MCP clients can be reached
  through the bridge, over gRPC (`InvokeMcpTool`) or MCP (`ason_invoke_mcp_tool`).
- **Log streaming**: gRPC `StreamExecution`, MCP `ason_stream_script`, and HTTP `POST /script/stream` (SSE).
- **Instance freshness**: `instancesRevision` detects a stale manifest snapshot, `includeInstanceDeclarations`
  lets a caller send only the body, and the runner transports accept `Proxies` to have the application rebuild
  its instance declarations per call.

### Additive API

Nothing was removed or renamed; a 1.0 bridge client keeps working against a 1.1 bridge, and every existing
entry point behaves as before.

- `Ason`: `IRunnerTransport` is public, `RunnerClient.OperatorInstances` and `RunnerClient.McpServerNames` are
  public, `KeywordScriptValidator` is public, `RootOperator.AttachChildOperator` reports `IsAttached`,
  `RunnerClient.UseTransport` / `AsonClientOptions.TransportFactory` accept a host-supplied transport.
- `Ason.Bridge`: `AsonBridgeRuntime`, `AsonBridgeAgent.ToOperatorsLibrary`, `IAsonExecutor.McpServers`,
  `IAsonBridgeEndpoint.ExecuteScriptWithLogsAsync`, `AsonBridgeStreamedResult`.
- `Ason.Bridge.Grpc`: `AddAsonGrpcBridge(runtime, authorizationPolicy, enableReflection)`,
  `MapAsonGrpcBridge(enableReflection)`, `GrpcAsonBridgeClient.Connect(address, headers)`,
  `StreamScriptCollectedAsync`.
- `Ason.Bridge.Mcp`: `AddAsonMcpBridge(endpoint, requireAuthorization)`,
  `McpAsonBridgeClient.ConnectAsync(endpoint, headers)`.
- `Ason.Bridge.OpenApi`: `AsonOpenApiScriptRequest.IncludeInstanceDeclarations`.
- `Ason.Bridge.McpHost`: `--key`, `--header Name=Value` (or `ASON_BRIDGE_KEY`).
- Samples: the console application side is `samples/ConsoleBridgeAppSample` (was `ConsoleGrpcBridgeHost`) and
  the caller is `samples/ConsoleBridgeCallerSample` (was `ConsoleGrpcBridgeDemo`); `samples/mcp` holds
  copy-pasteable MCP client configurations, and `samples/python` holds gRPC and MCP callers plus an
  OpenAI-driven MCP tool-calling test.

### Compatibility

- **No breaking changes.** Every addition is opt-in and defaulted off or to the previous behaviour.
- The bridge contract is now `1.1`; the additions are additive, so clients written against `1.0` need no change.
