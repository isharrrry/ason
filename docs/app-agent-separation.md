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
| `protocolVersion` | Contract revision; fail fast on a mismatch. `1.1` is additive: a `1.0` client still works |
| `appName` | Which application answered |
| `execution` | Where scripts run: `in-process`, `external-process`, `docker`, `remote-runner` |
| `capabilities` | Which interfaces are enabled (below) |
| `api` | The operator API: operators, static modules, methods, parameters, `[AsonModel]` types |
| `markdown` | The same listing as a document, for a human or a model |
| `proxies` / `signatures` | The generated proxy layer and the signature listing a script needs |
| `instances` | The live operator instances, with the handle that addresses each one |
| `instancesRevision` | Digest of `instances`; compare it with a manifest you kept to see whether that snapshot is stale |

The API listing comes from `OperatorApiCatalog`, which walks the same reflection rules the script prompt is
built from — so a listing cannot drift from what scripts are actually able to call.

## Two independent execution interfaces

| Capability | Interface | Use it when |
|---|---|---|
| `listApis` | manifest | The agent needs to know what it may call |
| `executeScript` | `ExecuteScriptAsync(script)` | The agent composes several calls, branches or loops |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | One call, precisely — no script text in the loop |
| `invokeMcpTool` | pass-through to the application's own MCP clients: gRPC `InvokeMcpTool`, MCP tool `ason_invoke_mcp_tool` | The application is a *client* of other MCP servers |
| `logStream` | gRPC `StreamExecution`, MCP `ason_stream_script`, HTTP `POST /ason/script/stream` | The caller wants to watch the application work |

They are independent switches, and they compose: enabling both execution interfaces changes nothing about
either. `AsonBridgeCapabilities` decides what exists, and every adapter publishes exactly that:

- gRPC: a disabled capability answers `StatusCode.Unimplemented`.
- MCP: a disabled capability's tool is not registered at all.

`invokeMcpTool` has a second, separate refusal, and the two mean different things:

- **Capability off** — the call does not exist (`Unimplemented`, or no such tool), which is the same signal
  every other disabled capability produces.
- **Capability on, no MCP server registered** — the call exists and answers `not-supported`, because the
  application enabled pass-through but never registered a client with `RunnerClient.RegisterMcpClient`.
  `IAsonExecutor.McpServers` is what the bridge consults, so a host that supplies its own executor reports the
  servers it really has.

A static operator module needs no handle (`BridgeStaticOperator.Add(2, 3)`); an instance operator is resolved
through the live instance directory when exactly one instance of its type exists, and otherwise requires the
`handle` the manifest reported. Failures come back as stable error codes — `operator-not-found`,
`handle-required`, `handle-ambiguous`, `handle-not-found`, `method-not-found`, `script-rejected`,
`not-supported`, `execution-failed` — so a client branches on a code instead of parsing a message.

## Live instances and manifest freshness

`manifest.proxies` ends with a declaration for every live instance
(`EmployeesOperator employeesOperator = new("EmployeesOperator");`), which is what lets a script say
`employeesOperator.GetEmployees()`. Those declarations are only correct for the moment the manifest was read:
a view that opened or closed afterwards is missing from them (or declared for a handle that is gone).

The bridge gives a caller three ways to stay correct, in increasing order of robustness:

1. **Compare `instancesRevision`.** It is a digest of the live instance list, so a caller that kept an older
   manifest can detect that it went stale and re-read it (`ason_get_manifest`).
2. **Send the body only.** `includeInstanceDeclarations: true` (gRPC `include_instance_declarations`, MCP
   `includeInstanceDeclarations`, HTTP `includeInstanceDeclarations`) tells the application to supply the proxy
   layer *and* today's declarations, so the caller sends nothing but the statements it wants to run. The
   declarations have to live inside the generated layer, so the application is the only side that can rebuild
   them; this flag is the explicit form of "I am sending a body, you own the layer".
3. **Name your snapshot layer.** `GrpcAsonBridgeTransport` and `McpAsonBridgeTransport` accept
   `Proxies = manifest.Proxies`. When it is set, the transport strips that exact layer back off the composed
   script and asks the application for its current one, per call and with no extra round trip. This is the
   switch the agent-side runner protocol uses, since it always prepends the layer it read at start-up.

The single-function interface needs none of this: it resolves the live handle on every call.

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

`Protos/ason_bridge.proto` holds the contract: `GetManifest`, `ListInstances`, `ExecuteScript`,
`InvokeFunction`, `InvokeMcpTool` and the streaming `StreamExecution`. `ExecuteScriptRequest` carries
`include_proxy_preamble` and `include_instance_declarations`, `ManifestReply` carries `instances_revision`,
and `InvokeMcpToolRequest` takes the tool arguments as a JSON object. Everything added in protocol `1.1` is
additive: a `1.0` client keeps working against a `1.1` bridge.

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

`samples/mcp/` holds the two client configurations to copy — one for a client that starts the relay itself,
one for a client that speaks Streamable HTTP to the application directly — plus a README that names the tools
and the authorization flags. `samples/python/ason_mcp_caller` is a stdlib-only MCP client for checking a
configuration without involving a desktop client.

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

## Watching a script run

Execution logs are a capability of the runtime (`Capabilities.LogStream`), and each adapter publishes the shape
its protocol can actually deliver - the manifest tells a client that the runtime supports logs, the adapter's
own surface tells it how:

| Adapter | Surface | What arrives |
|---|---|---|
| gRPC | `StreamExecution` | One `log` event per line while the script runs, then one `result`/`error` event |
| HTTP | `POST {base}/script/stream` | The same, as server-sent events (`event: log` … `event: result`) |
| MCP | `ason_stream_script` tool | The logs *and* the result in the single answer, because an MCP tool call cannot be pushed to |

Switching `LogStream` off removes all three (the rpc answers `Unimplemented`, the route is not served, the MCP
tool is not registered), and leaves the non-streaming interfaces alone. A transport that is relaying - the
stdio MCP host above all - collects the application's own stream instead of subscribing to a log event it would
never receive, so a relay's logs are the application's.

```bash
# server-sent events, straight from curl
curl -N -X POST http://localhost:5223/ason/script/stream \
  -H 'Content-Type: application/json' \
  -d '{"code":"return LibDemoOperator.Add(40, 2);"}'
# event: log
# data: {"level":"Information","message":"...","source":"RunnerClient"}
#
# event: result
# data: {"success":true,"result":42}
```

An `AsonBridgeRuntime` raises these events on `AsonBridgeRuntime.Log` (`IAsonBridgeEndpoint.Log` for an
adapter), which is also how a host can mirror them into its own logging.

## Calling the bridge without .NET

The gRPC contract is the interface, so a Python, Go, Java, Rust or `curl`-with-`grpcurl` caller needs the
`.proto` file and nothing else. Two ways to get the exact copy an application is serving:

```xml
<!-- 1. From the package: the file travels inside Ason.Bridge.Grpc -->
<PackageReference Include="Ason.Bridge.Grpc" Version="0.9.0" GeneratePathProperty="true" />
<!-- the contract is then at $(PkgAson_Bridge_Grpc)\protos\ason_bridge.proto -->
```

```bash
# 2. From the repository, or from an unpacked package
unzip -o Ason.Bridge.Grpc.0.9.0.nupkg 'protos/*' -d ./ason-contract
# src/Ason.Bridge.Grpc/Protos/ason_bridge.proto
```

The service is `ason.bridge.v1.AsonBridge`, with `GetManifest`, `ListInstances`, `ExecuteScript`,
`InvokeFunction`, `InvokeMcpTool` and the server-streaming `StreamExecution` (one `log` event per line, then
exactly one `result` or `error`).

```bash
# Python: build stubs, then call. samples/python/ason_bridge_client.py does this for you.
python -m grpc_tools.protoc -I./ason-contract --python_out=. --grpc_python_out=. ason_bridge.proto

# Go / Java / anything else protoc supports
protoc -I./ason-contract --go_out=. --go-grpc_out=. ason_bridge.proto
protoc -I./ason-contract --java_out=. ason_bridge.proto

# grpcurl, with the contract on disk...
grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto \
  -d '{"code":"return 1;"}' localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript

# ...or without it, when the application published reflection (--reflection, off by default)
grpcurl -plaintext -d '{"operator":"LibDemoOperator","method":"Add","arguments_json":"[40,2]"}' \
  localhost:5222 ason.bridge.v1.AsonBridge/InvokeFunction
```

`samples/python/` is a runnable version of the first example: it compiles the contract on first use and exposes
`manifest`, `instances`, `call`, `script [--stream]` and `mcp <server> <tool>` as subcommands.

Reflection is opt-in and off by default (`AddAsonGrpcBridge(runtime, enableReflection: true)`, or the sample's
`--reflection`): publishing it broadcasts the callable surface to anyone who can reach the port, which is fine
on loopback and should be paired with the authorization of the [Security](#security) section anywhere else.
Either way, **what a caller may call is still what the manifest says** - reflection only saves you from keeping
a `.proto` file in step; capabilities, the keyword filter and the operator API all still apply.

## Delegating orchestration to the agent

An agent that keeps ASON's own orchestration can use the application's operators without owning any. The
agent builds its operator library from the manifest and points its runner at the application:

```csharp
await using var client = GrpcAsonBridgeClient.Connect("http://localhost:5222");
var manifest = await client.GetManifestAsync();

// The manifest carries the proxy layer and the signatures the application published, so building the
// client's library is one call - and no operator is duplicated on this side.
var library = manifest.ToOperatorsLibrary();

var agent = new AsonClient(chatService, new RootOperator(new object()), library, new AsonClientOptions {
    // The application decides where the script is isolated; this side only says how to reach it.
    ExecutionMode = ExecutionMode.ExternalProcess,
    // Proxies: the layer this client read from the manifest. Setting it makes the application rebuild the
    // instance declarations on every execution, so a view that opened after this manifest was read still works.
    TransportFactory = () => new GrpcAsonBridgeTransport(client) { Proxies = manifest.Proxies }
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
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
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

## What a caller has to know and configure

The bridge is deliberately dumb about discovery: it answers questions, it does not announce itself. So the
honest answer to "what does it take to connect?" depends on the shape:

| Shape | What the caller prepares | What the application configures | Zero-config? |
|---|---|---|---|
| HTTP + OpenAPI | One URL (and the key, if the application set one) — `curl`, Swagger UI, Postman or any HTTP client | `AddAsonOpenApiBridge(runtime)` + `MapAsonOpenApiBridge()`, a port, optionally `ApiKey` | ✅ a URL is enough |
| MCP over HTTP | One URL ending in `/mcp`, plus an MCP client | `AddAsonMcpBridge(runtime)` + `MapAsonMcpBridge()`, optionally `requireAuthorization` | ✅ a URL is enough |
| gRPC, .NET | The URL, and a client wrapper: `GrpcAsonBridgeClient.Connect(url)` (or the raw generated stub) | `AddAsonGrpcBridge(runtime)` + `MapAsonGrpcBridge()`, an HTTP/2 listener, optionally a policy | ⚠️ needs a client, no configuration file |
| gRPC, another language | The URL **and the contract**: `ason_bridge.proto` from the package, then a generated stub — see [Calling the bridge without .NET](#calling-the-bridge-without-net) | the same, plus optionally reflection (`enableReflection`) so no local `.proto` is needed | ⚠️ needs the contract |
| MCP over stdio | **A launch command** the agent can run (`Ason.Bridge.McpHost --url …`), because stdio MCP means "the client spawns the server" | the application must be running and reachable over gRPC or MCP | ❌ the caller writes a command |

Three premises are worth stating plainly, because each one produces a confusing failure when it is missed:

1. **There is no registry, mDNS or auto-discovery.** The URL is out-of-band knowledge: a command-line argument,
   a configuration file, an environment variable. The manifest is discovery *after* you know where to ask — it
   tells you what the application exposes, not where it is.
2. **A handle is runtime state.** Instances appear and disappear as views open and close, so a caller that wants
   to address one must ask for `instances` first (or accept `handle-required` / `handle-ambiguous` /
   `handle-not-found`). The manifest's `instancesRevision` is how it notices that its picture went stale.
3. **`proxies` is a snapshot.** A script that uses an instance variable is only correct for the moment the
   manifest was read; [Live instances and manifest freshness](#live-instances-and-manifest-freshness) has the
   three ways to stay correct.

Two switches stay on the application's side of the line, and a caller can only read them, never change them:

- **Capabilities** decide what exists at all: with `executeScript` off, gRPC answers `Unimplemented`, the MCP
  tool is not listed and the HTTP route is `404`. The manifest says so in advance, so a caller can adapt instead
  of probing.
- **Authorization** decides who may call: `AddAsonGrpcBridge(runtime, "<policy>")` and
  `AddAsonMcpBridge(endpoint, requireAuthorization: true)` turn it on, and a caller that is not authorized gets
  `Unauthenticated` / `401` — never `Unimplemented`, which would be indistinguishable from a disabled
  capability. See [Requiring a caller to authenticate](#requiring-a-caller-to-authenticate).

The boundaries this crosses are drawn in [architecture](architecture.md#application--agent-split-the-bridge)
(boundary D is the caller's), and the execution-location axis — which is orthogonal to all of the above — is in
[execution modes](execution-modes.md).

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
- `invokeMcpTool` is off by default: it forwards to the MCP servers the *application* consumes, using the
  credentials that application already holds. Turning it on exposes those tools to every caller the bridge
  accepts, so pair it with the authorization below when the bridge is not loopback-only.
- Every request is validated, gated and answered with a code, but nothing here replaces network-level access
  control.

### Requiring a caller to authenticate

Authorization is opt-in per adapter and off by default, because the development setup is a loopback bridge with
no credentials:

| Adapter | Turn it on | An unauthorized caller sees |
|---|---|---|
| gRPC | `AddAsonGrpcBridge(runtime, "<policy>")` — the policy is an ordinary ASP.NET Core authorization policy | `StatusCode.Unauthenticated` (never `Unimplemented`, which means "capability disabled" on every adapter) |
| MCP (HTTP) | `AddAsonMcpBridge(endpoint, requireAuthorization: true)` | `401` |
| HTTP / OpenAPI | `AsonOpenApiBridgeOptions.ApiKey` (and `ApiKeyHeader`) | `401` |

Clients prove who they are with headers, and that is the whole mechanism — gRPC metadata *is* an HTTP/2 header:

```csharp
await using var grpc = GrpcAsonBridgeClient.Connect("http://localhost:5222", headers);   // e.g. Authorization: Bearer …
await using var mcp = await McpAsonBridgeClient.ConnectAsync("http://localhost:5223/mcp", headers);
```

A relay can carry the credentials for an agent that cannot set headers itself:

```bash
Ason.Bridge.McpHost --url http://localhost:5222 --key <value>                     # X-Ason-Bridge-Key: <value>
Ason.Bridge.McpHost --url http://localhost:5222 --header "Authorization=Bearer <token>"
```

Authorization failures stay credentials problems rather than application results: gRPC surfaces
`Unauthenticated` (the typed client rethrows it instead of folding it into a failed bridge call), MCP and HTTP
surface `401`. Application-level failures keep their error codes.

## Samples and how to run them

Which sample (or combination of samples) shows which shape — from the single-process arrangement to each way
of splitting it. Rows marked 🔑 need a model key: `MY_OPEN_AI_KEY` (plus optionally `MY_OPEN_AI_BASE_URL`,
`MY_OPEN_AI_MODEL`); `ConsoleMcpSample` also needs `MY_CONTEXT7_API_KEY`.

**Roles**: `…AppSample` / `…Host` / `…AppOnlyDemo` are the **application side** — they own the operators and
publish the endpoints; `…CallerSample` / `…AgentSample` / `…AgentDemo` are the **caller side** — they connect to
those endpoints and own no operators. One program can sit on both sides at once: the stdio relay is a caller of
the application and, at the same time, an application (a server) to the agent that launched it.

| Shape | Application side | Caller / agent side | What you see |
|---|---|---|---|
| **Not separated** — desktop app with the agent inside | `samples/WptDemoApp` | the same process | the chat panel drives the WPF UI through in-process operators |
| Not separated — Blazor Server | `samples/BlazorAdvancedApp` (http://localhost:5240) | the same process | the chat panel drives server-side components |
| Not separated — console with the extractor agent | `samples/ConsoleExtractorSample` | the same process | text extraction plus operator calls in one console app |
| Not separated — console whose API comes from an MCP server | `samples/ConsoleMcpSample` | the same process | the script calls Context7 MCP tools as if they were operators |
| Not separated — a brand-new app | `samples/templates` | the same process | `dotnet new ason.wpf` / `ason.winforms` / `ason.console` / `ason.blaz.srv` / `ason.maui` scaffold a working chat app |
| Not separated, but the **script host** is remote | `samples/WptDemoApp` + `samples/RemoteRunnerService` (http://localhost:5236) | the same process | only execution moves; app, agent, operators and data stay together |
| **Separated** — .NET agent with its own orchestration | `samples/WpfAppOnlyDemo` or `samples/ConsoleBridgeAppSample` | `samples/WpfAgentDemo`, or any `AsonClient` using `TransportFactory` | the agent lists the application's operator API and calls it; no operator exists on the agent side |
| Separated — the same agent side **without a UI** (any OS) | either application side | `samples/ConsoleAgentSample` (`--list` needs no key; `--send "…"` needs one) | the console agent prints the API it built from the manifest and then drives the application |
| Separated, with the **script host as the application's child process** | `samples/ConsoleBridgeAppSample --execution external` | any caller above | the manifest reports `execution=external-process`; the script text runs in the child while operator calls still resolve inside the application |
| Separated — an agent that speaks MCP over HTTP | either application side | any MCP client (Claude Desktop, an IDE) pointed at `/mcp` | the application appears as five MCP tools |
| Separated — a client configured for **stdio MCP** | either application side | `samples/mcp/claude_desktop_config.json` (stdio relay) or `http_mcp_config.json` (HTTP) | a real desktop client sees the application's tools; `samples/python/ason_mcp_caller` checks such a configuration without one |
| Separated — a **model** choosing MCP tools, as a test | either application side | `samples/python/ason_mcp_agent` (🔑 `MY_OPEN_AI_KEY`) | the model picks the tool, the operator runs in the application, and `--expect` fails the run if the result never appears |
| Separated — an agent that can only start a stdio MCP server | either application side | `src/Ason.Bridge.McpHost` (`--transport grpc` or `--transport mcp`) | the same tools over the agent's stdin/stdout |
| Separated — **no agent at all** | either application side | `samples/ConsoleBridgeCallerSample`, `curl`, Swagger UI/Postman | a program or a shell drives the application: one function call, or a script |
| Separated — a caller in **another language** | either application side | `samples/python` (compiles the shipped `.proto`) | Python lists the API from the manifest, calls a function, runs a script and reads its logs |

```bash
# --- not separated: application and agent in one process (🔑 requires the model key) ---
dotnet run --project samples/WptDemoApp/WpfSampleApp.csproj -f net9.0-windows
dotnet run --project samples/BlazorAdvancedApp                       # http://localhost:5240
dotnet run --project samples/ConsoleExtractorSample
dotnet run --project samples/ConsoleMcpSample                        # also needs MY_CONTEXT7_API_KEY
dotnet new install samples/templates && dotnet new ason.console   # add --force to refresh an older install

# not separated, script host remote: start the runner, then let the app use it
dotnet run --project samples/RemoteRunnerService/RunnerServiceSample.csproj    # http://localhost:5236
#   in samples/WptDemoApp/ViewModels/ChatViewModel.cs uncomment:
#     UseRemoteRunner = true, RemoteRunnerBaseUrl = "http://localhost:5236"

# --- separated: the application side (no model, no key) ---
dotnet run --project samples/WpfAppOnlyDemo -- --bridge-only --port 5222
#   gRPC   http://localhost:5222
#   MCP    http://localhost:5223/mcp
#   HTTP   http://localhost:5223/ason/openapi.json
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222    # same endpoints, LibDemo operators

# --- separated: an agent side ---
dotnet run --project samples/WpfAgentDemo                            # chat window; needs the key only for chat
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222          # gRPC self-check, no key
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # MCP self-check, no key
Ason.Bridge.McpHost --url http://localhost:5222                      # stdio MCP relay for Claude Desktop/Code
# copy-pasteable MCP client configurations (stdio and HTTP) and a minimal caller to check them:
#   samples/mcp/claude_desktop_config.json · samples/mcp/http_mcp_config.json · samples/mcp/README.md
python samples/python/ason_mcp_caller/main.py --transport http --list

# the same agent side as a console program, on any OS and with no key needed to inspect it
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5223/mcp --transport mcp --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --send "add 20 and 22"   # needs the key

# the application can also evaluate scripts in a child process, keeping its operators to itself
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222 --execution external

# --- separated: no agent, just a program ---
#   against the console host above (its operators come from LibDemo)
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func LibDemoOperator.GetProducts
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);" --stream
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add \
     -H "Content-Type: application/json" -d '{"arguments":[40,2]}'

#   against the WPF application above (its own operators)
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/GetDiagnostics \
     -H "Content-Type: application/json" -d '{}'
```

The automated proof of the separated rows is the bridge suite: it starts the real WPF application headless, drives
it over gRPC **and** MCP, starts the real agent sample in `--verify` mode, starts the relay as a real stdio MCP
server, and asserts the returned values — no model key involved anywhere:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release
```

The single-process demo keeps its own UI-level proof, which needs an interactive Windows desktop:

```bash
dotnet test tests/WpfDemoApp.UiTests/WpfDemoApp.UiTests.csproj --configuration Release   # set WPF_DEMO_TFM if needed
```

## Known limits

- `manifest.proxies` is a snapshot taken when the caller read it, so its instance declarations age. Compare
  `instancesRevision`, send the body only with `includeInstanceDeclarations`, or set `Proxies` on the runner
  transport (see [Live instances and manifest freshness](#live-instances-and-manifest-freshness)). The
  single-function interface needs none of that and is the robust path for instance operators.
- Invoking a method on an operator whose view is not loaded triggers the runtime's normal reload, which can
  open or navigate the view. Use attached instances when a side effect is not wanted.
- The `Ason.Bridge.Grpc` namespace shadows the `Grpc` root namespace inside files that import it: write
  `using Grpc.Net.Client;` and then `GrpcChannel.ForAddress(...)`, or fully qualify the type.
- Pass-through can only name a server and a tool; the bridge does not mirror the tool list of the MCP servers
  the application consumes, so a caller learns them from the application (or from the agent's own
  configuration) rather than from the manifest.
