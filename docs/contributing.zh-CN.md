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
| `tests/WpfDemoApp.UiTests` | net9.0-windows | FlaUI UI 自动化 —— 需要交互式的 Windows 桌面会话 |

在不使用 Docker 的情况下运行与外部环境隔离的子集：

```bash
dotnet test tests/Ason.Tests/Ason.Tests.csproj --configuration Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
```

会改变 UI 测试行为的环境变量：

| 变量 | 含义 |
|---|---|
| `WPF_DEMO_TFM` | 驱动哪个示例构建 —— `net9.0-windows`（默认）、`net6.0-windows` 或 `net10.0-windows` |
| `WPF_DEMO_CONFIG` | `Release`（默认）或 `Debug` |
| `MY_OPEN_AI_KEY`、`MY_OPEN_AI_BASE_URL`、`MY_OPEN_AI_MODEL` | 启用实时端到端测试；没有密钥时该测试会被报告为已跳过 |

配置提供方的说明见 [AI providers](ai-providers.zh-CN.md)。

## 持续集成

`.github/workflows/ci.yml` 在每次推送和拉取请求时运行。它会在 Ubuntu 上构建跨平台项目并运行与外部环境隔离的测试套件；Docker 模式和 MCP 用例在该任务中被排除。WPF 示例、UI 测试和 net10.0 部分仅在 Windows 上运行，目前尚未被该任务覆盖。
