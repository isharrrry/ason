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

// 清单里带着应用发布出来的代理层与签名列表，因此构建客户端库只需一次调用 —— 这一侧不复制任何 operator
var library = manifest.ToOperatorsLibrary();

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

## 不用 Agent：外部程序直接驱动应用

桥没有任何一处是专为 Agent 设计的。同一批端点就是普通程序的 RPC 面：驱动真实应用的测试夹具、灌入或检查状态的 CI 步骤、
运维/自动化脚本、另一个服务，或者拿 `curl` 的开发者。它们像任何客户端一样调用应用 —— 通常直接用**单函数接口**，
因为已经知道要调什么的程序不需要模型来写调用：

| 调用方 | 通道 | 典型调用 |
|---|---|---|
| .NET 程序 | `GrpcAsonBridgeClient` | `InvokeFunctionAsync(call)` —— 一次往返，JSON 进、JSON 出 |
| .NET 程序 | `McpAsonBridgeClient` | 同样的调用走 MCP（调用方本就会说 MCP 时） |
| 任意 HTTP 客户端 | `Ason.Bridge.OpenApi` | `POST /ason/functions/{operator}/{method}`，或 `POST /ason/script` |
| 测试夹具 / CI 任务 | 上述任一 | 一串调用，断言其返回的 JSON |

```bash
# 这条工作流里没有任何模型
curl -s http://localhost:5223/ason/manifest                       # 发现能调用什么
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/Rename \
     -H "Content-Type: application/json" -d '{"arguments":[1,"Ada"]}'
curl -s -X POST http://localhost:5223/ason/script \
     -H "Content-Type: application/json" -d '{"code":"return employeesOperator.GetEmployees().Count;"}'

# 同样的调用用 .NET 客户端（示例 console 客户端正是这个场景）
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
```

这类调用方能拿到：清单（用于发现）、单函数接口、需要组合多次调用时的整段脚本接口，以及 gRPC 上的流式日志。
它拿不到的是模型与编排 —— 调用序列由它自己决定，没有人替它把结果讲成人话，也没有超出它自身的重试。对自动化而言这正是重点：
**确定性的调用本身就是价值**。

两个值得知道的推论：

- **函数调用完全绕过脚本通道。** `ForbiddenScriptKeywords` 守的是"生成代码"；`invokeFunction` 不是代码。此时保护应用的是
  **暴露出去的 API**（打了 `[Ason*]` 的 operator，以及宿主收窄时的 `AdditionalMethodFilter`）、开启的能力开关，以及下面的
  网络控制。另外，函数级调用根本不涉及脚本宿主：没有执行器进程、没有编译、一次往返 —— 而一段脚本可能按它调用的 operator
  数量产生 N 次往返。
- **桥端点就是管理面。** 任何能到达它的东西都能调用清单上的每个 operator，因此请绑 loopback；要暴露到更远处，就在前面加 key
  或真正的认证。HTTP 适配器有可选的共享密钥；gRPC 与 MCP 期望由网关或 ASP.NET 授权挡在前面。

Agent 场景与这个场景**共用一切**：同一宿主、同一运行时、同一批适配器与端点。区别只在于由谁来组织调用 —— 模型，还是程序自己。

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

### 要求调用方通过鉴权

鉴权按适配器逐项开启、默认关闭 —— 因为开发形态就是 loopback 上的无凭据桥：

| 适配器 | 如何开启 | 未授权调用方看到 |
|---|---|---|
| gRPC | `AddAsonGrpcBridge(runtime, "<policy>")` —— policy 就是普通的 ASP.NET Core 授权策略 | `StatusCode.Unauthenticated`（**不是** `Unimplemented` —— 后者在所有适配器上都表示"能力被关闭"） |
| MCP（HTTP） | `AddAsonMcpBridge(endpoint, requireAuthorization: true)` | `401` |
| HTTP / OpenAPI | `AsonOpenApiBridgeOptions.ApiKey`（配合 `ApiKeyHeader`） | `401` |

调用方用请求头表明身份，机制就这一条 —— gRPC metadata *就是* HTTP/2 头：

```csharp
await using var grpc = GrpcAsonBridgeClient.Connect("http://localhost:5222", headers);   // 例如 Authorization: Bearer …
await using var mcp = await McpAsonBridgeClient.ConnectAsync("http://localhost:5223/mcp", headers);
```

对于自己无法设置请求头的 Agent，中继可以代为携带：

```bash
Ason.Bridge.McpHost --url http://localhost:5222 --key <value>                     # X-Ason-Bridge-Key: <value>
Ason.Bridge.McpHost --url http://localhost:5222 --header "Authorization=Bearer <token>"
```

鉴权失败始终是**凭据问题**而不是应用结果：gRPC 报 `Unauthenticated`（类型化客户端会把它原样抛出，而不是折叠成一次失败的桥调用），
MCP 与 HTTP 报 `401`；应用级失败仍保留各自的错误码。

## 示例与运行方式

哪种情形由哪个示例（或哪几个示例组合）演示 —— 从"不分离"到各种"分离"形态。带 🔑 的行需要模型密钥：
`MY_OPEN_AI_KEY`（可选 `MY_OPEN_AI_BASE_URL`、`MY_OPEN_AI_MODEL`）；`ConsoleMcpSample` 还需要 `MY_CONTEXT7_API_KEY`。

| 情形 | 应用侧 | 调用方 / Agent 侧 | 能看到什么 |
|---|---|---|---|
| **不分离** —— 桌面应用内嵌 Agent | `samples/WptDemoApp` | 同一进程 | 聊天面板通过进程内 operator 驱动 WPF 界面 |
| 不分离 —— Blazor Server | `samples/BlazorAdvancedApp`（http://localhost:5240） | 同一进程 | 聊天面板驱动服务端组件 |
| 不分离 —— 带 extractor 的 console | `samples/ConsoleExtractorSample` | 同一进程 | 文本抽取 + operator 调用都在一个 console 里 |
| 不分离 —— API 来自 MCP 服务的 console | `samples/ConsoleMcpSample` | 同一进程 | 脚本像调用 operator 一样调用 Context7 的 MCP 工具 |
| 不分离 —— 全新应用 | `samples/templates` | 同一进程 | `dotnet new ason.wpf` / `ason.winforms` / `ason.console` / `ason.blaz.srv` / `ason.maui` 直接生成可跑的聊天应用 |
| 不分离，但**脚本宿主**在远端 | `samples/WptDemoApp` + `samples/RemoteRunnerService`（http://localhost:5236） | 同一进程 | 只有执行被搬走；应用、Agent、operator 与数据仍在一起 |
| **分离** —— 自带编排的 .NET Agent | `samples/WpfAppOnlyDemo` 或 `samples/ConsoleGrpcBridgeHost` | `samples/WpfAgentDemo`，或任何用 `TransportFactory` 的 `AsonClient` | Agent 拉取应用的 operator API 并驱动它；Agent 侧一个 operator 都没有 |
| 分离 —— 同样的 Agent 侧但**没有界面**（任意系统） | 任一应用侧 | `samples/ConsoleAgentSample`（`--list` 不需要密钥；`--send "…"` 需要） | console agent 打印它从清单构建出的 API，然后驱动应用 |
| 分离，且**脚本宿主是应用自己的子进程** | `samples/ConsoleGrpcBridgeHost --execution external` | 上面任一调用方 | 清单报告 `execution=external-process`；脚本文本在子进程执行，而 operator 调用仍在应用内解析 |
| 分离 —— 用 HTTP MCP 的 Agent | 任一应用侧 | 任何 MCP 客户端（Claude Desktop、IDE）指向 `/mcp` | 应用表现为五个 MCP 工具 |
| 分离 —— 只能启动 stdio MCP 的 Agent | 任一应用侧 | `src/Ason.Bridge.McpHost`（`--transport grpc` 或 `--transport mcp`） | 同样的工具，走 Agent 的 stdin/stdout |
| 分离 —— **完全没有 Agent** | 任一应用侧 | `samples/ConsoleGrpcBridgeDemo`、`curl`、Swagger UI/Postman | 程序或 shell 驱动应用：一次函数调用，或一段脚本 |

```bash
# --- 不分离：应用与 Agent 同进程（🔑 需要模型密钥） ---
dotnet run --project samples/WptDemoApp/WpfSampleApp.csproj -f net9.0-windows
dotnet run --project samples/BlazorAdvancedApp                       # http://localhost:5240
dotnet run --project samples/ConsoleExtractorSample
dotnet run --project samples/ConsoleMcpSample                        # 另需 MY_CONTEXT7_API_KEY
dotnet new install samples/templates && dotnet new ason.console   # 已安装过旧版本时加 --force 刷新

# 不分离但脚本宿主在远端：先起运行器，再让应用使用它
dotnet run --project samples/RemoteRunnerService/RunnerServiceSample.csproj    # http://localhost:5236
#   在 samples/WptDemoApp/ViewModels/ChatViewModel.cs 取消注释：
#     UseRemoteRunner = true, RemoteRunnerBaseUrl = "http://localhost:5236"

# --- 分离：应用侧（无模型、无需密钥） ---
dotnet run --project samples/WpfAppOnlyDemo -- --bridge-only --port 5222
#   gRPC   http://localhost:5222
#   MCP    http://localhost:5223/mcp
#   HTTP   http://localhost:5223/ason/openapi.json
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222    # 同样的端点，operator 来自 LibDemo

# --- 分离：Agent 侧 ---
dotnet run --project samples/WpfAgentDemo                            # 聊天窗口；仅聊天需要密钥
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222           # gRPC 自检，无需密钥
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # MCP 自检，无需密钥
Ason.Bridge.McpHost --url http://localhost:5222                      # 供 Claude Desktop/Code 使用的 stdio MCP 中继

# 同样的 Agent 侧，改成 console 程序：任意系统可跑，且查看它不需要密钥
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5223/mcp --transport mcp --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --send "add 20 and 22"   # 需要密钥

# 应用也可以在子进程中求值脚本，把 operator 留在自己进程里
dotnet run --project samples/ConsoleGrpcBridgeHost -- --port 5222 --execution external

# --- 分离：不要 Agent，只要一个程序 ---
#   对着上面的 console 宿主（它的 operator 来自 LibDemo）
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func LibDemoOperator.GetProducts
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);" --stream
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add \
     -H "Content-Type: application/json" -d '{"arguments":[40,2]}'

#   对着上面的 WPF 应用（它自己的 operator）
dotnet run --project samples/ConsoleGrpcBridgeDemo -- --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics
curl -s -X POST http://localhost:5223/ason/functions/EmployeesOperator/GetDiagnostics \
     -H "Content-Type: application/json" -d '{}'
```

分离形态的自动化证明就是桥测试套件：它无窗口启动真实的 WPF 应用、用 gRPC **和** MCP 各驱动一遍、以 `--verify` 启动真实
的 Agent 示例、把中继作为真实 stdio MCP 服务启动，并断言返回值 —— **全程不需要任何模型密钥**：

```bash
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj --configuration Release
```

不分离的演示保留它自己的 UI 级证明，需要交互式 Windows 桌面：

```bash
dotnet test tests/WpfDemoApp.UiTests/WpfDemoApp.UiTests.csproj --configuration Release   # 需要时可设 WPF_DEMO_TFM
```

## 已知限制

- `manifest.proxies` 是调用方读取那一刻的快照。若此后视图开关过，使用实例变量的脚本可能过期；**单函数接口每次
  都会解析存活 handle**，是操作实例 operator 的稳妥路径。
- 调用一个视图尚未加载的 operator 会触发运行时既有的 reload 语义，可能打开或导航视图。不想有副作用时请使用已附着实例。
- 引入 `Ason.Bridge.Grpc` 的文件中，该命名空间会遮蔽 `Grpc` 根命名空间：请写 `using Grpc.Net.Client;` 后使用
  `GrpcChannel.ForAddress(...)`，或完整限定类型名。
- gRPC 契约不包含 MCP 透传（`invokeMcpTool`）；中继会将其报告为 `not-supported`。
