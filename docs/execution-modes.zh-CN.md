# 执行模式与远程执行

[English](execution-modes.md) | **中文** | [Español](execution-modes.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## 执行模式 / 环境

- **In-process（进程内）** —— 脚本在你的应用程序所在的同一进程中运行。  
  适合开发和测试，但由于隔离能力有限，不建议用于生产环境。  
  ASON 包含静态分析，用于阻止不安全的操作（反射、文件 I/O、网络等）。

- **External process（外部进程）** —— 脚本在单独的进程中运行，提供额外一层保护。  
  添加 `Ason.ExternalExecutor` NuGet 包即可启用此模式。

- **Docker** —— 脚本在完全隔离的容器内执行，以获得最高级别的安全性。  
  需要在本地安装 Docker。  
  运行应用前请先拉取所需的容器：

  > docker pull ghcr.io/alexgoon/ason:0.10.0

- **Remote server（远程服务器）** —— 脚本在远程服务器上执行，可能位于外部进程或 Docker 容器中。  
  配置细节见下一节。
  
![ASON 执行环境](../images/execution-environments.jpg)


## 选择模式

| 场景 | 建议模式 |
|---|---|
| 本地开发与测试，以及必须访问 UI 状态的脚本 | **In-process** |
| 不应在自身进程中运行生成代码的桌面应用 | **External process** |
| 不可信输入，或需要最高级别隔离时 | **Docker** |
| 移动客户端，或没有 Docker 的机器 | **Remote server** |

## 远程执行

你可以在 **远程服务器** 上运行 ASON 脚本，这对于移动客户端，或者用户机器上无法使用 Docker 的情况特别有用。

要启用远程执行：

1. 创建一个标准的 **ASP.NET Core Web API** 项目，并安装 `Ason.RemoteBridge` NuGet 包。  
2. 在 `Program.cs` 中注册脚本运行器：

```csharp
builder.Services.AddAsonScriptRunner();  
app.MapAson("/scriptRunnerHub", requireAuthorization: false);
```

3. 在客户端应用程序中，于 `AsonClientOptions` 中启用远程运行器：

```csharp
AsonClientOptions options = new() {  
    ExecutionMode = ExecutionMode.ExternalProcess,  
    RemoteRunnerBaseUrl = "http://localhost:5222",  
    UseRemoteRunner = true,  
};
```

演示此配置的示例项目包含在 **MAUI Project Template** 中。

## 模式与部署的两种独立轴线

`ExecutionMode` 和 `UseRemoteRunner` 是正交的：前者选择脚本如何被隔离，后者选择脚本宿主在哪里运行。传输方式则由这一组合决定：

在分离部署下，它们旁边还有第三个轴：**调用方到应用这一段由哪种传输承载**（gRPC、MCP、HTTP）。它由应用侧选定，而每种选择
下调用方需要准备什么见[调用方接入](app-agent-separation.zh-CN.md#调用方接入需要知道什么需要配置什么)。

```csharp
// RunnerTransportManager
RequiresTransport => UseRemoteRunner || Mode != ExecutionMode.InProcess;
// CreateTransport(): UseRemoteRunner -> SignalRTransport(RemoteUrl)
//                    otherwise      -> StdIoProcessTransport(Mode, DockerImage, RunnerExecutablePath)
```

| # | `ExecutionMode` | `UseRemoteRunner` | 脚本在何处被求值 | 链路 |
|---|---|---|---|---|
| 1 | `InProcess` | `false` | 你自己的进程 —— 完全不使用传输 | 仅客户端 |
| 2 | `ExternalProcess` | `false` | 客户端机器上的 `Ason.ExternalExecutor` 子进程 | 客户端 → 子进程 |
| 3 | `Docker` | `false` | 客户端机器上以 `docker run --rm -i <image>` 启动的容器 | 客户端 → 子进程 → 容器 |
| 4 | `InProcess` | `true` | 远程运行器的**服务进程** | 客户端 → 服务器 |
| 5 | `ExternalProcess` / `Docker` | `true` | **服务器**上的子进程或容器 | 客户端 → 服务器 → 服务器侧执行器 |

在每一行中，operator 方法仍然在客户端进程中运行。当 `UseRemoteRunner = true` 时，所选模式会被发送到服务器（`StartRunner((int)mode, dockerImage)`），由服务器决定是在自己的进程内求值脚本，还是启动一个执行器。这些边界在[架构](architecture.zh-CN.md#部署拓扑)中有所描述。

随着[桥](app-agent-separation.zh-CN.md)的出现，出现了第三条轴线：**由谁提供 runner 传输**。通过
`AsonClientOptions.TransportFactory`（底层即 `RunnerClient.UseTransport`），宿主可以把自有传输交给客户端，从而让脚本由**另一个进程**求值
——例如一个通过 gRPC、MCP 或 HTTP/OpenAPI 发布 operator 的应用 —— 而客户端保留代理生成、重试、校验与结果处理。三条轴线可自由组合：
执行模式描述的是**求值一侧**的隔离方式，传输只说明如何到达那一侧。而**函数级调用根本不属于这些轴线**：调用单个 operator 方法
不涉及脚本宿主（没有执行器进程、没有编译、一次往返），因此测试夹具或自动化脚本可以放心用它，而不必关心"脚本会在哪里运行"。

## 哪种配置适合哪种应用形态

| 应用形态 | 推荐 | 原因 |
|---|---|---|
| WPF 或 WinForms 桌面应用，且输入可信 | `InProcess`（示例默认值）或 `ExternalProcess` | 数据与 UI 都留在本地，operator 调用要么是进程内调用，要么是机器内 IPC —— 延迟尽可能低 |
| 不应在自身进程中运行生成代码的桌面应用 | `ExternalProcess`，机器上有 Docker 时则用 `Docker` | 无需任何额外基础设施即可获得隔离 |
| 用户无法安装 Docker，但生成代码仍不应在他们的机器上运行的桌面应用 | 在你的服务器上远程执行，并在那里使用 `Docker`（或 `InProcess`） | 脚本宿主离开客户端，而 operator、数据和凭据仍留在本地 —— 参见[凭据与数据边界](architecture.zh-CN.md#凭据与数据边界) |
| 数据已经位于服务器上的 Blazor Server / ASP.NET Core 应用 | 通过 `AddAson` 在该应用**内部**运行 `AsonClient` | 客户端主机*就是*服务器；operator 本就在处理服务器数据，因此远程执行并不会带来额外价值 |
| Blazor WebAssembly | 远程执行；如果 operator 接口允许，也可在浏览器中使用 `InProcess` | 浏览器无法启动进程或容器 |
| MAUI / 移动端或其他瘦客户端 | 远程（`Ason.RemoteBridge` + `UseRemoteRunner`） | 设备无法承载执行器 —— 这正是 MAUI 模板所演示的内容 |
| 一个服务为众多客户端运行脚本 | 专用的远程运行器主机 | 统一在一处为执行器定版本、实施策略并收集日志 |
| 应用与独立的 Agent 进程（包括只会 MCP 的 Agent） | 用 `Ason.Bridge` 发布应用的 operator，让 Agent 通过 gRPC、MCP 或 HTTP/OpenAPI 驱动它 | operator、数据与 UI 留在应用侧，模型与编排留在 Agent 侧 —— 参见[应用 / Agent 分离](app-agent-separation.zh-CN.md) |

上表每一行的可运行示例（不分离的各种形态与分离部署）都附有确切命令，见[示例与运行方式](app-agent-separation.zh-CN.md#示例与运行方式)。

## 如何在两者之间选择以及每种选择的代价

<!-- i18n: localize-labels - 标签本地化，保留结构（箭头、缩进） -->

```
你需要与生成的代码隔离吗？
  不需要 -> In-process                     最快；无需额外进程；关键字过滤是唯一屏障
  需要   -> 这个客户端能承载运行器吗（子进程；若要容器还需要 Docker）？
             能   -> 本地 External process / Docker    延迟最低，数据不离开本机
             不能 -> 远程，在服务器上使用 Docker / 外部进程 / 进程内
                     （移动端、浏览器、锁定或瘦客户端）
```

延迟是远程执行的主要代价：每次 operator 调用都是一次网络往返（当服务器启动执行器时还要再加一次本地跳转），并且每次调用都会被封送回客户端的 UI 线程。因此，如果一个脚本对 N 个条目逐个调用 operator，其代价约为 N 次往返，而在本地求值的脚本则没有任何往返。
