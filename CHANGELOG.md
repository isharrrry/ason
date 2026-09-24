# Changelog

All notable changes to this repository are recorded here. The version applies to every package it publishes.

## 0.10.0

Framework reach: the runtime now ships a single `netstandard2.0` asset (so a legacy .NET Framework host can
embed the scripting engine), the adapters cover `net6.0`/`net9.0`/`net10.0`, and a `net6.0` application that
cannot host an MCP server is driven through the stdio relay instead.

### Frameworks

| Package | Assets |
|---|---|
| `Ason.Abstractions`, `Ason.Runner.Core`, `Ason` | `netstandard2.0` — one asset, consumed by .NET Framework 4.6.2+ and by every modern .NET |
| `Ason.Bridge`, `Ason.Bridge.Grpc`, `Ason.Bridge.OpenApi`, `Ason.RemoteBridge`, `Ason.ExternalExecutor` | `net6.0`, `net9.0`, `net10.0` |
| `Ason.Bridge.Mcp` | `net9.0`, `net10.0` — the official MCP SDK requires net8+, and net8 is not a tier this repository ships |

- **A `net6.0` application is a supported shape without embedding MCP.** It publishes gRPC (and HTTP/OpenAPI)
  and an MCP agent reaches it through `Ason.Bridge.McpHost` — a separate tool process, so the relay's own
  framework never constrains the application. The manifest stays honest about it: `capabilities.invokeMcpTool`
  is `false` and the tool list has no `ason_invoke_mcp_tool`.
- **`Ason.Bridge` is deliberately not `netstandard2.0`**: its abstractions use default interface members
  (`IAsonExecutor.McpServers`, `IAsonBridgeEndpoint.ExecuteScriptWithLogsAsync`), which netstandard2.0 cannot
  compile — and every adapter needs the ASP.NET Core shared framework anyway.
- **A `netstandard2.0` host uses in-process execution.** `Ason.ExternalExecutor` is an executable, so a .NET
  Framework host cannot run it out of process; `ExecutionMode.ExternalProcess` and `docker` need .NET 6+.
- **`Ason.ExternalExecutor` ships one host-manifest pair per framework** (`buildTransitive/host/<tfm>/`) and its
  targets copy the pair matching the consuming project, failing loudly (`ASONEXEC001`) when there is none.
- **Every sample follows the same rule, and the rule is now enforced by a test.** The console caller (`ConsoleBridgeCallerSample`)
  and agent (`ConsoleAgentSample`) samples carry the three runtime legs of the application sample — the caller needs
  no conditional compilation at all, the agent gates its MCP client exactly like the WPF one — and so do
  `ConsoleMcpSample` and `ConsoleExtractorSample`, whose only dependency is the `netstandard2.0` runtime.
  `tests/Ason.Bridge.Tests/BuildMatrixTests.cs` holds the whole repository in one table: every project, the
  frameworks it must declare, how CI reaches it, and — for the four deliberate exceptions — why.

### Verified

`tests/LibDemo.SmokeTests` gained a `net472` leg — the only place where what a .NET Framework host receives is
actually *run* — and `Ason.Bridge.Tests` a `net6.0` application + relay end-to-end test. That suite runs on
`net9.0` *and* `net10.0`, and the library, runner, remote-runner and UI suites run on `net6.0`, `net9.0` and
`net10.0` (`net6.0-windows`/`net9.0-windows`/`net10.0-windows` for the UI tests) — always one `--framework` leg at a
time, because a multi-target `dotnet test` writes a single TRX for all of its frameworks and the annotations would
then describe one framework while the step was supposed to run three. CI installs the .NET 6, 9 and 10 SDKs, runs
the smoke tests on `net6.0`/`net9.0`/`net10.0` plus the `net472` leg on Windows, and builds every framework of the
WPF samples, both console-side bridge samples, the agent-side samples, the test fixtures and the templates.

An audit of that inventory (prompted by `ConsoleBridgeCallerSample` having stayed single-target while its siblings
went multi-target) found four classes of problem, all fixed here:

- **Projects that no build list named**: `samples/BlazorAdvancedApp`, `samples/ConsoleMcpSample`,
  `samples/ConsoleExtractorSample`, `samples/RemoteRunnerService/RemoteRunnerService.csproj`,
  `tests/TestMcpServer`, `tests/TestRemoteExecutorServer` and the template halves were in no CI build list at all,
  so nothing would have noticed them breaking. They are all built now, and the matrix test fails when a project is
  absent from the inventory or from CI.
- **A `ProjectReference` to a project that no longer exists**: `Ason.RemoteRunner.Tests` still referenced
  `src/Ason.Runner/Ason.Runner.csproj`, deleted in an earlier wave (its `AsonRunner` namespace lives in
  `Ason.Runner.Core` now). MSBuild reports a missing project as the *warning* `MSB9008`, so the suite kept building
  and staying green against a reference that resolved to nothing. The reference is gone and a test now fails on any
  dangling `ProjectReference`.
- **A sample that no longer compiled**: `samples/RemoteRunnerService/RemoteRunnerService.csproj` still added the
  retired `Ason.RemoteRunner` package (0.2.x), whose API no longer contains `AddAsonScriptRunner`/`MapAson` — the two
  projects in that folder share one `Program.cs`. It now consumes the published `Ason.RemoteBridge` and builds in CI.
- **A test suite left on one leg by an unused package**: `tests/Ason.Tests` referenced
  `Microsoft.AspNetCore.Mvc.Testing 8.0.1` (a `net8.0` asset only) — which nothing in the repository ever used
  (no `WebApplicationFactory` anywhere). With the unused reference removed and `LangVersion latest` set (the
  `net6.0` leg otherwise compiles as C# 10 and the sources use primary constructors), the whole library suite runs
  on `net6.0` too.

**Every project that used to stop at `net9.0` now carries `net10.0` as well** — `samples/BlazorAdvancedApp`,
`samples/RemoteRunnerService` (both projects), `tests/TestMcpServer`, `tests/TestRemoteExecutorServer`, the template
package and all six template halves (the MAUI app half gained `net10.0-android`/`-ios`/`-maccatalyst` plus the Windows
`net10.0-windows10.0.19041.0` leg). Where the leg changes what a *package* must be, the reference is pinned per leg:
`Microsoft.AspNetCore.OpenApi` is `9.0.x` on `net9.0` and `10.0.x` on `net10.0`, in the three projects that use its
document as a readiness probe. Elsewhere the `net10.0` leg consumes the published packages' `net9.0` asset through
NuGet's nearest-compatible rule, which is why the templates' `Ason`/`Ason.ExternalExecutor` references stay floating.

What stays narrower, recorded with its reason in the matrix test: `Ason.Bridge.Mcp`/`Ason.Bridge.McpHost`
(`net9.0`+, the official MCP SDK's floor) and the MAUI template's app half — it now *declares* both tiers' platform
legs, but none of them can be built here or on the hosted runners, because that needs the android/ios/maccatalyst
workloads (its platform-neutral server half is built on both tiers instead).

Adding the `net6.0` leg to `tests/Ason.Tests` immediately found one place where a shipped dependency's *asset
selection* changes behaviour, and it turned out to be a defect in this repository rather than in the dependency:
`AsonClient`'s streaming pipeline runs its orchestration on a background task that converts a cancelled run into a
**completed** channel (an exception cannot cross a channel), and only the consumer's `WaitToReadAsync(token)` could
surface the cancellation. Which side notices the token first is a race, and the runtime decides it: on
`net9.0`/`net10.0` the `net8.0` asset of SemanticKernel loses the race in the consumer's favour, while on `net6.0`
the `netstandard2.0` asset does not — so a **cancelled answer looked like a finished one** on that tier
(`SendStreamingAsync` returned normally, and a caller could report success for an abandoned run). The client now
re-checks the token after the channel completes, so cancellation ends the enumeration with
`OperationCanceledException` on every runtime, which is the .NET convention; a test asserts both halves (the stream
does not complete, and the cancellation is reported as OCE) and a second one guards the opposite (an uncancelled,
complete stream still ends normally). This is a behaviour change for callers that cancelled and relied on the
graceful end — the direction is the platform's own.

Giving `tests/TestMcpServer` its `net10.0` leg surfaced a second, more serious one: the fixture **could not start at
all** on either tier. `ModelContextProtocol`'s asset asks for `Microsoft.Extensions.Primitives 9.0.0.0` while the
fixture's own `Microsoft.Extensions.Hosting 8.0.1` reference put `8.0.0` in its output, so the host threw
`FileNotFoundException` before answering a single request — the same class of version split the `net472` smoke leg
found for `Microsoft.Bcl.AsyncInterfaces`. The `Microsoft.Extensions.*` references now follow the leg (`9.0.10` on
`net9.0`, `10.0.0` on `net10.0`), and the stdio handshake (`initialize` → `tools/list`) is verified on both.

### Compatibility

Additive for source. What changes is which asset a framework resolves: `Ason` no longer offers a `net6.0`/`net9.0`
asset (consumers get the `netstandard2.0` one), and `Ason.Bridge.Mcp` is unavailable below `net9.0`.

Two consequences of the `net6.0` floor are visible in build logs and are deliberately not silenced. `net6.0` is out
of support upstream, so MSBuild reports `NETSDK1138` for those targets; and several dependencies (`System.Collections.Immutable 9.0.0`,
`Microsoft.Extensions.* 9.0.10`, `Microsoft.Bcl.*`, `System.Net.ServerSentEvents`) ship `buildTransitive` checks that
warn they are untested on `net6.0`. They are consumed through their `netstandard2.0`/`net6.0` assets and the
`net6.0` end-to-end tests exercise them at run time, but `SuppressTfmSupportBuildWarnings` is not set: the warning is
real information for anyone deploying to a `net6.0` host.

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
