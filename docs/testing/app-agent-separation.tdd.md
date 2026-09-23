# TDD evidence report — application / agent separation (ASON bridge)

**Branch**: `feat/agent-app-separation-bridge`
**Scope of this round**: all five phases of the approved plan — the bridge core, the transport seam, the gRPC
adapter, the MCP adapter (Streamable HTTP and the stdio relay), the external gRPC client demo, the two WPF
demos (application side and agent side) and the HTTP/OpenAPI adapter.
**Source plan**: the plan was produced and approved in the `/eccplan` session (Phase 0–3, with the added
requirement of a gRPC client executable demonstrating precise function execution, and TDD throughout). No
`*.plan.md` file was written to disk, so this report records the plan-to-test mapping directly.

## User journeys

1. As an **application author**, I want to publish my `[Ason*]` operators plus an MCP/gRPC service and keep
   the model out of my process, so that data and credentials never leave the application.
2. As an **agent author**, I want to ask a running application what it can call, and then either run a whole
   script or call one function precisely, so that I can drive it without shipping its operators.
3. As an **operator author**, I want the two execution interfaces to be independently switchable and to
   compose, so that a deployment can expose exactly the surface it needs.
4. As an **integrator**, I want a new transport (gRPC today, MCP today, OpenAPI or something else tomorrow)
   to be a separate project that plugs into the same contract, so that the ason runtime stays free of mature
   transport libraries.
5. As a **desktop application author**, I want operator calls to keep running on the UI thread even when the
   request arrives over the network, so that operators can touch UI-bound state safely.

## Task report

### Phase 0 — branch and skeleton

- Created `feat/agent-app-separation-bridge`; added `src/Ason.Bridge` (multi-target `net6.0;net9.0`) to
  `Ason.sln`.
- Later entries created the same way: `src/Ason.Bridge.Grpc`, `src/Ason.Bridge.Mcp`,
  `src/Ason.Bridge.McpHost`, `samples/ConsoleGrpcBridgeHost`, `samples/ConsoleGrpcBridgeDemo`,
  `tests/Ason.Bridge.Tests`.
- Validation: `dotnet build src/Ason.Bridge/Ason.Bridge.csproj -c Release` → success (after dropping two
  explicit `Microsoft.Extensions.*` references that caused `NU1605` downgrades against what `Ason` resolves).

### Phase 1 — bridge core (`Ason.Bridge`)

- **RED** (`test: add failing Ason.Bridge contract tests (RED)`): 12 test methods across manifest,
  capability, script-execution, function-invocation, validation, executor-selection and
  synchronization-context behaviour, referencing `AsonBridgeRuntime`, `AsonBridgeOptions`,
  `AsonBridgeCapabilities`, `AsonBridgeExecution`, `AsonBridgeManifest`, `AsonBridgeFunctionCall`,
  `AsonBridgeCallResult`, `AsonBridgeInstance`, `IAsonExecutor` and `AsonExecutors`.
  - Command: `dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release`
  - Result: build failure, `error CS0246` for every planned type (the trimmed excerpt lists 8 of them;
    the exact total was not preserved).
- **GREEN**: implemented the contract, `RunnerClientAsonExecutor` (reusing the runtime's existing
  in-process / external-process / Docker / remote-runner switches) and `AsonBridgeRuntime`.
  - First run: 34 passed / 2 failed, both **test-side** defects, not implementation defects:
    - the API listing reports CLR names (`Int32`), matching `OperatorApiCatalogTests` — the expectation was
      wrong, and the implementation was already consistent with the prompt text;
    - `BridgeStaticOperator.AlwaysFails()` is `void`, so `return BridgeStaticOperator.AlwaysFails();` cannot
      compile; the script now calls it as a statement.
  - After the corrections: `36 passed / 0 failed`.
- Additive changes to `Ason` required by this phase (no behaviour change):
  - `RootOperator.OperatorInstances` made publicly readable (a host must be able to hand the bridge its live
    operator directory);
  - `OperatorBase.IsAttached` added (the manifest reports whether an instance is attached);
  - `OperatorVariableDeclarations` extracted, with `AsonClient` delegating to it, so the script prompt and
    the scripts accepted over a transport are declared by the same rules;
  - `KeywordScriptValidator` made public, so the bridge applies the same keyword filter instead of a copy.
  - Regression check: `tests/Ason.Tests` 101/101, `tests/LibDemo.SmokeTests` (net9.0) 11/11.

### Phase 2 — transport seam in `Ason`

- **RED**: `tests/Ason.Tests/Transport/CustomTransportTests.cs` → `error CS1061: RunnerClient does not
  contain UseTransport` (and `IRunnerTransport` was not reachable).
- **GREEN**: `IRunnerTransport` made public; `RunnerTransportSettings.TransportFactory` and
  `RunnerClient.UseTransport(...)` added; `RunnerTransportManager` prefers the factory and counts it when
  deciding whether a transport is needed at all.
- Validation: `tests/Ason.Tests` 104/104 (the 3 new tests plus the previous 101).

### Phase 3 — gRPC adapter

- **RED**: `error CS1061`/`CS0103` for `AddAsonGrpcBridge`, `MapAsonGrpcBridge`, `GrpcAsonBridgeClient`,
  `GrpcAsonBridgeTransport`.
- **GREEN**: `ason_bridge.proto` (`GetManifest`, `ListInstances`, `ExecuteScript`, `InvokeFunction`,
  `StreamExecution`), `GrpcAsonBridgeService`, `GrpcAsonBridgeClient`, `GrpcAsonBridgeTransport`,
  `GrpcAsonBridgeEndpoint`, `AsonBridgeGrpcExtensions`.
- Environment risk retired first with a throw-away spike: Kestrel h2c + `Grpc.AspNetCore` 2.71.0 +
  `Grpc.Tools` codegen compile and round-trip a call on this machine.
- Validation: `tests/Ason.Bridge.Tests` 46/46 (the 10 new tests: manifest, function invocation, structured
  error, script body, disabled capability as `Unimplemented` on the wire *and* as a result on the typed
  client, log+result streaming, live-instance listing, transport exec round trip, invoke answered with an
  error, log relay).

### Phase 3b — MCP adapter and stdio relay

- **RED**: `error CS0234: namespace 'Ason.Bridge' does not contain 'Mcp'`.
- **GREEN**: `AsonBridgeMcpTools` (tools registered only for enabled capabilities), `AsonBridgeMcpExtensions`
  (Streamable HTTP + stdio), `McpAsonBridgeClient`, `McpAsonBridgeTransport`, `IAsonBridgeEndpoint` (so an
  adapter can serve either a runtime that owns operators or a relay), `GrpcAsonBridgeEndpoint`, and the
  `Ason.Bridge.McpHost` relay executable.
- One implementation fix during GREEN: `McpClient.ListToolsAsync` returns `IList<T>`; the wrapper returns
  `IReadOnlyList<T>` via `ToList()`.
- One test-side fix: the MCP transport test used an instance variable, so the complete script needed the
  instance declarations (`OperatorVariableDeclarations.Build`) — which is exactly what a client reads from
  `manifest.proxies`.
- Validation: 54/54, then 57/57 after the relay-endpoint tests.

### Phase 3c — external request side and host samples

- `samples/ConsoleGrpcBridgeHost` (application side: `[Ason*]` operators from `LibDemo`, gRPC + MCP, no
  agent) and `samples/ConsoleGrpcBridgeDemo` (manifest, live instances, single functions, scripts, streamed
  logs).
- `AsonBridgeOperators.MaterializeMarkerOnly` added (with tests) because a host has to materialise
  marker-only operators the way `AsonClient` does; the console host uses it.
- Demo fixed to report an invalid `--args` as a message and exit code 2 instead of an unhandled
  `JsonException`.
- Validation: `tests/Ason.Bridge.Tests` 60/60.
- **Manual end-to-end check** (host on port 5599, released builds):

  | Command | Result |
  |---|---|
  | `ConsoleGrpcBridgeDemo --url http://localhost:5599` | `Connected to 'LibDemo application' (protocol 1.0, execution in-process).` · `2 operators, 5 methods, 0 live instances` |
  | `--instances` | `LibDemoOperator (LibDemoOperator, initialized=True)` |
  | `--func LibDemoStaticOperator.Add --args "[2,3]"` | `OK 5` |
  | `--func LibDemoOperator.GetProducts` (non-static, no handle, resolved through the live instance) | `OK [{"id":1,...},{"id":2,...}]` |
  | `--script "return LibDemoStaticOperator.Add(40, 2);"` | `OK 42` |
  | `--script "return LibDemoStaticOperator.Repeat(new string(new[]{'a','b'}), 3);"` | `OK "ababab"` |
  | `--script ... --stream` | `[Information] Evaluating script via Roslyn. Length=...` then the result |
  | `--func NoSuchOperator.Do` | `FAILED [operator-not-found] Unknown operator 'NoSuchOperator'. Read the manifest for the operators this application exposes.` |
  | `--func LibDemoOperator.GetLibraryTimestamp` (no live instance) | `FAILED [handle-required] ... pass the handle of a live instance.` |
  | `--args "not json"` | `--args is not valid JSON: ...`, exit code 2 |

### Phase 4a — the WPF application side

- `samples/WpfAppOnlyDemo` (`net9.0-windows`): a WPF window whose operator root owns the employee collection
  the `ListView` is bound to, plus a static module, a marker-only module and the `LibDemo` class library. It
  hosts gRPC, MCP and HTTP/OpenAPI in the same process and contains no model client.
- Two details were needed to make a windowless run work: the runtime is built on the dispatcher thread (so it
  captures the `DispatcherSynchronizationContext`) and bridge-only mode sets
  `ShutdownMode.OnExplicitShutdown`, because the default mode ends the process as soon as the dispatcher idles
  with no window open.
- **The deployment risk in the plan — WPF and ASP.NET Core in one `WinExe` — is retired**: the sample builds
  and runs with `FrameworkReference Microsoft.AspNetCore.App` and `UseWPF` together.
- Validation: build, then the manual run recorded below; later automated by the tests in Phase 4b.

| Command (against `WpfAppOnlyDemo --bridge-only --port 5210`) | Result |
|---|---|
| `ConsoleGrpcBridgeDemo --url http://localhost:5210` | `5 operators, 12 methods, 3 live instances`, including `EmployeesOperator (initialized=True)` |
| `--func EmployeesOperator.GetDiagnostics` | `OK {"onUiThread":true,"threadId":2,"employeeCount":3}` |
| `--script "return employeesOperator.GetDiagnostics().OnUiThread;"` | `OK true` |
| `--script "return employeesOperator.Rename(1, new string(new[]{'A','d','a'})).Name;"` | `OK "Ada"` |
| `--func EmployeesOperator.GetEmployees` (after the mutation) | `name` is now `Ada` |
| `--func ReportOperator.BuildSummary` (marker-only) | `OK "machine ... · operators are resolved inside the application process"` |
| `--func AppInfoOperator.Add --args "[40,2]"` (static module) | `OK 42` |

### Phase 4b — the WPF agent side, and the seam it needs

- `AsonClientOptions.TransportFactory` was added (RED: `error CS0117: AsonClientOptions does not contain
  TransportFactory`), applied in the `AsonClient` constructor. This is what lets an agent host configure a
  bridge endpoint declaratively; `RunnerClient.UseTransport` remains the underlying switch.
- `samples/WpfAgentDemo` (`net9.0-windows`): chat window, endpoint and transport selection (gRPC/MCP), the API
  listing fetched from the application, and a call log. It declares **no** `[AsonOperator]`.
- `--verify <endpoint> [--mcp]` gives the sample a headless self-check, which is what makes the agent half
  testable without a model and without a desktop session.
- Validation: `tests/Ason.Bridge.Tests` 66/66 at this point (the two new end-to-end tests below), and
  `tests/Ason.Tests` 105/105 including the new options-level transport test.
- One flake was found and fixed rather than retried: two WPF samples starting in parallel could claim the same
  port pair. The fixture now reserves a consecutive pair before launching and both test classes share a
  non-parallel xunit collection.

### Phase 5 — the HTTP/OpenAPI adapter

- `src/Ason.Bridge.OpenApi`: `GET /ason/manifest`, `GET /ason/instances`, `POST /ason/script`,
  `POST /ason/functions/invoke`, `POST /ason/functions/{operator}/{method}` and `GET /ason/openapi.json` — the
  document is generated from the manifest (routes, schemas, and the callable surface as `x-ason-operators` /
  `x-ason-models` / `x-ason-capabilities`).
- Capability gating is "not served at all" (404), failures are `400` with the ASON error code, and an optional
  `ApiKey` guards every route with `401`.
- The adapter was added without touching `Ason.Bridge`, the runtime, the application or the other adapters —
  which is the claim the transport story makes, tested rather than asserted.
- Validation: 7 new tests (73/73 in the suite); manual check against `ConsoleGrpcBridgeHost` returned
  `openapi=3.0.3 title=LibDemo application operators=2`, five mapped paths, an HTTP function call (`result=42`)
  and an HTTP script call (`result=3`).

## Test specification

| # | What is guaranteed | Test file | Type | Result |
|---|---|---|---|---|
| 1 | The manifest describes every marked operator, model, method and parameter, and reports the effective capabilities, execution location and protocol version | `ManifestTests` | unit | PASS |
| 2 | The manifest survives a JSON round trip (it crosses a process boundary) | `ManifestTests` | unit | PASS |
| 3 | Live instances are listed, declared to scripts as variables, and the root operator is not advertised | `ManifestTests`, `BridgeOperatorsTests` | unit | PASS |
| 4 | Marker-only operators are materialised and callable by type name | `BridgeOperatorsTests` | unit | PASS |
| 5 | A script body runs against the generated proxy layer, with or without the preamble, and can use instance variables | `ScriptExecutionTests` | unit | PASS |
| 6 | A single operator method is called precisely: static module without a handle, instance operator through the live directory, explicit handle, JSON/model arguments | `FunctionInvocationTests` | unit | PASS |
| 7 | Failures map to stable codes: operator-not-found, handle-required, handle-ambiguous, method-not-found, execution-failed | `FunctionInvocationTests` | unit | PASS |
| 8 | A disabled capability is refused and never reaches the executor; both execution interfaces compose | `CapabilityTests` | unit | PASS |
| 9 | Scripts are rejected by the keyword filter and by the empty-script check before execution | `ScriptValidationTests` | unit | PASS |
| 10 | The execution location resolves to an executor, and an injected executor wins | `ExecutorSelectionTests` | unit | PASS |
| 11 | Operator calls are marshalled to the captured synchronization context (function and script interfaces) | `SynchronizationContextTests` | unit | PASS |
| 12 | A host-supplied runner transport is used, wins over the execution mode, and is recreated after a restart | `Ason.Tests/Transport/CustomTransportTests` | unit | PASS |
| 13 | The gRPC service serves the same contract: manifest, instances, script body, single function, structured errors, streamed logs | `GrpcBridgeTests` | integration | PASS |
| 14 | A disabled capability is `StatusCode.Unimplemented` on the wire and `not-supported` on the typed client | `GrpcBridgeTests` | integration | PASS |
| 15 | The runner protocol travels over gRPC (exec round trip), an unexpected invoke is answered with an error, logs are relayed on request | `GrpcBridgeTransportTests` | integration | PASS |
| 16 | The MCP tool surface follows the capabilities (tool absent when disabled) | `McpBridgeTests` | integration | PASS |
| 17 | Manifest, single-function call, script body and structured errors work over MCP; the script API tool returns the prompt layer | `McpBridgeTests` | integration | PASS |
| 18 | The MCP transport carries exec lines to the application | `McpBridgeTests` | integration | PASS |
| 19 | A forwarding endpoint reports the remote capabilities/instances, relays both interfaces and builds the MCP tool surface (the stdio relay's path) | `RelayEndpointTests` | integration | PASS |
| 20 | `AsonClientOptions.TransportFactory` sends the client's script through the supplied transport | `Ason.Tests/Transport/AsonClientTransportFactoryTests` | unit | PASS |
| 21 | An agent built from a manifest runs a script inside the application and returns its result, with the application's API in the script prompt | `AgentOverBridgeTests` | integration | PASS |
| 22 | A failure raised by an operator inside the application comes back as a failed task with the application's error text | `AgentOverBridgeTests` | integration | PASS |
| 23 | The real WPF application (started headless) publishes its operators, live instances and script declarations, and its operator calls run on the dispatcher thread | `WpfApplicationEndToEndTests` | e2e | PASS |
| 24 | The same application answers over MCP with the same contract and the same function-call result | `WpfApplicationEndToEndTests` | e2e | PASS |
| 25 | The WPF agent sample connects over gRPC, declares zero operators, reads the application's API and executes a function there | `WpfAgentEndToEndTests` | e2e | PASS |
| 26 | The same agent sample works over MCP | `WpfAgentEndToEndTests` | e2e | PASS |
| 27 | The HTTP adapter serves the manifest, instances, script bodies, both function-call shapes, structured failures, capability gating, the bridge key and a coherent OpenAPI document | `OpenApiBridgeTests` | integration | PASS |

Commands used for every row above:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release
dotnet test tests/Ason.Tests/Ason.Tests.csproj -c Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj -c Release --framework net9.0
```

Final counts: `Ason.Bridge.Tests` 73/73, `Ason.Tests` 105/105 (hermetic filter), `LibDemo.SmokeTests` 11/11.
On Windows with the samples built, 4 of the 73 are end-to-end tests that start the real WPF executables; on
Linux they report as skipped instead.

## Coverage and known gaps

Coverage collected with
`dotnet test tests/Ason.Bridge.Tests --collect:"XPlat Code Coverage" --results-directory artifacts/coverage`:

| Assembly | Line rate | Branch rate |
|---|---|---|
| `Ason.Bridge` | 88.9% | 79.9% |
| `Ason.Bridge.Mcp` | 85.4% | 48.4% |
| `Ason.Bridge.OpenApi` | 98.3% | 70.0% |
| `Ason.Bridge.Grpc` | 53.8% | 43.7% |
| `Ason.Abstractions` | 84.6% | 100% |

`Ason.Bridge`, `Ason.Bridge.Mcp` and `Ason.Bridge.OpenApi` clear the 80% line bar. `Ason.Bridge.Grpc` does not,
and the reason is worth stating precisely: the generated protobuf/gRPC stubs dominate that assembly's line
count, and the service's less common branches (malformed `arguments_json`, cancelled streaming, error paths
inside `StreamExecution`, `GrpcAsonBridgeEndpoint`'s `not-supported` reply) are not all exercised. Closing it
means either excluding generated code from the metric or adding explicit error-path tests; it is not a sign of
untested behaviour on the paths the adapters actually use, all of which the suite drives over a real channel.

Known gaps, stated rather than implied:

1. **The stdio relay is verified to build, and its forwarding path is covered by `RelayEndpointTests`, but no
   test spawns an MCP client over stdio into the relay process.** The relay's own logic is thin (connect,
   forward, republish), so this is a coverage gap rather than an untested claim, but it is a gap.
2. **`import`-level coverage of the WPF samples is not measured**: the samples are exercised end to end
   (processes started, calls made, results asserted) but no coverage is collected for the sample assemblies.
3. **`invokeMcpTool` is not part of the gRPC contract** (its relay endpoint answers `not-supported`), so the
   capability is only available on a bridge that owns the MCP clients. The MCP and HTTP adapters expose
   everything the runtime offers; the gRPC proto does not carry this one capability.
4. **`Ason.Bridge.Grpc` line coverage is 53.8%** for the reason given above (generated stubs and a few error
   branches).
5. **The agent sample's chat requires a model key** (`MY_OPEN_AI_KEY`); without one the window still connects,
   lists the API and shows the call log, but the chat itself cannot be exercised in CI. The agent's
   *integration* with the bridge is covered by `AgentOverBridgeTests` (scripted chat service) and by
   `WpfAgentEndToEndTests` (`--verify`), which is why the missing key does not leave the agent path untested.
6. **The `--verify` self-check and `--bridge-only` mode are sample-level test hooks.** They exist for the
   tests and are documented as such; they are not part of the library API.
7. **One test project, not two.** The plan listed a separate `tests/Ason.Bridge.IntegrationTests`; the
   integration and end-to-end tests live in `tests/Ason.Bridge.Tests` instead, which keeps the CI wiring
   smaller. The WPF-dependent ones skip on a machine where the samples are not built, so the suite stays green
   everywhere while still being real where it can be.
8. **Test-side corrections are recorded, not hidden.** Phase 1: CLR type names (`Int32`) and a void operator
   called as a statement. Phase 3b: a complete script needs the instance declarations it reads from the
   manifest. Phase 4b: two assertions about what an agent returns with the explainer switched off, and a port
   race in the WPF fixture (fixed by reserving a port pair and serialising the tests). In every case the
   expectation was wrong, not the implementation.
9. **The Phase 1 RED commit message claims "24x error CS0246"**; the exact count was not preserved (the
   output was trimmed to 8 lines). The intended RED signal — every planned type missing — is not in doubt.

## Merge evidence

The work is carried by checkpoint commits on this branch (one per TDD stage, in order):

| Commit | Stage |
|---|---|
| `test: add failing Ason.Bridge contract tests (RED)` | Phase 1 RED |
| `stage: GREEN for Ason.Bridge core ...` | Phase 1 GREEN |
| `feat: let a host supply the runner transport (IRunnerTransport made public) - GREEN` | Phase 2 RED+GREEN |
| `feat: add Ason.Bridge.Grpc adapter (service, typed client, runner transport) - GREEN; evidence: 46/46 bridge tests pass` | Phase 3 gRPC RED+GREEN |
| `feat: MCP adapter, stdio relay host and the external gRPC client - GREEN` | Phase 3b/3c |
| `docs: document application/agent separation and wire the new projects into CI` | docs, CI, first evidence report |
| `feat: WPF application-side demo plus end-to-end tests over gRPC and MCP, and an options-level transport seam - GREEN` | Phase 4a + the transport option |
| `feat: WPF agent demo plus agent/application end-to-end verification over gRPC and MCP - GREEN` | Phase 4b |
| `feat: OpenAPI adapter, Windows CI job and the completed documentation - GREEN` | Phase 5, docs, CI, this report |

Copy the RED/GREEN summary above into the pull-request body if these commits are squashed.
