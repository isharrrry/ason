# TDD evidence report — application / agent separation (ASON bridge)

**Branch**: `feat/agent-app-separation-bridge`
**Scope of this round**: Phases 0–3 of the approved plan (bridge core, transport seam, gRPC adapter, MCP
adapter, stdio relay, external gRPC client demo). Phases 4–5 (WPF app-only and agent demos, OpenAPI adapter)
are **not** done and are listed under known gaps.
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

Commands used for every row above:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release
dotnet test tests/Ason.Tests/Ason.Tests.csproj -c Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj -c Release --framework net9.0
```

Final counts: `Ason.Bridge.Tests` 60/60, `Ason.Tests` 104/104 (hermetic filter), `LibDemo.SmokeTests` 11/11.

## Coverage and known gaps

Coverage collected with
`dotnet test tests/Ason.Bridge.Tests --collect:"XPlat Code Coverage" --results-directory artifacts/coverage`:

| Assembly | Line rate | Branch rate |
|---|---|---|
| `Ason.Bridge` | 88.9% | 79.9% |
| `Ason.Bridge.Mcp` | 85.4% | 48.4% |
| `Ason.Bridge.Grpc` | 53.8% | 43.6% |
| `Ason.Abstractions` | 84.6% | 100% |

`Ason.Bridge` clears the 80% line bar. `Ason.Bridge.Grpc` does not, for two reasons that are worth knowing:
the generated protobuf/gRPC stubs dominate that assembly's line count, and the service's less common branches
(malformed `arguments_json`, cancelled streaming, error paths inside `StreamExecution`) are not all exercised.
Closing it means either excluding generated code from the metric or adding explicit error-path tests.

Known gaps, stated rather than implied:

1. **Phase 4 not started.** Neither WPF demo (app-only, agent) exists; the app side is demonstrated as
   `samples/ConsoleGrpcBridgeHost` instead. UI-thread affinity is proven with a
   `SingleThreadSynchronizationContext`, not with a real WPF dispatcher.
2. **Phase 5 (OpenAPI adapter) not started.**
3. **`AsonClient`-over-bridge is only covered at the transport level.** No test builds an `AsonClient` whose
   `OperatorsLibrary` comes from a manifest and whose runner is a bridge transport; that composition needs a
   stub chat completion service and belongs with the Phase 4 agent demo.
4. **The stdio relay is verified to build and its forwarding endpoint is unit-tested; no test spawns an MCP
   client over stdio** to the relay process.
5. **`invokeMcpTool` is not part of the gRPC contract** (its relay endpoint answers `not-supported`), so the
   capability is only available on a bridge that owns the MCP clients.
6. **One test project, not two.** The plan listed a separate `tests/Ason.Bridge.IntegrationTests`; the
   integration tests live in `tests/Ason.Bridge.Tests` instead, which keeps the CI wiring smaller.
7. **Two test-side corrections in Phase 1** (CLR type names, void operator as a statement) and **one in
   Phase 3b** (complete script needs the instance declarations) are recorded above rather than hidden: in
   each case the expectation was wrong, not the implementation.
8. **The Phase 1 RED commit message claims "24x error CS0246"**; the exact count was not preserved (the
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
| `docs: document application/agent separation and wire the new projects into CI` | docs, CI, this report |

Copy the RED/GREEN summary above into the pull-request body if these commits are squashed.
