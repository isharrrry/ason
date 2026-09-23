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
  and is the only component that knows a transport (gRPC, MCP, ...) exists. The four deployment shapes — a .NET
  agent, MCP over HTTP, the stdio relay and a generic HTTP client — are drawn as flow diagrams in
  [architecture](architecture.md), under *Application / agent split*.

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
| MCP (Streamable HTTP) | `Ason.Bridge.Mcp` | `AsonBridgeMcpTools`, `McpAsonBridgeClient`, `McpAsonBridgeTransport`, `McpAsonBridgeEndpoint` |
| MCP (stdio relay) | `Ason.Bridge.McpHost` | A process that republishes an application's gRPC bridge as MCP over stdin/stdout |
| HTTP + OpenAPI (Swagger) | `Ason.Bridge.OpenApi` | HTTP endpoints plus a document generated from the manifest, for generic HTTP clients and Swagger UI |
| anything else | your own project | Implement the same mapping against `IAsonBridgeEndpoint` |

The mature transports stay out of `Ason` on purpose: `Ason` has no gRPC, no MCP server and no ASP.NET
reference. Adding a transport means adding a project that references `Ason.Bridge` and maps
`IAsonBridgeEndpoint` onto it - one gRPC service, one MCP tool set, or the HTTP/OpenAPI adapter.
`Ason.Bridge.OpenApi` is that exercise carried out: it was added without touching the runtime, the application
or the other adapters. Nothing in the application has to change.

`Ason.Bridge`, `Ason.Bridge.Grpc`, `Ason.Bridge.Mcp` and `Ason.Bridge.OpenApi` all target `net9.0` (the core
also targets `net6.0`, matching `Ason`). The two WPF samples target `net9.0-windows`.

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
which connects to the application and serves the same tools over stdio:

```bash
# the application publishes gRPC
Ason.Bridge.McpHost --url http://localhost:5222

# ...or the relay can speak MCP to the application instead, so gRPC is not involved at all
Ason.Bridge.McpHost --url http://localhost:5223/mcp --transport mcp
```

The relay exists because of what stdio MCP *is*: the contract is that the client spawns the server and talks
over that child's stdin/stdout. A running desktop application cannot be that child, so something has to own the
pipe - and that something needs a channel to the application, which is why two hops appear in this one
deployment shape. It is not a prerequisite of the design: an agent that speaks HTTP MCP connects to the
application directly, and an application whose lifetime *is* the agent's session can serve stdio MCP itself with
`AddAsonMcpStdioBridge` (that is exactly the call the relay uses).

### HTTP + OpenAPI (Swagger)

```csharp
builder.Services.AddAsonOpenApiBridge(runtime, options => options.ApiKey = Environment.GetEnvironmentVariable("ASON_BRIDGE_KEY"));
...
app.MapAsonOpenApiBridge("/ason");
```

| Route | Purpose |
|---|---|
| `GET /ason/manifest` | The full manifest |
| `GET /ason/instances` | Live instances and their handles |
| `POST /ason/script` | Whole-script interface (`{ "code": "..." }`) |
| `POST /ason/functions/invoke` | Single-function interface (`{ "operator", "method", "handle?", "arguments": [] }`) |
| `POST /ason/functions/{operator}/{method}` | The same call as a path, so it appears in Swagger UI |
| `GET /ason/openapi.json` | The document, generated from the manifest |

The document is produced from the manifest, so it cannot drift: it lists the mapped routes, the request and
result schemas, and the callable surface as `x-ason-operators` / `x-ason-models` extensions. A generic HTTP
client, Swagger UI or Postman therefore sees the application's operator API without knowing anything about
ASON. A successful call is `200`; an application-level failure is `400` with the ASON error code in the body;
a capability that is switched off is not served at all (`404`); and with `ApiKey` set, every route requires
that key in `ApiKeyHeader` (`401` otherwise) - which matters because an HTTP endpoint is reachable by anything
on the machine, not just by ASON clients.

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

var agent = new AsonClient(chatService, new RootOperator(new object()), library, new AsonClientOptions {
    // The application decides where the script is isolated; this side only says how to reach it.
    ExecutionMode = ExecutionMode.ExternalProcess,
    TransportFactory = () => new GrpcAsonBridgeTransport(client)
});
```

`AsonClientOptions.TransportFactory` (and `RunnerClient.UseTransport` underneath it) is the seam that makes
this possible: the transport carries the runner protocol (`exec` down, results up) to the application, while
proxy generation, retries, validation and result handling stay exactly where they were.
`McpAsonBridgeTransport` does the same over MCP.

The application resolves operator calls in its own process, so the transport never sees an `invoke` message.
If one arrives anyway — a deployment pointed at an executor that is not the application — it is answered with
an error instead of leaving the caller waiting.

## Using the bridge without an agent

Nothing about the bridge is agent-specific. The same endpoints are an RPC surface for ordinary programs: a test
harness that drives the real application, a CI step that seeds or checks state, an automation or maintenance
script, another service, or a developer with `curl`. They call the application the way any client would —
usually the single-function interface, because a program that already knows what to call does not need a model
to write the call:

| Caller | Channel | Typical call |
|---|---|---|
| .NET program | `GrpcAsonBridgeClient` | `InvokeFunctionAsync(call)` — one round trip, JSON in and out |
| .NET program | `McpAsonBridgeClient` | the same call over MCP, when the caller already speaks MCP |
| any HTTP client | `Ason.Bridge.OpenApi` | `POST /ason/functions/{operator}/{method}`, or `POST /ason/script` |
| test harness / CI job | any of them | a sequence of calls whose returned JSON is asserted |

```bash
# no model anywhere in this workflow
curl -s http://localhost:5223/ason/manifest                       # discover what can be called
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/Rename \
     -H "Content-Type: application/json" -d '{"arguments":[1,"Ada"]}'
curl -s -X POST http://localhost:5223/ason/script \
     -H "Content-Type: application/json" -d '{"code":"return employeesOperator.GetEmployees().Count;"}'

# the same calls from a .NET client (the sample console client is exactly this case)
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
```

What such a caller gets: the manifest (discovery), the single-function interface, the whole-script interface if
it wants to compose several calls, and streamed logs over gRPC. What it does not get is the model and the
orchestration — the call sequence is its own, there is nobody to explain a result in words, and there are no
retries beyond its own. For automation that is the point: the deterministic call is the feature.

Two consequences worth knowing:

- **Function calls bypass the script channel entirely.** `ForbiddenScriptKeywords` guards generated code; an
  `invokeFunction` call is not code. What protects the application there is the exposed API (the marked
  operators, and `AdditionalMethodFilter` when the host narrows it), the capabilities that are enabled, and the
  network controls below. Function-level invocation also never involves a script host: no executor process, no
  compilation, one round trip — unlike a script, which may cost one round trip per operator call it makes.
- **A bridge endpoint is an administration surface.** Anything that can reach it can call every operator the
  manifest lists, so bind it to loopback and put a key or real authentication in front when it is exposed
  further. The HTTP adapter has an optional shared key; gRPC and MCP are expected to sit behind a gateway or
  ASP.NET authorization.

The agent scenario and this one share everything: the same host, runtime, adapters and endpoints. What differs
is only who composes the calls — a model, or the program itself.

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
| `samples/WpfAppOnlyDemo` | **The application side as a real desktop app**: WPF window with `[Ason*]` operators (a view operator bound to the window, a static module, a marker-only module and the LibDemo class library) that hosts gRPC, MCP and HTTP/OpenAPI. No model, no chat. `--bridge-only --port 5222` runs it headless. |
| `samples/WpfAgentDemo` | **The agent side as a real desktop app**: chat window, endpoint and transport selection (gRPC/MCP), the API listing fetched from the application, and a call log. It declares no `[AsonOperator]` at all. `--verify <endpoint> [--mcp]` runs a headless self-check. |
| `samples/ConsoleGrpcBridgeHost` | The application side in its smallest form: `[Ason*]` operators from `LibDemo`, gRPC + MCP + OpenAPI, no agent |
| `samples/ConsoleGrpcBridgeDemo` | The external request side: manifest, live instances, single-function calls, scripts, streamed logs |
| `src/Ason.Bridge.McpHost` | The stdio relay for MCP-only agents |

```bash
# application side (desktop, or headless for scripting)
dotnet run --project samples/WpfAppOnlyDemo -- --bridge-only --port 5222
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222

# agent side
dotnet run --project samples/WpfAgentDemo
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222          # gRPC self-check
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # MCP self-check

# external request side, no agent involved
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoStaticOperator.Add --args "[2,3]"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "..." --stream

# the same application over HTTP/OpenAPI
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add -H "Content-Type: application/json" -d '{"arguments":[40,2]}'
```

The WPF pair is also covered by end-to-end tests: the application sample is started headless and driven over
gRPC and MCP (including an assertion that operator calls land on the dispatcher thread), and the agent sample
is started in `--verify` mode to prove it owns zero operators while still calling one in the application.

## Known limits

- `manifest.proxies` is a snapshot taken when the caller read it. A script that uses an instance variable may
  be stale if views opened or closed since; the single-function interface resolves the live handle on every
  call and is the robust path for instance operators.
- Invoking a method on an operator whose view is not loaded triggers the runtime's normal reload, which can
  open or navigate the view. Use attached instances when a side effect is not wanted.
- The `Ason.Bridge.Grpc` namespace shadows the `Grpc` root namespace inside files that import it: write
  `using Grpc.Net.Client;` and then `GrpcChannel.ForAddress(...)`, or fully qualify the type.
- The gRPC contract has no MCP pass-through (`invokeMcpTool`); a relay reports it as `not-supported`.
