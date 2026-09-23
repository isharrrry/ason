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
| `samples/ConsoleGrpcBridgeHost` | 分离部署的应用侧：`[Ason*]` operator + gRPC 与 MCP 服务 |
| `samples/ConsoleGrpcBridgeDemo` | 外部请求侧：清单、单函数调用、脚本、流式日志 |
| `samples/WptDemoApp` | WPF 演示（`net10.0`、`net9.0`、`net6.0-windows`） |
| `samples/LibDemo` | 仅使用标记的类库（`net6.0`、`netstandard2.0`） |
| `samples/templates` | `dotnet new` 模板 |
| `tests/*` | 测试套件，见下文 |

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

会改变 UI 测试行为的环境变量：

| 变量 | 含义 |
|---|---|
| `WPF_DEMO_TFM` | 驱动哪个示例构建 —— `net9.0-windows`（默认）、`net6.0-windows` 或 `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release`（默认）或 `Debug` |
| `MY_OPEN_AI_KEY`、`MY_OPEN_AI_BASE_URL`、`MY_OPEN_AI_MODEL` | 启用实时端到端测试；没有密钥时该测试会被报告为已跳过 |

配置提供方的说明见 [AI providers](ai-providers.zh-CN.md)。

## 持续集成

`.github/workflows/ci.yml` 在每次推送和拉取请求时运行。它的 Linux 任务构建跨平台项目并运行与外部环境隔离的测试套件 —— Docker 模式与 MCP 用例在该任务中被排除，WPF 示例的端到端测试会被跳过。第二个任务（`windows-samples`）构建两个 WPF 示例，并在 Windows 上重新运行 `tests/Ason.Bridge.Tests`，这才让那些端到端测试真正执行；它同时会构建原有的 WPF 演示，以防改动库把必须保留的示例编译坏。原有的 WPF 演示的 UI 测试、FlaUI 用例与 net10.0 分支仍未被任何任务覆盖。
