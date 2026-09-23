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
| `src/Ason.Bridge.McpHost` | relay that republishes an application's gRPC bridge as stdio MCP |
| `samples/ConsoleGrpcBridgeHost` | application side of a split deployment: `[Ason*]` operators plus gRPC and MCP services |
| `samples/ConsoleGrpcBridgeDemo` | external request side: manifest, single-function calls, scripts, streamed logs |
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
| `tests/Ason.Bridge.Tests` | net9.0 | the bridge core, the gRPC and MCP adapters (each host is started in-process and driven over the wire), the runner transport seam and the relay endpoint |
| `tests/WpfDemoApp.UiTests` | net9.0-windows | FlaUI UI automation — needs an interactive Windows desktop session |

The bridge packages (`Ason.Bridge`, `Ason.Bridge.Grpc`, `Ason.Bridge.Mcp`, `Ason.Bridge.McpHost`) and their
test suite are cross-platform and run on Linux; see [application / agent separation](app-agent-separation.md).

Running the hermetic subset without Docker:

```bash
dotnet test tests/Ason.Tests/Ason.Tests.csproj --configuration Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
```

Environment variables that change what the UI tests do:

| Variable | Meaning |
|---|---|
| `WPF_DEMO_TFM` | which sample build to drive — `net9.0-windows` (default), `net6.0-windows` or `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release` (default) or `Debug` |
| `MY_OPEN_AI_KEY`, `MY_OPEN_AI_BASE_URL`, `MY_OPEN_AI_MODEL` | enable the live end-to-end test; without a key it is reported as skipped |

Configuring a provider is described in [AI providers](ai-providers.md).

## Continuous integration

`.github/workflows/ci.yml` runs on every push and on pull requests. It builds the cross-platform projects and runs the hermetic test suites on Ubuntu; the Docker-mode and MCP cases are excluded there. The WPF sample, the UI tests and the net10.0 leg are Windows-only and are not covered by that job yet.
