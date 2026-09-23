# Application / agent separation with the ASON bridge

[English] | [中文](app-agent-separation.zh-CN.md) | [Español](app-agent-separation.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

ASON originally assumed one process did everything: the application, its operators, the model calls and the
script orchestration. The **bridge** splits that in two while keeping the runtime identical on both sides.

- The **application** is the only place that holds `[Ason*]` marked operators, business data and UI state.
  It contains no model client at all.
- The **agent** holds the model and the orchestration. It owns no operators: it asks the application what it
  can call and asks it to run things.
- The **bridge** is the seam between them. It describes the application's operator API, forwards execution,
  and is the only component that knows a transport (gRPC, MCP, ...) exists.

```
        ┌──────────────────────── application process ────────────────────────┐
        │  [AsonOperator]/[AsonMethod] operators + UI/data                    │
        │  Ason.Bridge runtime ──► Ason.Bridge.Grpc / Ason.Bridge.Mcp          │
        └───────────────────────────────┬─────────────────────────────────────┘
                                        │  manifest · execute script · invoke function
        ┌───────────────────────────────┴─────────────────────────────────────┐
        │  agent process: model, prompts, orchestration                        │
        │  (optional) Ason.Bridge.Grpc client + GrpcAsonBridgeTransport        │
        └──────────────────────────────────────────────────────────────────────┘
```

## The manifest is the contract

`AsonBridgeRuntime.GetManifestAsync()` returns one payload that every adapter publishes unchanged:

| Field | Meaning |
|---|---|
| `protocolVersion` | Contract revision; fail fast on a mismatch |
| `appName` | Which application answered |
| `execution` | Where scripts run: `in-process`, `external-process`, `docker`, `remote-runner` |
| `capabilities` | Which interfaces are enabled (below) |
| `api` | The operator API: operators, static modules, methods, parameters, `[AsonModel]` types |
| `markdown` | The same listing as a document, for a human or a model |
| `proxies` / `signatures` | The generated proxy layer and the signature listing a script needs |
| `instances` | The live operator instances, with the handle that addresses each one |

The API listing comes from `OperatorApiCatalog`, which walks the same reflection rules the script prompt is
built from — so a listing cannot drift from what scripts are actually able to call.

## Two independent execution interfaces

| Capability | Interface | Use it when |
|---|---|---|
| `listApis` | manifest | The agent needs to know what it may call |
| `executeScript` | `ExecuteScriptAsync(script)` | The agent composes several calls, branches or loops |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | One call, precisely — no script text in the loop |
| `invokeMcpTool` | pass-through to the application's own MCP clients | The application is a *client* of other MCP servers |
| `logStream` | `StreamExecutionAsync(script)` | The caller wants to watch the application work |

They are independent switches, and they compose: enabling both execution interfaces changes nothing about
either. `AsonBridgeCapabilities` decides what exists, and every adapter publishes exactly that:

- gRPC: a disabled capability answers `StatusCode.Unimplemented`.
- MCP: a disabled capability's tool is not registered at all.

A static operator module needs no handle (`BridgeStaticOperator.Add(2, 3)`); an instance operator is resolved
through the live instance directory when exactly one instance of its type exists, and otherwise requires the
`handle` the manifest reported. Failures come back as stable error codes — `operator-not-found`,
`handle-required`, `handle-ambiguous`, `handle-not-found`, `method-not-found`, `script-rejected`,
`not-supported`, `execution-failed` — so a client branches on a code instead of parsing a message.

## Transports

| Adapter | Package | Contains |
|---|---|---|
| gRPC | `Ason.Bridge.Grpc` | `GrpcAsonBridgeService`, `GrpcAsonBridgeClient`, `GrpcAsonBridgeTransport`, `GrpcAsonBridgeEndpoint` |
| MCP (Streamable HTTP) | `Ason.Bridge.Mcp` | `AsonBridgeMcpTools`, `McpAsonBridgeClient`, `McpAsonBridgeTransport` |
| MCP (stdio relay) | `Ason.Bridge.McpHost` | A process that republishes an application's gRPC bridge as MCP over stdin/stdout |
| anything else | your own project | Implement the same mapping against `IAsonBridgeEndpoint` |

The mature transports stay out of `Ason` on purpose: `Ason` has no gRPC, no MCP server and no ASP.NET
reference. Adding a transport means adding a project that references `Ason.Bridge` and maps
`IAsonBridgeEndpoint` onto it — one gRPC service, one MCP tool set, or an OpenAPI document generated from the
manifest. Nothing in the application has to change.

`Ason.Bridge`, `Ason.Bridge.Grpc` and `Ason.Bridge.Mcp` all target `net9.0` (the core also targets `net6.0`,
matching `Ason`).

### gRPC

```csharp
// application
var runtime = new AsonBridgeRuntime(new AsonBridgeOptions {
    AppName = "My application",
    Assemblies = new[] { typeof(MyOperators).Assembly },
    Execution = AsonBridgeExecution.InProcess,
    SingletonOperators = AsonBridgeOperators.MaterializeMarkerOnly(typeof(MyOperators).Assembly),
    ForbiddenScriptKeywords = new[] { "System.IO", "System.Reflection", "Process.Start" }
});

builder.WebHost.ConfigureKestrel(k => k.ListenLocalhost(5222, o => o.Protocols = HttpProtocols.Http2));
builder.Services.AddAsonGrpcBridge(runtime);
...
app.MapAsonGrpcBridge();
```

The gRPC endpoint must be HTTP/2 without TLS on a dedicated listener (`h2c`) unless it is served over TLS.

### MCP

```csharp
builder.Services.AddAsonMcpBridge(runtime);   // Streamable HTTP
...
app.MapAsonMcpBridge("/mcp");
```

For agents that can only launch a process (Claude Desktop, Claude Code, ...), point them at the relay host,
which connects to the application over gRPC and serves the same tools over stdio:

```bash
Ason.Bridge.McpHost --url http://localhost:5222
```

## Delegating orchestration to the agent

An agent that keeps ASON's own orchestration can use the application's operators without owning any. The
agent builds its operator library from the manifest and points its runner at the application:

```csharp
await using var client = GrpcAsonBridgeClient.Connect("http://localhost:5222");
var manifest = await client.GetManifestAsync();

// The manifest carries the proxy layer the scripts are written against.
var library = new OperatorsLibrary(
    Task.FromResult((manifest.Proxies, manifest.Signatures, (IOperatorMethodCache)new NoOperatorCache())),
    false, Array.Empty<IMcpClient>(), Array.Empty<Assembly>());

var agent = new AsonClient(chatService, new RootOperator(new object()), library);
agent.Runner.UseTransport(() => new GrpcAsonBridgeTransport(client));
```

`RunnerClient.UseTransport` is the seam that makes this possible: the transport carries the runner protocol
(`exec` down, results up) to the application, while proxy generation, retries, validation and result handling
stay exactly where they were. `McpAsonBridgeTransport` does the same over MCP.

The application resolves operator calls in its own process, so the transport never sees an `invoke` message.
If one arrives anyway — a deployment pointed at an executor that is not the application — it is answered with
an error instead of leaving the caller waiting.

## Execution location

`AsonBridgeOptions.Execution` is orthogonal to the transport:

| Value | Scripts are evaluated | Operator calls are resolved |
|---|---|---|
| `InProcess` | In the application process | The application |
| `ExternalProcess` | In an `Ason.ExternalExecutor` child process of the application | The application (over stdio) |
| `Docker` | In a container started by the application | The application |
| `RemoteRunner` | On a remote `Ason.RemoteBridge` host | The application |

In every case the operators, the data and the credentials stay in the application. See
[execution modes](execution-modes.md) and [deployment topology](architecture.md#deployment-topology).

Operator calls are marshalled through the `SynchronizationContext` captured when the runtime was built, so in
a WPF application they run on the dispatcher thread — build the runtime on that thread. Set
`CaptureSynchronizationContext = false` only in a host that has no UI affinity.

## Security

A bridge executes code and calls application functions on behalf of a caller, so treat the endpoint as
privileged:

- Bind to loopback by default; a bridge exposed on a network needs authentication in front of it
  (`requireAuthorization`-style policies on the Kestrel endpoint, or an MCP/HTTP gateway).
- Set `ForbiddenScriptKeywords` to the list you would give a local `AsonClient`; leaving it `null` only
  rejects empty scripts. It is a keyword filter, not a sandbox — prefer `ExternalProcess` or `Docker` for
  untrusted input.
- Enable only the capabilities the deployment needs; the manifest tells every client the truth about which
  are on.
- `invokeMcpTool` is off by default: it forwards to the MCP servers the *application* consumes.
- Every request is validated, gated and answered with a code, but nothing here replaces network-level access
  control.

## Samples

| Sample | Role |
|---|---|
| `samples/ConsoleGrpcBridgeHost` | The application side: `[Ason*]` operators from `LibDemo`, gRPC + MCP hosted, no agent |
| `samples/ConsoleGrpcBridgeDemo` | The external request side: manifest, live instances, single-function calls, scripts, streamed logs |
| `src/Ason.Bridge.McpHost` | The stdio relay for MCP-only agents |

```bash
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222

dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoStaticOperator.Add --args "[2,3]"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "..." --stream
```

## Known limits

- `manifest.proxies` is a snapshot taken when the caller read it. A script that uses an instance variable may
  be stale if views opened or closed since; the single-function interface resolves the live handle on every
  call and is the robust path for instance operators.
- Invoking a method on an operator whose view is not loaded triggers the runtime's normal reload, which can
  open or navigate the view. Use attached instances when a side effect is not wanted.
- The `Ason.Bridge.Grpc` namespace shadows the `Grpc` root namespace inside files that import it: write
  `using Grpc.Net.Client;` and then `GrpcChannel.ForAddress(...)`, or fully qualify the type.
- The gRPC contract has no MCP pass-through (`invokeMcpTool`); a relay reports it as `not-supported`.
