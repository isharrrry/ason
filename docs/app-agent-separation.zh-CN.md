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
| `protocolVersion` | 契约版本；不一致时应当立即失败。`1.1` 是纯追加：`1.0` 客户端仍可用 |
| `appName` | 应答的是哪个应用 |
| `execution` | 脚本在哪里求值：`in-process`、`external-process`、`docker`、`remote-runner` |
| `capabilities` | 启用了哪些接口（见下） |
| `api` | operator API：operator、静态模块、方法、参数、`[AsonModel]` 类型 |
| `markdown` | 同一份列表的文档形态，便于人读或交给模型 |
| `proxies` / `signatures` | 脚本所需的代理层与签名列表 |
| `instances` | 当前存活 operator 实例，以及每个实例的 handle |
| `instancesRevision` | `instances` 的摘要；与你留存的清单对比即可判断那份快照是否过期 |

API 列表来自 `OperatorApiCatalog`，它与脚本提示词使用同一套反射规则，因此**列表不可能与脚本能调用到的东西漂移**。

## 两个彼此独立的执行接口

| 能力 | 接口 | 适用场景 |
|---|---|---|
| `listApis` | 清单 | Agent 需要知道能调用什么 |
| `executeScript` | `ExecuteScriptAsync(script)` | Agent 要组合多次调用、分支或循环 |
| `invokeFunction` | `InvokeFunctionAsync(operator, method, args)` | 只调用一次、要精准 —— 不必生成脚本文本 |
| `invokeMcpTool` | 透传到应用自身消费的 MCP 客户端：gRPC `InvokeMcpTool`、MCP 工具 `ason_invoke_mcp_tool` | 应用本身是别的 MCP 服务的客户端 |
| `logStream` | gRPC `StreamExecution`、MCP `ason_stream_script`、HTTP `POST /ason/script/stream` | 调用方想看着应用干活 |

它们各自独立、且可组合：同时开启两个执行接口不会互相影响。由 `AsonBridgeCapabilities` 决定“存在什么”，
每个适配器如实发布：

- gRPC：被关闭的能力返回 `StatusCode.Unimplemented`。
- MCP：被关闭的能力对应的工具**根本不注册**。

`invokeMcpTool` 还有第二种拒绝，二者含义完全不同：

- **能力关闭** —— 调用压根不存在（`Unimplemented`，或没有该工具），与其他被关闭能力给出的信号一致。
- **能力开启但未注册 MCP 服务** —— 调用存在，但返回 `not-supported`：应用打开了透传，却从未用
  `RunnerClient.RegisterMcpClient` 注册任何客户端。桥查的就是 `IAsonExecutor.McpServers`，因此自带执行器的宿主
  报告的是它真实拥有的服务。

静态 operator 模块无需 handle（`BridgeStaticOperator.Add(2, 3)`）；实例 operator 在其类型只有一个存活实例时按
实例目录自动解析，否则需要清单里报告的 `handle`。失败以稳定错误码返回 —— `operator-not-found`、
`handle-required`、`handle-ambiguous`、`handle-not-found`、`method-not-found`、`script-rejected`、
`not-supported`、`execution-failed`，客户端据此分支，而不必解析文本。

## 实例鲜度与清单过期

`manifest.proxies` 末尾为每个存活实例声明了变量
（`EmployeesOperator employeesOperator = new("EmployeesOperator");`），脚本因此可以直接写
`employeesOperator.GetEmployees()`。但这些声明只在读取清单的那一刻成立：此后新开或关闭的视图，要么不在声明里
（变量不存在），要么声明的是已经消失的 handle。

桥给调用方三种由弱到强的正确路径：

1. **比对 `instancesRevision`。** 它是存活实例列表的摘要，留过旧清单的调用方据此判断快照已过期，并重新读取
   （`ason_get_manifest`）。
2. **只发脚本体。** `includeInstanceDeclarations: true`（gRPC `include_instance_declarations`、MCP
   `includeInstanceDeclarations`、HTTP `includeInstanceDeclarations`）让应用提供代理层**与**当天的实例声明，
   调用方只发想执行的语句。声明必须位于生成层内部，因此只有应用侧能重建它；这个开关正是“我只发 body，层由你负责”
   的显式写法。
3. **把你的快照层告诉传输层。** `GrpcAsonBridgeTransport` / `McpAsonBridgeTransport` 接受
   `Proxies = manifest.Proxies`：设置后，传输层会把这段精确文本从拼好的脚本里剥掉，改为每次调用都请应用给出
   当前层 —— 不增加任何往返。Agent 侧 runner 协议用的就是这个开关，因为它启动时就会预置读到的层。

单函数接口不需要以上任何一条：它每次调用都解析存活 handle。

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

`Protos/ason_bridge.proto` 就是契约本身：`GetManifest`、`ListInstances`、`ExecuteScript`、`InvokeFunction`、
`InvokeMcpTool` 与流式 `StreamExecution`。`ExecuteScriptRequest` 带 `include_proxy_preamble` 与
`include_instance_declarations`，`ManifestReply` 带 `instances_revision`，`InvokeMcpToolRequest` 以 JSON 对象
携带工具参数。协议 `1.1` 的新增全部是追加式的：`1.0` 客户端对着 `1.1` 的桥仍然可用。

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

`samples/mcp/` 里有两份可直接复制的客户端配置 —— 一份给"自己启动中继"的客户端，一份给直接与应用说 Streamable HTTP
的客户端 —— 外加一份列出工具名与鉴权参数的 README。`samples/python/ason_mcp_caller` 是一个只用标准库的 MCP 客户端，
用来在不惊动桌面客户端的前提下先验一遍配置。

中继的存在源于 stdio MCP 的**契约本身**：客户端负责启动服务进程，并通过该子进程的 stdin/stdout 通信。一个正在运行的
桌面应用无法充当这个子进程，因此必须有人持有这条管道 —— 而它又需要一条通往应用的通道，这就是**只有这一种部署形态**
会出现两跳的原因。它不是设计的前提：会说 HTTP MCP 的 Agent 直连应用；而生命周期本身就是“被 Agent 拉起”的应用，可以
用 `AddAsonMcpStdioBridge`（中继内部用的正是这个调用）自己提供 stdio MCP。

## 看着脚本跑起来

执行日志是**运行时**的能力（`Capabilities.LogStream`），而“你的传输能不能送到”由各适配器的原生表面如实体现 ——
清单告诉客户端运行时支持日志，适配器自己的表面告诉它怎么拿到：

| 适配器 | 表面 | 拿到什么 |
|---|---|---|
| gRPC | `StreamExecution` | 脚本运行期间逐条 `log` 事件，最后恰好一个 `result`/`error` 事件 |
| HTTP | `POST {base}/script/stream` | 同上，以 Server-Sent Events 形式（`event: log` …… `event: result`） |
| MCP | `ason_stream_script` 工具 | 日志**与**结果在同一次应答里 —— MCP 的工具调用无法向调用方推送 |

关闭 `LogStream` 会同时移除这三者（rpc 返回 `Unimplemented`、该路由不再提供、MCP 工具不再注册），且不影响非流式接口。
处于中继位置的传输（尤其是 stdio MCP 宿主）**转而收集应用自身的流**，而不是订阅一个它永远不会收到的事件 ——
因此中继给出的日志就是应用的日志。

```bash
# 直接用 curl 收 Server-Sent Events
curl -N -X POST http://localhost:5223/ason/script/stream \
  -H 'Content-Type: application/json' \
  -d '{"code":"return LibDemoOperator.Add(40, 2);"}'
# event: log
# data: {"level":"Information","message":"...","source":"RunnerClient"}
#
# event: result
# data: {"success":true,"result":42}
```

`AsonBridgeRuntime` 通过 `AsonBridgeRuntime.Log`（适配器侧是 `IAsonBridgeEndpoint.Log`）抛出这些事件，宿主也可以借此
镜像到自己的日志系统。

## 不用 .NET 也能调用这座桥

gRPC 契约本身就是接口：Python、Go、Java、Rust 或 `grpcurl` 调用方只需要那个 `.proto` 文件。拿到“应用此刻正在
服务的同一份契约”有两条路径：

```xml
<!-- 1. 从包里取：该文件随 Ason.Bridge.Grpc 一起发布 -->
<PackageReference Include="Ason.Bridge.Grpc" Version="0.9.0" GeneratePathProperty="true" />
<!-- 契约位于 $(PkgAson_Bridge_Grpc)\protos\ason_bridge.proto -->
```

```bash
# 2. 从仓库取，或从解包后的 nupkg 取
unzip -o Ason.Bridge.Grpc.0.9.0.nupkg 'protos/*' -d ./ason-contract
# src/Ason.Bridge.Grpc/Protos/ason_bridge.proto
```

服务全名是 `ason.bridge.v1.AsonBridge`，方法为 `GetManifest`、`ListInstances`、`ExecuteScript`、`InvokeFunction`、
`InvokeMcpTool`，以及服务端流式的 `StreamExecution`（先逐条 `log` 事件，最后恰好一个 `result` 或 `error`）。

```bash
# Python：先生成 stub 再调用；samples/python/ason_bridge_client.py 已经把这些封好了
python -m pip install -r samples/python/requirements.txt      # PyPI 慢时可加 -i <国内镜像>/simple
python -m grpc_tools.protoc -I./ason-contract --python_out=. --grpc_python_out=. ason_bridge.proto

# Go / Java / protoc 支持的任何语言
protoc -I./ason-contract --go_out=. --go-grpc_out=. ason_bridge.proto
protoc -I./ason-contract --java_out=. ason_bridge.proto

# grpcurl：带着磁盘上的契约……
grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto \
  -d '{"code":"return 1;"}' localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript

# ……或在应用开启了反射时，连契约都不用带（--reflection，默认关闭）
grpcurl -plaintext -d '{"operator":"LibDemoOperator","method":"Add","arguments_json":"[40,2]"}' \
  localhost:5222 ason.bridge.v1.AsonBridge/InvokeFunction
```

`samples/python/` 就是第一个示例的可运行版本：首次运行时自动编译契约，并暴露 `manifest`、`instances`、
`call`、`script [--stream]`、`mcp <server> <tool>` 等子命令。

反射是 **opt-in 且默认关闭**的（`AddAsonGrpcBridge(runtime, enableReflection: true)`，或样例的 `--reflection`）：
一旦发布，等于把可调用面再次广播给任何能连上该端口的人 —— 在 loopback 上没问题，放到别处就应当与
[安全](#安全)一节的鉴权并用。无论走哪条路，**能调什么仍然以 manifest 为准**：反射只省去你手工同步 `.proto` 的
功夫，能力开关、关键字过滤与 operator API 一样都不会绕过。

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
    // Proxies：本客户端从清单里读到的代理层。设置后应用会在每次执行前重建实例声明，
    // 因此“读清单之后才打开的视图”依然可用。
    TransportFactory = () => new GrpcAsonBridgeTransport(client) { Proxies = manifest.Proxies }
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

# 全部路由、按四组整理、可逐条发送：samples/bridge-examples.http
# （VS Code REST Client、Rider，或把其中一段贴进 curl）

# 同样的调用用 .NET 客户端（示例 console 客户端正是这个场景）
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetEmployees
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return employeesOperator.GetDiagnostics().OnUiThread;" --stream
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

## 调用方接入：需要知道什么、需要配置什么

桥在"被发现"这件事上是刻意保持笨的：它只回答问题，不会主动广播自己。因此"要接入需要做什么"取决于形态：

| 形态 | 调用方要准备 | 应用侧要配置 | 能否零配置接入 |
|---|---|---|---|
| HTTP + OpenAPI | 一个 URL（应用设了 key 就再带上 key）—— `curl`、Swagger UI、Postman 或任何 HTTP 客户端 | `AddAsonOpenApiBridge(runtime)` + `MapAsonOpenApiBridge()`、端口、可选 `ApiKey` | ✅ 一个 URL 就够 |
| MCP over HTTP | 一个以 `/mcp` 结尾的 URL，以及一个 MCP 客户端 | `AddAsonMcpBridge(runtime)` + `MapAsonMcpBridge()`，可选 `requireAuthorization` | ✅ 一个 URL 就够 |
| gRPC（.NET） | URL，外加一个客户端包装：`GrpcAsonBridgeClient.Connect(url)`（或用生成的 stub） | `AddAsonGrpcBridge(runtime)` + `MapAsonGrpcBridge()`、HTTP/2 监听，可选策略 | ⚠️ 需要客户端，但无需配置文件 |
| gRPC（非 .NET） | URL **与契约本身**：从包里取 `ason_bridge.proto` 再生成 stub —— 见[不用 .NET 也能调用这座桥](#不用-net-也能调用这座桥) | 同上，可选开启反射（`enableReflection`），这样连本地 `.proto` 都不需要 | ⚠️ 需要契约 |
| MCP over stdio | **一条启动命令**（`Ason.Bridge.McpHost --url …`），因为 stdio MCP 的契约就是"客户端启动服务进程" | 应用必须在跑，并且能通过 gRPC 或 MCP 访问到 | ❌ 调用方必须写启动配置 |

有三条前提值得明说 —— 每一条被忽略时，都会以令人困惑的失败形式出现：

1. **没有注册中心、mDNS 或自动发现。** URL 属于"带外知识"：命令行参数、配置文件、环境变量。manifest 是"你已经知道去哪问"
   **之后**的发现机制 —— 它告诉你应用暴露了什么，而不是它在哪。
2. **handle 是运行期状态。** 视图开关会带来实例的增删，因此想指定某个实例的调用方必须先取 `instances`（否则会撞上
   `handle-required` / `handle-ambiguous` / `handle-not-found`）。判断自己那张快照是否过期，靠的就是 manifest 的
   `instancesRevision`。
3. **`proxies` 是快照。** 使用实例变量的脚本只在读取清单那一刻成立；[实例鲜度与清单过期](#实例鲜度与清单过期)给出三种
   保持正确的办法。

有两个开关始终留在应用侧，调用方只能读、不能改：

- **能力开关**决定"存在什么"：关掉 `executeScript` 后，gRPC 返回 `Unimplemented`、MCP 不列出该工具、HTTP 路由 404。
  manifest 会提前说明，调用方据此适配，而不必靠试。
- **鉴权**决定"谁可以调"：`AddAsonGrpcBridge(runtime, "<policy>")` 与 `AddAsonMcpBridge(endpoint, requireAuthorization: true)`
  负责开启；未通过鉴权的调用方拿到的是 `Unauthenticated` / `401`，**绝不会**是 `Unimplemented` —— 否则就与"能力未启用"
  无法区分。见[要求调用方通过鉴权](#要求调用方通过鉴权)。

这里涉及的边界画在[架构文档](architecture.zh-CN.md#应用--agent-分离桥)（调用方边界是 D），而与以上全部正交的"执行位置"轴见
[执行模式](execution-modes.zh-CN.md)。

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
- `invokeMcpTool` 默认关闭：它转发到**应用**所消费的 MCP 服务，用的是应用自己持有的凭据。一旦开启，桥接受的
  每个调用方都能碰到这些工具，因此桥不只是 loopback 时请配合下面的鉴权一起使用。
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

**角色图例**：`…AppSample` / `…Host` / `…AppOnlyDemo` 属于**应用侧** —— 它们持有 operator 并发布端点；
`…CallerSample` / `…AgentSample` / `…AgentDemo` 属于**调用方侧** —— 它们连接这些端点，自己没有任何 operator。
同一个程序可以同时站在两侧：stdio 中继对应用来说是调用方，同时对拉起它的 Agent 来说又是服务端（应用）。

| 情形 | 应用侧 | 调用方 / Agent 侧 | 能看到什么 |
|---|---|---|---|
| **不分离** —— 桌面应用内嵌 Agent | `samples/WptDemoApp` | 同一进程 | 聊天面板通过进程内 operator 驱动 WPF 界面 |
| 不分离 —— Blazor Server | `samples/BlazorAdvancedApp`（http://localhost:5240） | 同一进程 | 聊天面板驱动服务端组件 |
| 不分离 —— 带 extractor 的 console | `samples/ConsoleExtractorSample` | 同一进程 | 文本抽取 + operator 调用都在一个 console 里 |
| 不分离 —— API 来自 MCP 服务的 console | `samples/ConsoleMcpSample` | 同一进程 | 脚本像调用 operator 一样调用 Context7 的 MCP 工具 |
| 不分离 —— 全新应用 | `samples/templates` | 同一进程 | `dotnet new ason.wpf` / `ason.winforms` / `ason.console` / `ason.blaz.srv` / `ason.maui` 直接生成可跑的聊天应用 |
| 不分离，但**脚本宿主**在远端 | `samples/WptDemoApp` + `samples/RemoteRunnerService`（http://localhost:5236） | 同一进程 | 只有执行被搬走；应用、Agent、operator 与数据仍在一起 |
| **分离** —— 自带编排的 .NET Agent | `samples/WpfAppOnlyDemo` 或 `samples/ConsoleBridgeAppSample` | `samples/WpfAgentDemo`，或任何用 `TransportFactory` 的 `AsonClient` | Agent 拉取应用的 operator API 并驱动它；Agent 侧一个 operator 都没有 |
| 分离 —— 同样的 Agent 侧但**没有界面**（任意系统） | 任一应用侧 | `samples/ConsoleAgentSample`（`--list` 不需要密钥；`--send "…"` 需要） | console agent 打印它从清单构建出的 API，然后驱动应用 |
| 分离，且**脚本宿主是应用自己的子进程** | `samples/ConsoleBridgeAppSample --execution external` | 上面任一调用方 | 清单报告 `execution=external-process`；脚本文本在子进程执行，而 operator 调用仍在应用内解析 |
| 分离 —— 用 HTTP MCP 的 Agent | 任一应用侧 | 任何 MCP 客户端（Claude Desktop、IDE）指向 `/mcp` | 应用表现为五个 MCP 工具 |
| 分离 —— 为 **stdio MCP** 配置的客户端 | 任一应用侧 | `samples/mcp/claude_desktop_config.json`（stdio 中继）或 `http_mcp_config.json`（HTTP） | 真实桌面客户端能看到应用的工具；没有客户端时可用 `samples/python/ason_mcp_caller` 先验一遍配置 |
| 分离 —— 由**模型**自行挑选 MCP 工具（作为测试） | 任一应用侧 | `samples/python/ason_mcp_agent`（🔑 `MY_OPEN_AI_KEY`） | 模型挑工具、operator 在应用内执行；若结果没出现，`--expect` 会让这次运行失败 |
| 分离 —— 只能启动 stdio MCP 的 Agent | 任一应用侧 | `src/Ason.Bridge.McpHost`（`--transport grpc` 或 `--transport mcp`） | 同样的工具，走 Agent 的 stdin/stdout |
| 分离 —— **完全没有 Agent** | 任一应用侧 | `samples/ConsoleBridgeCallerSample`、`curl`、Swagger UI/Postman | 程序或 shell 驱动应用：一次函数调用，或一段脚本 |
| 分离 —— **换一种语言**写的调用方 | 任一应用侧 | `samples/python`（编译随包发布的 `.proto`） | Python 从清单列出 API、调用函数、执行脚本并读取日志 |

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
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222    # 同样的端点，operator 来自 LibDemo

# --- 分离：Agent 侧 ---
dotnet run --project samples/WpfAgentDemo                            # 聊天窗口；仅聊天需要密钥
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5222           # gRPC 自检，无需密钥
dotnet run --project samples/WpfAgentDemo -- --verify http://localhost:5223/mcp --mcp # MCP 自检，无需密钥
Ason.Bridge.McpHost --url http://localhost:5222                      # 供 Claude Desktop/Code 使用的 stdio MCP 中继
# 可直接复制的 MCP 客户端配置（stdio 与 HTTP）与用于先验的最小调用端：
#   samples/mcp/claude_desktop_config.json · samples/mcp/http_mcp_config.json · samples/mcp/README.md
python samples/python/ason_mcp_caller/main.py --transport http --list

# 同样的 Agent 侧，改成 console 程序：任意系统可跑，且查看它不需要密钥
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5223/mcp --transport mcp --list
dotnet run --project samples/ConsoleAgentSample -- --url http://localhost:5222 --send "add 20 and 22"   # 需要密钥

# 应用也可以在子进程中求值脚本，把 operator 留在自己进程里
dotnet run --project samples/ConsoleBridgeAppSample -- --port 5222 --execution external

# --- 分离：不要 Agent，只要一个程序 ---
#   对着上面的 console 宿主（它的 operator 来自 LibDemo）
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func LibDemoOperator.GetProducts
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --script "return LibDemoStaticOperator.Add(40, 2);" --stream
curl -s http://localhost:5223/ason/openapi.json
curl -s -X POST http://localhost:5223/ason/functions/LibDemoStaticOperator/Add \
     -H "Content-Type: application/json" -d '{"arguments":[40,2]}'

#   对着上面的 WPF 应用（它自己的 operator）
dotnet run --project samples/ConsoleBridgeCallerSample -- --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics
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

- `manifest.proxies` 是调用方读取那一刻的快照，其中的实例声明会随时间过期。请比对 `instancesRevision`、用
  `includeInstanceDeclarations` 只发脚本体，或在 runner 传输层设置 `Proxies`（见“实例鲜度与清单过期”）。
  **单函数接口每次都会解析存活 handle**，是操作实例 operator 的稳妥路径。
- 调用一个视图尚未加载的 operator 会触发运行时既有的 reload 语义，可能打开或导航视图。不想有副作用时请使用已附着实例。
- 引入 `Ason.Bridge.Grpc` 的文件中，该命名空间会遮蔽 `Grpc` 根命名空间：请写 `using Grpc.Net.Client;` 后使用
  `GrpcChannel.ForAddress(...)`，或完整限定类型名。
- 透传只能指定服务名与工具名；桥不会镜像应用所消费 MCP 服务的工具列表，调用方需要从应用（或 Agent 自身配置）得知，
  而不是从清单得知。
