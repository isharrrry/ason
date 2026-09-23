# Contributing

[English](contributing.md) | [中文](contributing.zh-CN.md) | [Español](contributing.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

## Building

```bash
dotnet build Ason.sln --configuration Release
```

The whole solution builds on Windows. The WPF sample (`samples/WptDemoApp`) and the FlaUI UI tests target `*-windows`, so a Linux or macOS build of the entire solution is not expected to succeed — build the projects you need instead:

```bash
dotnet build src/Ason/Ason.csproj --configuration Release
```

## Repository layout

| Path | Contents |
|---|---|
| `src/Ason.Abstractions` | marker attributes only (`netstandard2.0`) |
| `src/Ason` | the runtime: client, orchestration, proxy generation (`net6.0`, `net9.0`) |
| `src/Ason.Runner.Core` | script execution host |
| `src/Ason.ExternalExecutor` | out-of-process runner executable |
| `src/Ason.RemoteBridge` | ASP.NET Core remote runner |
| `src/Ason.Bridge` | transport-neutral bridge: manifest, capabilities, executors, operator directory (`net6.0`, `net9.0`) |
| `src/Ason.Bridge.Grpc` | gRPC adapter: service, typed client, runner transport, forwarding endpoint |
| `src/Ason.Bridge.Mcp` | MCP adapter: tool surface, typed client, runner transport (Streamable HTTP) |
| `src/Ason.Bridge.OpenApi` | HTTP + OpenAPI (Swagger) adapter: endpoints and a document generated from the manifest |
| `src/Ason.Bridge.McpHost` | relay that republishes an application's gRPC bridge as stdio MCP |
| `samples/WpfAppOnlyDemo` | WPF application side: `[Ason*]` operators plus gRPC, MCP and HTTP/OpenAPI services, no agent |
| `samples/WpfAgentDemo` | WPF agent side: chat plus endpoint/transport selection, and not a single `[AsonOperator]` |
| `samples/ConsoleBridgeAppSample` | application side of a split deployment: `[Ason*]` operators plus gRPC and MCP services |
| `samples/ConsoleAgentSample` | agent side as a console program: builds its API from the application's manifest and drives it over gRPC or MCP (cross-platform, no UI) |
| `samples/ConsoleBridgeCallerSample` | external request side: manifest, single-function calls, scripts, streamed logs |
| `samples/WptDemoApp` | WPF demo (`net10.0`, `net9.0`, `net6.0-windows`) |
| `samples/LibDemo` | class library that only uses the markers (`net6.0`, `netstandard2.0`) |
| `samples/templates` | the `dotnet new` templates |
| `tests/*` | test suites, see below |

## Test suites

| Suite | Framework | Notes |
|---|---|---|
| `tests/LibDemo.SmokeTests` | net6.0 / net9.0 / net10.0 | marker discovery, type forwards and real in-process operator invocation |
| `tests/Ason.Tests` | net9.0 | the `E2E_AllExecutionModes(executionMode: Docker, …)` cases need a Docker daemon; `McpClientTests` needs live MCP servers |
| `tests/Ason.Runner.Tests` | net9.0 | script runner |
| `tests/Ason.RemoteRunner.Tests` | net9.0 | the integration test is skipped unless `ASON_REMOTE_RUNNER_URL` points at a running remote runner |
| `tests/Ason.Bridge.Tests` | net9.0 | the bridge core, the gRPC/MCP/OpenAPI adapters (each host is started in-process and driven over the wire), the runner transport seam, the relay endpoint, and the WPF samples' end-to-end tests — which skip on Linux and on a machine where the Windows-only samples have not been built |
| `tests/WpfDemoApp.UiTests` | net9.0-windows | FlaUI UI automation — needs an interactive Windows desktop session |

The bridge packages (`Ason.Bridge`, `Ason.Bridge.Grpc`, `Ason.Bridge.Mcp`, `Ason.Bridge.McpHost`) and their
test suite are cross-platform and run on Linux; see [application / agent separation](app-agent-separation.md).

Running the hermetic subset without Docker:

```bash
dotnet test tests/Ason.Tests/Ason.Tests.csproj --configuration Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
```

Coverage for the bridge suite is collected with `coverlet.runsettings`, which keeps the gRPC code protoc
generates out of the numbers so the percentage describes the hand-written adapters:

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

The four bridge projects have a floor, and the CI job enforces it per adapter (line ≥ 87%, branch ≥ 70%; as of
0.9.0 they sit at 89–98% and 75–82%):

```bash
./scripts/check-bridge-coverage.ps1 -CoverageFile 'artifacts/coverage/*/coverage.cobertura.xml'
```

Sample assemblies are deliberately not part of that floor: each sample is a separate process, and coverage is
collected inside the test host, so a sample's own assembly cannot be measured this way. The samples are covered
by the process-level end-to-end tests instead (`ConsoleSamplesEndToEndTests`, `WpfApplicationEndToEndTests`,
`RemoteRunnerBridgeEndToEndTests`), which build them first and then drive the real processes.

Environment variables that change what the UI tests do:

| Variable | Meaning |
|---|---|
| `WPF_DEMO_TFM` | which sample build to drive — `net9.0-windows` (default), `net6.0-windows` or `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release` (default) or `Debug` |
| `MY_OPEN_AI_KEY`, `MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL` | enable the live end-to-end test; without a key it is reported as skipped |
| `ASON_BRIDGE_KEY`, `ASON_BRIDGE_REMOTE_URL`, `ASON_BRIDGE_EXECUTION` | the relay's and the application sample's equivalents of `--key`, `--remote-url` and `--execution` |

Configuring a provider is described in [AI providers](ai-providers.md).

## Continuous integration

`.github/workflows/ci.yml` runs on every push and on pull requests. Its Linux job builds the cross-platform
projects (including the console and remote-runner samples) and runs the hermetic test suites; the Docker-mode
and MCP cases are excluded there, and the WPF samples' end-to-end tests skip. It then collects coverage, checks
the adapter floors, and unpacks the `Ason.Bridge.Grpc` package to prove the shipped contract still contains
`protos/ason_bridge.proto`. A second job (`windows-samples`) builds the WPF samples and re-runs
`tests/Ason.Bridge.Tests` plus the library suite on Windows, which is what makes those end-to-end tests actually
execute; it also runs the FlaUI UI tests with `continue-on-error`, because UI Automation needs an interactive
desktop session a hosted runner provides only inconsistently. The net10.0 leg stays out until that SDK is GA.
