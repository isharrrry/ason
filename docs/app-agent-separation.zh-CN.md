# 应用 / Agent 分离：ASON 桥

[English](app-agent-separation.md) | **中文** | [Español](app-agent-separation.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

ASON 最初假设一个进程包办一切：应用程序、它的 operator、模型调用与脚本编排都在同一处。**桥（bridge）**
把这件事一分为二，同时让两侧的运行时保持完全一致。

- **应用侧**是唯一持有 `[Ason*]` 标记 operator、业务数据与 UI 状态的地方，**完全不含模型客户端**。
- **Agent 侧**持有模型与编排，**不持有任何 operator**：它先问应用“你能调用什么”，再请应用去执行。
- **桥**是两者之间的接缝：它描述应用的 operator API、转发执行请求，并且是唯一知道“传输”存在的组件。四种部署形态
  （.NET Agent、HTTP MCP、stdio 中继、通用 HTTP 客户端）的流向图见[架构](architecture.zh-CN.md)中的 *应用 / Agent 分离（桥）* 一节。

```
        ┌──────────────────────── 应用进程 ──────────────────────────────┐
        │  [AsonOperator]/[AsonMethod] operator + UI/数据                │
        │  Ason.Bridge 运行时 ──► Ason.Bridge.Grpc / Ason.Bridge.Mcp      │
        └───────────────────────────────┬───────────────────────────────┘
                                        │  清单 · 执行脚本 · 调用单个函数
        ┌───────────────────────────────┴───────────────────────────────┐
        │  Agent 进程：模型、提示词、编排                                  │
        │  （可选）Ason.Bridge.Grpc 客户端 + GrpcAsonBridgeTransport       │
        └───────────────────────────────────────────────────────────────┘
```

## 清单（manifest）就是契约

`AsonBridgeRuntime.GetManifestAsync()` 返回一份载荷，所有适配器原样发布它：

| 字段 | 含义 |
|---|---|
| `protocolVersion` | 契约版本；不一致时应当立即失败 |
| `appName` | 应答的是哪个应用 |
| `execution` | 脚本在哪里求值：`in-process`、`external-process`、`docker`、`remote-runner` |
| `capabilities` | 启用了哪些接口（见下） |
| `api` | operator API：operator、静态模块、方法、参数、`[AsonModel]` 类型 |
| `markdown` | 同一份列表的文档形态，便于人读或交给模型 |
| `proxies` / `signatures` | 脚本所需的代理层与签名列表 |
| `instances` | 当前存活 operator 实例，以及每个实例的 handle |

API 列表来自 `OperatorApiCatalog`，它与脚本提示词使用同一套反射规则，因此**列表不可能与脚本能调用到的东西漂移**。

## 两个彼此独立的执行接口

| 能力 | 接口 | 适用场景 |
|---|---|---|
| `listApis` | 清单 | Agent 需要知道能调用什么 |
| `executeScript` | `ExecuteScriptAsync(script)` | Agent 要组合多次调用、分支或循环 |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | 只调用一次、要精准 —— 不必生成脚本文本 |
| `invokeMcpTool` | 透传到应用自身消费的 MCP 客户端 | 应用本身是别的 MCP 服务的客户端 |
| `logStream` | `StreamExecutionAsync(script)` | 调用方想看着应用干活 |

它们各自独立、且可组合：同时开启两个执行接口不会互相影响。由 `AsonBridgeCapabilities` 决定“存在什么”，
每个适配器如实发布：

- gRPC：被关闭的能力返回 `StatusCode.Unimplemented`。
- MCP：被关闭的能力对应的工具**根本不注册**。

静态 operator 模块无需 handle（`BridgeStaticOperator.Add(2, 3)`）；实例 operator 在其类型只有一个存活实例时按
实例目录自动解析，否则需要清单里报告的 `handle`。失败以稳定错误码返回 —— `operator-not-found`、
`handle-required`、`handle-ambiguous`、`handle-not-found`、`method-not-found`、`script-rejected`、
`not-supported`、`execution-failed`，客户端据此分支，而不必解析文本。

## 传输

| 适配器 | 包 | 内容 |
|---|---|---|
| gRPC | `Ason.Bridge.Grpc` | `GrpcAsonBridgeService`、`GrpcAsonBridgeClient`、`GrpcAsonBridgeTransport`、`GrpcAsonBridgeEndpoint` |
| MCP（Streamable HTTP） | `Ason.Bridge.Mcp` | `AsonBridgeMcpTools`、`McpAsonBridgeClient`、`McpAsonBridgeTransport`、`McpAsonBridgeEndpoint` |
| MCP（stdio 中继） | `Ason.Bridge.McpHost` | 把应用的 gRPC 桥重新发布为 stdio MCP 的进程 |
| HTTP + OpenAPI（Swagger） | `Ason.Bridge.OpenApi` | HTTP 端点 + 由清单生成的文档，供通用 HTTP 客户端与 Swagger UI 使用 |
| 其它 | 你自己的项目 | 面向 `IAsonBridgeEndpoint` 做同样的映射 |

成熟传输库被刻意挡在 `Ason` 之外：`Ason` 不引用 gRPC、不含 MCP 服务端、也不引用 ASP.NET。
新增一种传输 = 新增一个引用 `Ason.Bridge` 的项目，把 `IAsonBridgeEndpoint` 映射过去 —— 一个 gRPC 服务、
一组 MCP 工具，或一份由清单生成的 OpenAPI 文档。**应用侧无需任何改动。**

`Ason.Bridge`、`Ason.Bridge.Grpc`、`Ason.Bridge.Mcp` 均以 `net9.0` 为目标（核心同时面向 `net6.0`，与 `Ason` 保持一致）。

### gRPC

```csharp
// 应用侧
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

除非走 TLS，gRPC 端点必须放在一个单独的 HTTP/2-only（h2c）监听上。

### MCP

```csharp
builder.Services.AddAsonMcpBridge(runtime);   // Streamable HTTP
...
app.MapAsonMcpBridge("/mcp");
```

对于只能“启动一个进程”的 Agent（Claude Desktop、Claude Code 等），把它指向中继宿主：后者连上应用，再用同样的
工具集通过 stdio 提供服务：

```bash
# 应用发布的是 gRPC
Ason.Bridge.McpHost --url http://localhost:5222

# ……中继也可以改用 MCP 与应用对话，此时完全不需要 gRPC
Ason.Bridge.McpHost --url http://localhost:5223/mcp --transport mcp
```

中继的存在源于 stdio MCP 的**契约本身**：客户端负责启动服务进程，并通过该子进程的 stdin/stdout 通信。一个正在运行的
桌面应用无法充当这个子进程，因此必须有人持有这条管道 —— 而它又需要一条通往应用的通道，这就是**只有这一种部署形态**
会出现两跳的原因。它不是设计的前提：会说 HTTP MCP 的 Agent 直连应用；而生命周期本身就是“被 Agent 拉起”的应用，可以
用 `AddAsonMcpStdioBridge`（中继内部用的正是这个调用）自己提供 stdio MCP。

## 把编排交给 Agent

若 Agent 想保留 ASON 自身的编排，它可以**零 operator**地使用应用的 operator：从清单构建自己的 operator 库，
并把运行器指向应用。

```csharp
await using var client = GrpcAsonBridgeClient.Connect("http://localhost:5222");
var manifest = await client.GetManifestAsync();

// 清单里带着脚本所针对的代理层
var library = new OperatorsLibrary(
    Task.FromResult((manifest.Proxies, manifest.Signatures, (IOperatorMethodCache)new NoOperatorCache())),
    false, Array.Empty<IMcpClient>(), Array.Empty<Assembly>());

var agent = new AsonClient(chatService, new RootOperator(new object()), library, new AsonClientOptions {
    // 隔离由应用侧决定；这一侧只负责怎么连上它
    ExecutionMode = ExecutionMode.ExternalProcess,
    TransportFactory = () => new GrpcAsonBridgeTransport(client)
});
```

`AsonClientOptions.TransportFactory`（底层就是 `RunnerClient.UseTransport`）正是让这件事成立的接缝：传输把
runner 协议（`exec` 下行、结果上行）送到应用，而代理生成、重试、校验与结果处理**原地不动**。
`McpAsonBridgeTransport` 用 MCP 做同样的事。

应用在自己的进程内解析 operator 调用，因此传输**永远不会**看到 `invoke` 消息。若它还是出现了（意味着部署被指向了
并非应用本身的执行器），会以错误应答，而不是让调用方永久等待。

## 执行位置

`AsonBridgeOptions.Execution` 与传输是正交的两件事：

| 取值 | 脚本在哪求值 | operator 调用在哪解析 |
|---|---|---|
| `InProcess` | 应用进程内 | 应用 |
| `ExternalProcess` | 应用的 `Ason.ExternalExecutor` 子进程 | 应用（经 stdio） |
| `Docker` | 由应用启动的容器 | 应用 |
| `RemoteRunner` | 远端 `Ason.RemoteBridge` 宿主 | 应用 |

无论哪种，**operator、数据与凭据都留在应用侧**。参见[执行模式](execution-modes.zh-CN.md)与
[部署拓扑](architecture.zh-CN.md#部署拓扑)。

operator 调用会经构造运行时那一刻捕获的 `SynchronizationContext` 封送，因此 WPF 应用中它们跑在 dispatcher 线程 ——
请在 UI 线程上构造运行时。只有在没有 UI 关联性的宿主里才设置 `CaptureSynchronizationContext = false`。

## 安全

桥会代替调用方执行代码、调用应用函数，因此应当把它当作特权端点：

- 默认只绑 loopback；要暴露到网络，就在前面加认证（Kestrel 端点上的授权策略，或 MCP/HTTP 网关）。
- `ForbiddenScriptKeywords` 请设置为你给本地 `AsonClient` 的同一份列表；留 `null` 只会拒绝空脚本。
  它是关键字过滤而非沙箱 —— 面对不可信输入请选择 `ExternalProcess` 或 `Docker`。
- 只开启部署需要的能力；清单会如实告诉每个客户端哪些是开着的。
- `invokeMcpTool` 默认关闭：它转发到**应用**所消费的 MCP 服务。
- 每个请求都会被校验、按能力放行并返回错误码，但这一切都**不能替代网络层访问控制**。

## 示例

| 示例 | 角色 |
|---|---|
| `samples/WpfAppOnlyDemo` | **应用侧的真实桌面应用**：WPF 窗口 + `[Ason*]` operator（绑定窗口的视图 operator、静态模块、仅标记模块、`LibDemo` 类库），托管 gRPC / MCP / HTTP-OpenAPI，无模型无聊天。`--bridge-only --port 5222` 可无窗口运行 |
| `samples/WpfAgentDemo` | **Agent 侧的真实桌面应用**：聊天窗口、端点与传输（gRPC/MCP）选择、从应用拉取的 API 列表、调用日志；**一个 `[AsonOperator]` 都没有**。`--verify <endpoint> [--mcp]` 提供无界面自检 |
| `samples/ConsoleGrpcBridgeHost` | 应用侧的最小形态：来自 `LibDemo` 的 `[Ason*]` operator，托管 gRPC + MCP + OpenAPI，无 Agent |
| `samples/ConsoleGrpcBridgeDemo` | 外部请求侧：清单、存活实例、单函数调用、脚本、流式日志 |
| `src/Ason.Bridge.McpHost` | 面向只会 MCP 的 Agent 的 stdio 中继 |

```bash
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222

dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoStaticOperator.Add --args "[2,3]"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);"
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "..." --stream
```

## 已知限制

- `manifest.proxies` 是调用方读取那一刻的快照。若此后视图开关过，使用实例变量的脚本可能过期；**单函数接口每次
  都会解析存活 handle**，是操作实例 operator 的稳妥路径。
- 调用一个视图尚未加载的 operator 会触发运行时既有的 reload 语义，可能打开或导航视图。不想有副作用时请使用已附着实例。
- 引入 `Ason.Bridge.Grpc` 的文件中，该命名空间会遮蔽 `Grpc` 根命名空间：请写 `using Grpc.Net.Client;` 后使用
  `GrpcChannel.ForAddress(...)`，或完整限定类型名。
- gRPC 契约不包含 MCP 透传（`invokeMcpTool`）；中继会将其报告为 `not-supported`。
