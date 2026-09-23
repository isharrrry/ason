# 贡献指南

[English](contributing.md) | **中文** | [Español](contributing.es.md)

> 属于 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## 构建

```bash
dotnet build Ason.sln --configuration Release
```

整个解决方案可以在 Windows 上构建。WPF 示例（`samples/WptDemoApp`）和 FlaUI UI 测试面向 `*-windows`，因此整个解决方案在 Linux 或 macOS 上的构建预计不会成功 —— 请改为只构建你需要的项目：

```bash
dotnet build src/Ason/Ason.csproj --configuration Release
```

## 仓库结构

| 路径 | 内容 |
|---|---|
| `src/Ason.Abstractions` | 仅含标记特性（`netstandard2.0`） |
| `src/Ason` | 运行时：客户端、编排、代理生成（`net6.0`、`net9.0`） |
| `src/Ason.Runner.Core` | 脚本执行宿主 |
| `src/Ason.ExternalExecutor` | 进程外运行器可执行文件 |
| `src/Ason.RemoteBridge` | ASP.NET Core 远程运行器 |
| `src/Ason.Bridge` | 与传输无关的桥：清单、能力、执行器、operator 目录（`net6.0`、`net9.0`） |
| `src/Ason.Bridge.Grpc` | gRPC 适配器：服务、类型化客户端、runner 传输、转发端点 |
| `src/Ason.Bridge.Mcp` | MCP 适配器：工具面、类型化客户端、runner 传输（Streamable HTTP） |
| `src/Ason.Bridge.OpenApi` | HTTP + OpenAPI（Swagger）适配器：端点 + 由清单生成的文档 |
| `src/Ason.Bridge.McpHost` | 把应用的 gRPC 桥重新发布为 stdio MCP 的中继 |
| `samples/WpfAppOnlyDemo` | WPF 应用侧：`[Ason*]` operator + gRPC / MCP / HTTP-OpenAPI 服务，无 Agent |
| `samples/WpfAgentDemo` | WPF Agent 侧：聊天 + 端点/传输选择，一个 `[AsonOperator]` 都没有 |
| `samples/ConsoleBridgeAppSample` | 分离部署的应用侧：`[Ason*]` operator + gRPC 与 MCP 服务 |
| `samples/ConsoleAgentSample` | Agent 侧的 console 形态：从应用的清单构建自己的 API，并通过 gRPC 或 MCP 驱动它（跨平台、无界面） |
| `samples/ConsoleBridgeCallerSample` | 外部请求侧：清单、单函数调用、脚本、流式日志 |
| `samples/WptDemoApp` | WPF 演示（`net10.0`、`net9.0`、`net6.0-windows`） |
| `samples/LibDemo` | 仅使用标记的类库（`net6.0`、`netstandard2.0`） |
| `samples/mcp` | 可直接复制的 MCP 客户端配置（stdio 中继与 HTTP）以及工具清单 |
| `samples/python` | 非 .NET 调用方：gRPC 客户端、纯标准库 MCP 客户端、OpenAI 驱动的 MCP 工具调用测试 |
| `samples/bridge-examples.http` | 桥的全部 HTTP 路由，按组整理，可逐条发送 |
| `samples/templates` | `dotnet new` 模板 |
| `scripts` | CI 执行的仓库级检查（覆盖率下限、失败注解） |
| `tests/*` | 测试套件，见下文 |
| `.agents/plans` | 实现计划；**故意**留在仓库里，便于把决策与代码一起评审 |
| `CHANGELOG.md` | 已发布变更，每个版本一节 |

## 测试套件

| 套件 | 框架 | 说明 |
|---|---|---|
| `tests/LibDemo.SmokeTests` | net6.0 / net9.0 / net10.0 | 标记发现、类型转发以及真实的进程内 operator 调用 |
| `tests/Ason.Tests` | net9.0 | `E2E_AllExecutionModes(executionMode: Docker, …)` 用例需要 Docker 守护进程；`McpClientTests` 需要实时的 MCP server |
| `tests/Ason.Runner.Tests` | net9.0 | 脚本运行器 |
| `tests/Ason.RemoteRunner.Tests` | net9.0 | 除非 `ASON_REMOTE_RUNNER_URL` 指向正在运行的远程运行器，否则该集成测试会被跳过 |
| `tests/Ason.Bridge.Tests` | net9.0 | 桥核心、gRPC/MCP/OpenAPI 适配器（各自在进程内起宿主并走真实链路）、runner 传输缝、中继端点，以及 WPF 示例的端到端测试（在 Linux 或未构建 Windows 示例时会跳过） |
| `tests/WpfDemoApp.UiTests` | net9.0-windows | FlaUI UI 自动化 —— 需要交互式的 Windows 桌面会话 |

在不使用 Docker 的情况下运行与外部环境隔离的子集：

```bash
dotnet test tests/Ason.Tests/Ason.Tests.csproj --configuration Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
```

桥的覆盖率用 `coverlet.runsettings` 采集 —— 它把 protoc 生成的 gRPC 代码排除在外，使百分比描述的是手写适配器：

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings
```

四个桥工程都有**下限**，CI 任务会逐适配器强制检查（行 ≥ 87%、分支 ≥ 70%；`0.9.0` 时实测为 89–98% 与 75–82%）：

```bash
./scripts/check-bridge-coverage.ps1 -CoverageFile 'artifacts/coverage/*/coverage.cobertura.xml'
```

样例程序集**故意不计入**该下限：每个样例都是独立进程，而覆盖率是在测试宿主内采集的，因此样例自身的程序集无法用这种方式度量。
样例由进程级端到端测试覆盖（`ConsoleSamplesEndToEndTests`、`WpfApplicationEndToEndTests`、`RemoteRunnerBridgeEndToEndTests`）——
它们先构建样例，再驱动真实进程。

会改变 UI 测试行为的环境变量：

| 变量 | 含义 |
|---|---|
| `WPF_DEMO_TFM` | 驱动哪个示例构建 —— `net9.0-windows`（默认）、`net6.0-windows` 或 `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release`（默认）或 `Debug` |
| `MY_OPEN_AI_KEY`、`MY_OPEN_AI_BASE_URL`、`MY_OPEN_AI_MODEL` | 启用实时端到端测试；没有密钥时该测试会被报告为已跳过 |
| `ASON_BRIDGE_KEY`、`ASON_BRIDGE_REMOTE_URL`、`ASON_BRIDGE_EXECUTION` | 中继宿主与示例应用对应 `--key`、`--remote-url`、`--execution` 的环境变量写法 |

配置提供方的说明见 [AI providers](ai-providers.zh-CN.md)。

## 持续集成

`.github/workflows/ci.yml` 在每次推送和拉取请求时运行。它的 Linux 任务构建跨平台项目（含 console 与 remote-runner 样例）
并运行与外部环境隔离的测试套件 —— Docker 模式与 MCP 用例在该任务中被排除，WPF 示例的端到端测试会被跳过。随后它采集覆盖率、
检查适配器下限，并解包 `Ason.Bridge.Grpc` 包以证明随包发布的契约里仍有 `protos/ason_bridge.proto`。第二个任务
（`windows-samples`）构建 WPF 示例，并在 Windows 上重新运行 `tests/Ason.Bridge.Tests` 与库测试套件，这才让那些端到端测试真正执行；
它同时以 `continue-on-error` 运行 FlaUI UI 测试 —— 因为 UI Automation 需要交互式桌面会话，而托管运行器只能不稳定地提供。
net10.0 分支在该 SDK 正式发布前不纳入。

每个测试步骤都会写出 TRX 文件，最后一个步骤（`scripts/emit-test-failures.ps1`，以 `if: failure()` 守护）把它们转成
check-run 注解。这是有意的：失败运行的作业日志只有管理员权限才能下载，而携带测试名与断言内容的注解可以匿名读取 ——
包括需要解释这次失败的人与工具。
