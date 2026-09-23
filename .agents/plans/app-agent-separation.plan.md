# Plan: ASON Bridge —— 应用 / Agent 分离（Wave 1 已交付 · Wave 2 待执行）

**Created**: 2026-09-23
**Mode**: conversational `/plan`（eccplan → `references/commands/plan.md`）
**Branch**: `feat/agent-app-separation-bridge`
**Baseline**: `fb00b0f`（撰写本文件时的 HEAD，工作树干净）
**Complexity**: Large（Wave 1 已落地 5 个新项目 + 6 个样例 + 91 个桥测试；Wave 2 约 14.0 人日）
**Status**: Wave 1 **已完成并提交**；Wave 2 **待确认** —— 规划阶段不写实现代码，等人工检测本文件后开工
**Evidence**: `docs/testing/app-agent-separation.tdd.md`（Wave 1 的逐任务 RED/GREEN 证据、覆盖率与缺口清单）
**Provenance**: Wave 1 的原始计划是内联给出的（未落盘）。本文件是**唯一计划产物**：§5 的 Wave 1 部分按当时确认的范围 + 真实提交记录重建，Wave 2 部分为本轮待执行计划。根目录曾短暂存在的 `plan.md` 已删除（见 §10.1）。

---

## 0. 已拍板的决策（Decision Log）

| 决策 | 选择 | 理由 |
|---|---|---|
| 保留既有演示 | **保留 `samples/WptDemoApp` 不动** | 它是内嵌 Agent 的形态，与新增的分离形态互不替代 |
| 分离形态的两个新演示 | 应用侧（无 Agent）+ Agent 侧（零 operator） | 需求原话：应用"仅含 `[Ason*]` 标签和开启 MCP/gRPC 服务" |
| 传输库归属 | **gRPC / MCP / OpenAPI 一律独立 csproj**，绝不进 `Ason` | 需求硬约束；`Ason` 至今不引用 gRPC、MCP 服务端、ASP.NET |
| 桥的形态 | 与传输无关的 `Ason.Bridge` 核心 + 每传输一个适配器 | 让"再注入一种服务"= 新增一个 csproj，不改运行时与应用 |
| 接口粒度 | 整段脚本与单函数**各自独立开关、可组合** | 需求原话"可选可组合，不会互相冲突"；落地为 `AsonBridgeCapabilities` |
| 契约单一来源 | 清单（manifest）由 `OperatorApiCatalog` + `ProxySerializer` 派生 | 列表与脚本提示词同源，不可能漂移 |
| 调用方边界上 operator 是否跨越 | **不跨越**：脚本文本下行，operator 在应用进程内解析 | 这保证了数据与凭据留在应用侧 |
| 应用侧执行器边界（A′/E） | **`invoke` 会回环到应用**：executor 回调应用执行真实方法 | 两种边界方向不同，必须分别画出，不能一句"operator calls never cross"带过（→ T0） |
| stdio 中继是否"绕" | **不是**：只有 stdio-only Agent 需要两跳，且由 stdio MCP 契约决定 | 中继的上游通道还能二选一（`--transport grpc\|mcp`），gRPC 非前置条件 |
| 鉴权 | **opt-in**：默认 loopback 无鉴权，适配器各自提供开关 | 样例与 CI 的默认路径必须保持可直接跑（→ T1） |
| 覆盖率口径 | `coverlet.runsettings` 排除 protoc 生成源 | 否则 gRPC 适配器被误报 53.8%（生成桩行数占多数） |
| TDD | 每项先 RED 再 GREEN，同分支留检查点提交 | 全程遵守；Wave 1 的 RED/GREEN 证据在证据报告中 |
| 测试项目 | **不**拆分 `tests/Ason.Bridge.IntegrationTests`，集成与 E2E 合并进 `tests/Ason.Bridge.Tests` | 减少 CI 接线与构建时间 |
| 目标版本号 | **`0.9.0`**（追加式 API 至少 minor）；T7 负责把仓库内版本号一并更新 | 你已拍板（§9-1） |
| `logStream` 能力口径（T5） | **(a)** 清单字段保留"运行时支持日志流"，"你的传输给不给"由各适配器原生表面体现（gRPC rpc / MCP 工具 / HTTP 路由 / OpenAPI path） | 你已拍板（§9-9）；不新增"按适配器改写清单"的机制 |
| 样例改名（T11） | **保留 `Sample` 后缀**：`ConsoleBridgeAppSampleSample` / `ConsoleBridgeCallerSampleSample` | 你已拍板（§9-5/§9-10）；与 `ConsoleMcpSample`/`ConsoleAgentSample` 一致 |
| gRPC 反射（T10） | **提供 opt-in 开关，默认关闭**；文档写明"开了等于公开方法清单，应与鉴权并用" | 你已拍板（§9-4） |
| 非 .NET 调用示例 | **T10 必须交付 Python 的 gRPC 调用示例**（除 proto 投递与反射开关外） | 你已追加要求（§9-3） |
| 最小 MCP 消费端（T14） | **Python**；并且用 **OpenAI 官方库 + 配置 + 命令行参数传入对话指令**，实现"模型自动选择并调用 MCP 工具"的**自动化测试** | 你已拍板并追加要求（§9-6）：验的是"真实第三方 Agent 能否自动用起来"，而非只连得上 |

---

## 1. 需求复述

1. 保留 `samples/WptDemoApp`；新增**不含 Agent 的 WPF 应用演示**与 **WPF Agent 演示**，两者组合成"应用 / Agent 分离"形态。
2. 应用侧只含 `[Ason*]` 标记的业务能力，并开启 MCP / gRPC 服务；Agent 侧通过 MCP / gRPC 查询应用 API 列表并下发执行。
3. 新增一个**桥**：既要对外提供 API（让 Agent 能查询到可调用面），又要把 MCP / gRPC 的执行请求转给执行器（内部执行器或 `Ason.ExternalExecutor`）并回传结果。
4. 桥可注入 MCP / gRPC / Swagger(OpenAPI) / 未来其它服务；**但 gRPC 这类成熟库不得直接塞进 `Ason`，须开独立 csproj**。
5. 同时提供**整段代码**接口与**单函数级**接口，二者可组合、不冲突；例如 gRPC 既能执行原始 script，也能精准调用单个函数。
6. 追加要求（会话中提出）：补一个 gRPC 客户端 exe demo 展示精准执行函数能力；TDD 测试先行；先做 Phase 0–3，随后完成其余全部。
7. 追加要求（本轮）：把两波计划合并成 `.agents/plans/` 下 eccplan 格式的计划文件（本文件）。
8. 追加要求（本轮）：把"**非 .NET 调用 gRPC**"缺口（`Ason.Bridge.Grpc` 的 NuGet 包内没有 `ason_bridge.proto`，Python/Go/Java/Node 等调用方无法从包生成客户端）纳入 Wave 2（→ T10）；**本轮只修订本计划文件，不执行任何实现**。
9. 追加要求（本轮，随后指示"纳入"）：把两项可用性/文档缺口也纳入 Wave 2 ——
   ① **示例命名可读性**：`ConsoleGrpcBridgeHost`＝应用侧 / `ConsoleGrpcBridgeDemo`＝调用方，名字易反读（→ T11）；
   ② **"调用方需要知道/配置什么"对照表**：分离形态下调用方各需准备什么、能否零配置接入、以及"无自动发现"等语义（→ T12）。

---

## 2. 证据（本仓库 + 本机实测）

### 2.1 Wave 1 结束时的实测基线

| 项 | 值 | 出处 |
|---|---|---|
| 桥测试套件 | **91/91**（5 console E2E + 2 WPF 应用 E2E + 2 WPF Agent E2E + 2 中继 E2E） | `dotnet test tests/Ason.Bridge.Tests -c Release` |
| `Ason.Tests`（hermetic 过滤集） | **105/105** | `--filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"` |
| `LibDemo.SmokeTests`（net9.0） | **11/11** | `--framework net9.0` |
| 覆盖率 | `Ason.Bridge` 88.9%/79.9% · `.Grpc` 93.4%/60.5% · `.Mcp` 87.1%/51.4% · `.OpenApi` 98.3%/70.0%（行/分支） | `--settings coverlet.runsettings` |

### 2.2 Wave 1 期间用实测排除的关键风险

| 疑问 | 结论 | 证据 |
|---|---|---|
| WPF `WinExe` 能否同时自托管 ASP.NET Core（`UseWPF` + `Microsoft.AspNetCore.App`） | **可以**，且 `--bridge-only` 需 `ShutdownMode.OnExplicitShutdown` 才能无窗口存活 | `samples/WpfAppOnlyDemo` 构建+运行；`WpfApplicationEndToEndTests` 真起进程 |
| WPF + Kestrel 下 operator 是否仍在 UI 线程 | **是**，`threadId=2` 即 dispatcher 线程 | `--func EmployeesOperator.GetDiagnostics` → `{"onUiThread":true,"threadId":2}` |
| Kestrel h2c + `Grpc.AspNetCore` 2.71 + `Grpc.Tools` 代码生成在本机是否可用 | **可用**（先用一次性 spike 验证，再动生产代码） | spike：`SPIKE_LISTENING=…` / `SPIKE_REPLY=HELLO` |
| MCP 服务端 API 形态（`McpServerTool.Create` / `WithTools` / `WithHttpTransport` / `MapMcp` / `StdioClientTransport`） | 经反射 dump 确认签名后才落代码 | nuget 包 XML/反射检查（`ModelContextProtocol` 0.4.0-preview.3 系列） |
| gRPC 适配器覆盖率为何只有 53.8% | **protoc 生成源占了分母**；排除后为 93.4% | `coverlet.runsettings` 前后对比 |

### 2.3 Wave 1 期间**由测试发现**的真实缺陷（不是评审发现的）

| 缺陷 | 症状 | 影响面 | 修法 | 现状 |
|---|---|---|---|---|
| MCP 工具的可选参数被当成必填 | `System.ArgumentException: … missing a value for the required parameter 'handle'`；stdio 通道会把 JSON `null` 归一化成"缺失" | **中继下每次不传 `handle` 的调用都失败**，而 73/73 测试全绿 | 给委托参数加默认值（`handle = null` 等）+ 类型化客户端不再发送 null | 已修 + 有回归测试（`McpRelayHostTests`） |
| 中继无 stderr 诊断 | MCP 客户端只看到 `An error occurred invoking 'x'` | 现场无法定位 | 中继接 `Console` 日志到 **stderr**（stdout 只走协议） | 已修 |
| WPF E2E 端口对竞争 | `Failed to bind … address already in use` | 测试偶发失败 | `TestPorts` 预留连续端口对 + 相关测试类串行集合 | 已修（FO4 时统一到 `TestPorts`） |

### 2.4 Wave 1 遗留缺口（→ Wave 2 输入）

| 编号 | 缺口 | 纳入 |
|---|---|---|
| A | 鉴权可达性：只有 HTTP 适配器有 key；gRPC/MCP 无授权钩子；类型化 MCP 客户端与中继**发不出** key | ✅ T1 |
| B | `logStream` 只有 gRPC 真正实现，MCP/HTTP 没有，但 manifest 处处声明 | ✅ T2 |
| C | `invokeMcpTool` 不在 gRPC 契约、MCP 也无对应工具 | ✅ T3 |
| **D** | `manifest.proxies` 是快照：脚本用实例变量时会因视图开关而失效 | ✅ **T4（本轮指定纳入）** |
| E | 适配器分支覆盖 51–80%；样例程序集覆盖率未采集；UI 自动化未接入 | ✅ T5 |
| F | 发布卫生：版本、release notes、包清单 | ✅ T6 |
| G | CI：无覆盖率门禁；Windows 作业未跑 `Ason.Tests` | ✅ T7 |
| **H** | **非 .NET 调用 gRPC：`Ason.Bridge.Grpc` 的 nupkg 内只有 `lib/net9.0/Ason.Bridge.Grpc.dll` + `icon.png` + nuspec，没有 `ason_bridge.proto`；非 .NET 调用方无法从包生成客户端（只能回仓库取），且无反射可用** | ✅ **T10（本轮指定纳入，未执行）** |
| **I** | **示例命名可读性：`ConsoleGrpcBridgeHost` 其实是应用侧（服务端）、`ConsoleGrpcBridgeDemo` 其实是调用方（客户端），名字易反读；指南形态表也缺一行"角色图例"** | ✅ **T11（本轮指定纳入，未执行）** |
| **J** | **调用方配置语义未成表：各形态下调用方需要准备什么（URL／客户端／启动命令）、应用侧配置什么、能否"零配置接入"，以及"无注册中心/无自动发现、handle 是运行期状态、`proxies` 是快照"这些前提，散落在正文里** | ✅ **T12（本轮指定纳入，未执行）** |
| **K** | **分离部署执行位置收尾：`external` 已由 `fb00b0f` 交付（`ConsoleGrpcBridgeHost --execution inprocess\|external`，`ConsoleSamplesEndToEndTests` 断言 `external-process`，且 `external` 下 operator 调用仍回到应用侧）；仍缺 —— ① `WpfAppOnlyDemo` 应用侧写死 `InProcess`（`Bridge/BridgeHost.cs:49`），而"桌面应用不想在自己进程里跑生成代码"正是该形态的核心诉求；② `AsonBridgeExecution.RemoteRunner` 在**分离部署**下没有任何样例或 E2E（Wave 1 只在单进程 `Ason.Tests` 覆盖）；③ `Docker` 取值无样例（按既有策略不启动守护进程，只断言上报）** | ✅ **T13（本轮指定纳入，未执行）** |
| **L** | **MCP 消费端接入不可落地：MCP 服务端三形态齐备且中继有进程级 E2E，但全仓库无任何可复制粘贴的 MCP 客户端配置（无 `mcp.json` / `claude_desktop_config.json`，文档仅一行命令行）→ stdio-only Agent（Claude Desktop/Code）这条唯一入口无法照做** | ✅ **T14（本轮指定纳入，未执行）** |
| **M** | **桥缺 `.http` 调用示例：仓库已有 `.http` 惯例（`samples/RemoteRunnerService/*.http`、`tests/TestRemoteExecutorServer/*.http`），但桥没有任何 `.http` 文件；文档里的 HTTP 调用只有零散 `curl`，无法"一键逐条试"** | ✅ **T15（本轮指定纳入，未执行）** |
| — | 决定不动的：Agent 聊天需模型 key、`--verify`/`--bridge-only` 为样例级钩子、集成测试单项目、gRPC 命名空间遮蔽 `Grpc`（已在文档给出写法） | ❌ 仅记录 |

---

## 3. Patterns to Mirror

| Category | Source | Pattern |
|---|---|---|
| 适配器能力门控 | `src/Ason.Bridge.Grpc/GrpcAsonBridgeService.cs`、`src/Ason.Bridge.Mcp/AsonBridgeMcpTools.cs`、`src/Ason.Bridge.OpenApi/AsonBridgeOpenApiExtensions.cs` | 先查 `Capabilities`，再映射到本协议原生信号：gRPC=`StatusCode.Unimplemented`、MCP=不注册工具、HTTP=404 |
| 错误码 | `src/Ason.Bridge/AsonBridgeErrorCodes.cs` + `AsonBridgeErrors.cs` | 所有失败落稳定错误码，不新增第二套词汇 |
| 授权（opt-in） | `Ason.RemoteBridge` 的 `MapAson(pattern, requireAuthorization)`；`AsonOpenApiBridgeOptions.ApiKey` | 授权是可选开关；默认 loopback 无鉴权可直接跑 |
| 契约版本 | `src/Ason.Bridge/AsonBridgeProtocol.cs` + `AsonBridgeManifest.ProtocolVersion` | 契约变更必须同步版本并写入文档 |
| 错误分支测试 | `tests/Ason.Bridge.Tests/GrpcErrorPathTests.cs` | 每个新分支配一个"该分支专属"的用例，命名说明场景 |
| 进程级 E2E | `tests/Ason.Bridge.Tests/ConsoleSamplesEndToEndTests.cs` + `TestSupport/{ConsoleBridgeHost,WpfApplication,WpfAgentRunner,RelayHost,TestPorts}.cs` | 新进程路径复用同一套宿主与端口工具 |
| 样例即测试钩子 | `WpfAppOnlyDemo --bridge-only`、`WpfAgentDemo --verify`、`ConsoleAgentSample --list` | 无模型、无桌面也能在 CI 驱动真实进程 |
| 三语文档 | `docs/app-agent-separation.*`、`docs/architecture.*`（含 `<!-- i18n: localize-labels -->`） | 用户可见变更三语同步；ASCII 只翻译标签、结构不动 |
| 覆盖率口径 | `coverlet.runsettings` | 排除 `**/Protos/*.cs`，只度量手写适配器 |
| 包内投递额外契约文件 | `src/Ason.ExternalExecutor/Ason.ExternalExecutor.csproj` + `buildTransitive/Ason.ExternalExecutor.targets`；`src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj` 现有 `<None … Pack="true" PackagePath="…"/>`（icon） | 包需要额外文件时用 `Pack="true"` + `PackagePath` 显式投递，不让消费者猜路径 |
| 样例/工程重命名 | `Ason.sln` 的 Project 行 + `.github/workflows/*.yml` 的构建清单 + 三语文档命令块 + `tests/Ason.Bridge.Tests` 的路径定位助手 | 重命名必须一次改到位，并用"无残留引用"的 grep 断言收口（样例非公开 API，不做别名/兼容） |

---

## 4. Files to Change（Wave 2 视角）

| File | Action | Why |
|---|---|---|
| `src/Ason.Bridge.Grpc/Protos/ason_bridge.proto` | UPDATE | T3 新增 `InvokeMcpTool`；T4 新增 `include_instance_declarations`（合并为一次契约变更，版本升 1.1） |
| `src/Ason.Bridge/AsonBridgeProtocol.cs` | UPDATE | 协议版本 → `1.1` |
| `src/Ason.Bridge/AsonBridgeRuntime.cs` | UPDATE | T4 实例声明拼装开关 + `instancesRevision`；T2 能力如实上报 |
| `src/Ason.Bridge/AsonBridgeManifest.cs`、`AsonBridgeCapabilities.cs` | UPDATE | T4 新字段；T2 能力语义注释 |
| `src/Ason.Bridge.Mcp/AsonBridgeMcpTools.cs` | UPDATE | T3 `ason_invoke_mcp_tool`；T2 日志流工具；T4 参数 |
| `src/Ason.Bridge.Mcp/McpAsonBridgeClient.cs`、`McpAsonBridgeTransport.cs` | UPDATE | T1 headers；T4 参数透传 |
| `src/Ason.Bridge.Grpc/GrpcAsonBridgeClient.cs`、`GrpcAsonBridgeTransport.cs`、`GrpcAsonBridgeEndpoint.cs` | UPDATE | T1 metadata/CallCredentials；T3 转发 MCP 透传；T4 参数 |
| `src/Ason.Bridge.Mcp/McpAsonBridgeEndpoint.cs` | UPDATE | T3 由 `not-supported` 改为转发（未配置时才 `not-supported`） |
| `src/Ason.Bridge.OpenApi/AsonBridgeOpenApiExtensions.cs`、`AsonOpenApiDocument.cs` | UPDATE | T2 SSE 端点；T4 body 字段；文档同步 |
| `src/Ason.Bridge.Grpc/AsonBridgeGrpcExtensions.cs`、`src/Ason.Bridge.Mcp/AsonBridgeMcpExtensions.cs` | UPDATE | T1 授权钩子 |
| `src/Ason.Bridge.McpHost/Program.cs` | UPDATE | T1 `--key`/`--header`；T2 视需要；T10 视需要（`--reflection` 不在此进程） |
| `src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj` | UPDATE | **T10** 把 `Protos\ason_bridge.proto` 打进包（`Pack="true" PackagePath="protos\"`）；（可选）`Grpc.AspNetCore.Server.Reflection` + `MapGrpcReflectionService()` 作为 opt-in |
| `README.*`、`docs/app-agent-separation.*` | UPDATE | **T10** 新增"非 .NET 调用方"小节：proto 获取路径、`protoc`/`grpcurl` 最小示例、服务与方法全名（三语）；**T12** 新增"调用方需要知道/配置什么"对照表（三语） |
| `samples/ConsoleGrpcBridgeHost/**` → `samples/ConsoleBridgeAppSample/**`、`samples/ConsoleGrpcBridgeDemo/**` → `samples/ConsoleBridgeCallerSample/**` | RENAME/CREATE/DELETE | **T11** 消除"Host/Demo 谁是哪一侧"的反读；示例非公开 API，纯重命名 |
| `Ason.sln`、`.github/workflows/ci.yml`、`docs/**`、`README.*`、`.agents/plans/**`、`docs/testing/**`、`tests/Ason.Bridge.Tests/TestSupport/**` | UPDATE | **T11** 重命名后的全量引用更新（含 CI 构建清单、三语命令块、E2E 助手与测试定位） |
| `tests/Ason.Bridge.Tests/*` | CREATE/UPDATE | T1–T5 的新测试；T4 鲜度测试；T2 流式测试 |
| `coverlet.runsettings`、`.github/workflows/ci.yml` | UPDATE | T5 样例覆盖率采集；T7 门禁与 Windows 作业补测；**T10 打包校验**（pack 后断言 nupkg 内含 proto） |
| `Directory.Build.props` | UPDATE | T6 版本递增 |
| `docs/app-agent-separation.*`、`docs/configuration.*`、`docs/contributing.*`、`docs/architecture.*`、`README.*` | UPDATE | T0 图 + T1/T2/T4 的能力与安全说明（三语） |
| `docs/testing/app-agent-separation.tdd.md` | UPDATE | T8 增补 "Wave 2" 章节 + 覆盖率表 + 缺口更新 |
| `samples/WpfAppOnlyDemo/**`（`App.xaml.cs`、`Bridge/BridgeHost.cs`） | UPDATE | **T13** 应用侧 `--execution inprocess\|external`（与 console 宿主同名同语义），并在 READY/端点面板显示实际执行位置 |
| `samples/RemoteRunnerService/RunnerServiceSample.csproj`、`samples/ConsoleGrpcBridgeHost/**` | UPDATE | **T13** 样例侧补 `--remote-url`／`ASON_BRIDGE_REMOTE_URL`（`AsonBridgeOptions.RemoteRunnerBaseUrl` 已存在），供 `remote-runner` 端口级 E2E 使用 |
| `tests/Ason.Bridge.Tests/RemoteRunnerBridgeEndToEndTests.cs`、`TestSupport/{ConsoleBridgeHost,TestPorts}.cs` | CREATE/UPDATE | **T13** `remote-runner` 分离部署的进程级 E2E（起 `RunnerServiceSample` + 应用侧 `--execution remote`） |
| `samples/mcp/claude_desktop_config.json`、`samples/mcp/README.md` | CREATE | **T14** stdio 中继与 HTTP MCP 两种接入的可复制配置；无 key 与 keyed 两种写法 |
| `samples/ConsoleMcpCallerSample/**` | CREATE | **T14** 最小 MCP 消费端（`McpAsonBridgeClient`，`--list`/`--call`）；命名与既有 `ConsoleMcpSample`（方向相反：ASON 调用 MCP 服务）显式区分 |
| `samples/bridge-examples.http`（或与 T11 命名协调后落在应用侧样例目录） | CREATE | **T15** 按形态的 HTTP 请求集合：manifest/instances/openapi、script、functions（两种粒度）、错误码与 401 |
| `docs/app-agent-separation.*`、`docs/contributing.*`、`README.*` | UPDATE | **T13/T14/T15** 运行矩阵补"WPF 侧 `--execution external`""接到真实 MCP 客户端""用 `.http` 逐条试"三行与对应命令块（三语）；layout 表补 `samples/mcp` |

---

## 5. Tasks

> 纪律：每项先写失败测试（RED，编译期或运行期皆可，须留证据）→ 最小实现（GREEN）→ 同分支检查点提交。
> Wave 1 的 RED/GREEN 逐条记录在 `docs/testing/app-agent-separation.tdd.md`。

### Wave 1 —— 已完成（保留供对照）

#### Phase 0 — 分支与骨架
- **Action**: 从 `feat/net6-net10-multitarget-and-abstractions` 建分支；创建 `src/Ason.Bridge`（`net6.0;net9.0`）并注册进 `Ason.sln`。
- **Mirror**: `src/Ason.RemoteBridge/Ason.RemoteBridge.csproj`（包元数据/`BaseOutputPath`/icon 打包方式）。
- **Validate**: `dotnet build src/Ason.Bridge/Ason.Bridge.csproj -c Release`。
- **Status**: ✅ DONE（`6c9452d` 之前的基础工作；期间移除两个引发 `NU1605` 的显式包引用）

#### Phase 1 — 桥核心（`Ason.Bridge`）
- **Action**: 清单/能力/执行器/运行时 + `RunnerClientAsonExecutor`；对 `Ason` 做追加式改动（`IRunnerTransport` 后续在 Phase 2；本阶段公开 `KeywordScriptValidator`、抽出 `OperatorVariableDeclarations`、`OperatorBase.IsAttached`、`RootOperator.OperatorInstances`）。
- **Mirror**: `OperatorApiCatalog.Describe`（列表与提示词同源）、`OperatorBuilder.Build`（method cache）。
- **Validate**: `dotnet test tests/Ason.Bridge.Tests -c Release` → **36/36**；`Ason.Tests` 101/101；smoke 11/11。
- **Status**: ✅ DONE（RED `6c9452d` → GREEN `0e9efc7`）

#### Phase 2 — 传输缝
- **Action**: `IRunnerTransport` 由 internal 改 public；`RunnerClient.UseTransport` + `RunnerTransportSettings.TransportFactory`；`RequiresTransport` 计入工厂。
- **Mirror**: `RunnerTransportManager.CreateTransport()` 既有分支结构。
- **Validate**: RED `error CS1061: RunnerClient 未包含 UseTransport` → GREEN `Ason.Tests` **104/104**。
- **Status**: ✅ DONE（`afc74c2`）

#### Phase 3 — gRPC 适配器
- **Action**: `ason_bridge.proto`（GetManifest/ListInstances/ExecuteScript/InvokeFunction/StreamExecution）、服务、类型化客户端、runner 传输、转发端点、扩展方法。
- **Mirror**: `Ason.RemoteBridge` 的 hub 接线与 `MapAson` 形态。
- **Validate**: RED `CS1061/CS0103` → GREEN **46/46**；环境风险先以 spike 排除。
- **Status**: ✅ DONE（`9c49c8c`）

#### Phase 3b — MCP 适配器 + stdio 中继
- **Action**: 按能力注册的工具面、Streamable HTTP 托管、类型化 MCP 客户端、MCP runner 传输；`Ason.Bridge.McpHost` 中继。
- **Mirror**: `AsonBridgeMcpTools` 的 `McpServerTool.Create` 用法（先反射确认 SDK 签名）。
- **Validate**: RED `CS0234` → GREEN **54/54**（修 `IList→IReadOnlyList`）。
- **Status**: ✅ DONE（`2b54c58`）

#### Phase 3c — 外部请求侧与宿主样例
- **Action**: `ConsoleGrpcBridgeDemo`（清单/实例/单函数/脚本/流式日志）与 `ConsoleGrpcBridgeHost`（应用侧，无 Agent）；`AsonBridgeOperators.MaterializeMarkerOnly`。
- **Mirror**: `samples/ConsoleMcpSample` 的控制台样例风格。
- **Validate**: **60/60** + 手工 E2E（`--func LibDemoStaticOperator.Add --args "[2,3]"` → `OK 5`）。
- **Status**: ✅ DONE（`2b54c58`）

#### Phase 4a — WPF 应用侧（无 Agent）
- **Action**: `WpfAppOnlyDemo`：窗口即 operator 宿主（视图 operator 绑定窗口集合 + 静态模块 + 仅标记模块 + `LibDemo`），同进程托管 gRPC/MCP/OpenAPI，`--bridge-only` 无窗口模式。
- **Mirror**: `samples/WptDemoApp` 的 `RootOperator`/`AttachChildOperator` 用法与 `ThemeMode` 处理。
- **Validate**: 构建 + 手工 E2E（`{"onUiThread":true,"threadId":2}`、脚本改名后状态可见）→ 由 Phase 4b 的测试自动化。
- **Status**: ✅ DONE（`7a16497`）

#### Phase 4b — WPF Agent 侧 + 传输选项
- **Action**: `WpfAgentDemo`（零 `[AsonOperator]`，端点/传输选择、API 列表、`--verify` 自检）；`AsonClientOptions.TransportFactory`。
- **Mirror**: `AsonClient` 既有构造与 `RunnerClient.UseTransport`。
- **Validate**: RED `CS0117` → GREEN **66/66**；E2E 断言 `agent-operators=0` 且 `remote-call=42`（gRPC/MCP 各一）。
- **Status**: ✅ DONE（`4b94c9c`）

#### Phase 5 — OpenAPI 适配器
- **Action**: `Ason.Bridge.OpenApi`：manifest/instances/script/functions(两种形态)/openapi.json；可选 key。
- **Mirror**: `AsonOpenApiBridgeOptions` 的 key 校验与 404/400 语义。
- **Validate**: **73/73**；手工 HTTP（`openapi=3.0.3`、函数 42、脚本 3）。
- **Status**: ✅ DONE（`7834718`）

#### Phase 6 — 缺口收敛（FO1）
- **Action**: `McpAsonBridgeEndpoint`（中继 MCP↔MCP）+ `--transport mcp`；**修 MCP 可选参数缺陷**；中继 stderr 日志；gRPC 错误路径测试；`coverlet.runsettings`。
- **Mirror**: `RelayEndpointTests` 的转发测试形态；`GrpcErrorPathTests` 命名。
- **Validate**: **86/86**；`.Grpc` 行覆盖 53.8%→**93.4%**。
- **Status**: ✅ DONE（`22a5ec5`）

#### Phase 7 — 文档审计补漏（FO2）
- **Action**: 三语补 `configuration`（`TransportFactory`）、`architecture`（桥拓扑）、`execution-modes`（第三轴）、`contributing`（覆盖率命令 + CI 段落）、`README`（桥指引）；删除 `Ason.Tests` 中悬空的 `SimpleConsoleApp` 引用。
- **Mirror**: 既有三语表格与 `i18n` 注释。
- **Validate**: `Ason.Tests` **105/105**。
- **Status**: ✅ DONE（`a54ff5a`）

#### Phase 8 — 流程图与无 Agent 文档（FO3，并行提交）
- **Action**: `architecture.*` 增 `Case 1–5` 流程块 + 边界表（D/E/A′）；无 Agent 调用方文档；形态↔样例↔命令映射。
- **Validate**: 三语结构一致；`git diff --stat` 仅文档。
- **Status**: ✅ DONE（`bd96f79`、`c4991af`、`18b8613`）

#### Phase 9 — 跨平台 Agent 样例（FO4，并行提交）
- **Action**: `samples/ConsoleAgentSample`；`ConsoleGrpcBridgeHost --execution inprocess|external`；`manifest.ToOperatorsLibrary()`；`ConsoleSamplesEndToEndTests`；`TestPorts`。
- **Validate**: **91/91**。
- **Status**: ✅ DONE（`fb00b0f`）

### Wave 2 —— 待执行

#### Task 1（T0）—— ASCII 流程图收尾（文档，无代码）· 0.5 天
- **Action**: ① `docs/architecture.{md,zh-CN,es}` 的 `Case 1` 增补 **boundary A′ 双向流**：`exec` 下行 → 应用内求值 → 执行器 **`invoke` 上行** → 真实方法执行 → **`invokeResult` 下行** → **`execResult` 回调用方**；并把紧随其后的 "operator calls do not [cross]" 明确限定为"在调用方边界 D 上"。② 新增 `Case 6 —— 远程运行器变体`（应用 → `Ason.RemoteBridge`(SignalR) → 服务器侧执行器）。③ 可选 `Case 7 —— 日志流`（与 T2 联动）。④ 三语结构一致性（编号/箭头/缩进一致，仅翻译标签）。
- **Mirror**: `docs/architecture.md:101-161` 现有 Case 1–5 与边界表；文件内 `i18n: localize-labels` 注释。
- **Validate**: 三语 `Case` 数量与框编号一致；`git diff --stat` 仅动文档。
- **Risk**: 低（唯一风险＝与既有"operator calls 不跨越"表述自相矛盾，正是要修的）。

#### Task 2（T1）—— 鉴权可达性（安全）· 1.5 天
- **Action**: ① `AddAsonGrpcBridge(..., policyName)` / `MapAsonGrpcBridge(policyName)`：未授权返回 `StatusCode.Unauthenticated`（**不得**用 `Unimplemented`，否则被误判为"能力未启用"）。② `AddAsonMcpBridge(..., requireAuthorization)`：走 ASP.NET `RequireAuthorization`/`AddAuthorizationFilters`，401。③ `McpAsonBridgeClient.ConnectAsync(endpoint, headers)` 补 `AdditionalHeaders`。④ `GrpcAsonBridgeClient.Connect(address, headers)` 以 `CallCredentials`/`Metadata` 注入。⑤ `Ason.Bridge.McpHost --key`（或可重复 `--header Name=Value`，`ASON_BRIDGE_KEY` 等价）。⑥ 三语 Security 小节补"各适配器如何开启鉴权、中继如何携带"。
- **Mirror**: `§3 授权（opt-in）`；`OpenApiBridgeTests.A_configured_bridge_key_is_required`。
- **Validate**: 新增 `GrpcAuthTests` / `McpAuthTests` / `RelayAuthTests`；`dotnet test tests/Ason.Bridge.Tests -c Release` 全绿且既有 console E2E 默认路径不回归；手工 `--key` 双向成功、去掉失败。
- **Risk**: 中（授权默认必须关闭；Unauthenticated vs Unimplemented 语义要被测死）。

#### Task 3（T3）—— `invokeMcpTool` 进入 gRPC 与 MCP（契约）· 1.5 天
- **Action**: ① proto 增 `rpc InvokeMcpTool` + request message。② MCP 增 `ason_invoke_mcp_tool`（仅当 `Capabilities.InvokeMcpTool` 为真时注册）。③ `GrpcAsonBridgeEndpoint` / `McpAsonBridgeEndpoint` 由 `not-supported` 改为转发；桥未配置 MCP 客户端时仍 `not-supported`（错误来源由"契约缺失"变为"运行时未配置"）。④ `AsonBridgeProtocol.Version` → `1.1`，文档写明"1.0 客户端仍可用（全部新增）"。
- **Mirror**: `AsonBridgeRuntime.InvokeMcpToolAsync`（已存在）；测试替身 `tests/TestMcpServer`。
- **Validate**: `GrpcMcpPassthroughTests`、`McpPassthroughTests`（含"未注册 → not-supported"）+ manifest 一致性断言。
- **Risk**: 中（proto 变更需端到端同步升级）。

#### Task 4（T4 / 缺口 D）—— 实例声明鲜度（契约/行为）· 1.0 天
- **Action**: ① `ExecuteScriptRequest` 增 `bool include_instance_declarations`（proto + MCP 参数 + HTTP body）：`true` 时由**应用**把当前实例变量声明拼到脚本体前（调用方只发 body）。② `AsonBridgeManifest` 增 `instancesRevision`（实例增删即变），供调用方判断清单是否过期。③ `ason_get_script_api` 保持最新；文档"已知限制"改写为"脚本用实例变量时用该开关（或先取最新 manifest），函数级接口始终解析活 handle"。④ 首选路径在样例落地（`ConsoleAgentSample --send`、WPF Agent）。
- **Mirror**: `AsonBridgeRuntime.BuildProxyPreamble()` / `BuildInstanceDeclarations()`（已存在，含 `OperatorVariableDeclarations.Build`）。
- **Validate**: `InstanceFreshnessTests`（快照 → 新建实例 → 不带开关失败 / 带开关成功；`instancesRevision` 变化）+ `ConsoleSamplesEndToEndTests` 增一条实例变量脚本 + 全量套件。
- **Risk**: 中（默认 `false` 必须与 Wave 1 行为完全一致，先加测试固定旧行为）。

#### Task 5（T2）—— `logStream` 口径与实现（契约）· 1.5 天
- **Action**: ① **口径已裁决 = (a)**：`Capabilities.LogStream` 保留"运行时支持日志流"的含义（加注释说明这是端到端可见性，不是运行时开关），**"你的传输给不给"由各适配器原生表面体现** —— gRPC 的 `StreamExecution` rpc、MCP 有没有 `ason_stream_script` 工具、HTTP 有没有 SSE 路由、OpenAPI 文档有没有该 path；**不新增**"按适配器改写 manifest"的机制。② HTTP：`POST {base}/script/stream`（SSE，`log* → result|error`），受 T1 授权约束。③ MCP：`ason_stream_script` 工具（先做工具，progress notification 作为后续增强，需先验证 stdio/HTTP 一致性）。④ 三语能力表补"各适配器如何拿到日志"。
- **Mirror**: `GrpcAsonBridgeService.StreamExecution`（channel + `runtime.Log`，复用事件形状）。
- **Validate**: `OpenApiStreamingTests`、`McpStreamingTests`、`CapabilityTests`（关闭 executeScript 时 stream 也不可用）+ 全量套件。
- **Risk**: 中（SSE 缓冲/代理；MCP progress 的传输差异）。

#### Task 6（T5）—— 覆盖率（测试）· 1.5 天
- **Action**: ① 逐条补候选分支：MCP 取消脚本、空参数调用、**每个适配器**能力关闭、中继 `--transport` 非法值、OpenAPI `handle` 经 query、流式错误事件、客户端自有 channel 释放（核对）。② 样例程序集覆盖率采集（console 三件套，**不设门禁**）。③ UI 自动化决策落地：FlaUI 接入并 `continue-on-error`，或明确"不接入 + 理由"（二选一，必须写进证据报告）。
- **Mirror**: `GrpcErrorPathTests` 命名与结构；`coverlet.runsettings`。
- **Validate**: `dotnet test … --collect:"XPlat Code Coverage" --settings coverlet.runsettings`；目标：四适配器**行 ≥87% 保持、分支 ≥70%**（`.Grpc` 从 60.5% 起）。
- **Risk**: 低（若某分支确实不可达，在报告里说明而不是硬凑）。

#### Task 7（T6）—— 发布卫生（发布）· 0.5 天
- **Action**: ① `Directory.Build.props` 版本 `0.8.2` → **`0.9.0`**（已裁决），并**盘点全仓库写死的版本引用一并更新**：`Directory.Build.props`、文档中出现的包名/镜像 tag（如 `ghcr.io/alexgoon/ason:<version>`）需与发布流程产出的 tag 一致、README 的版本提及、`samples/templates` 的模板内容（若其中引用了 `Ason*` 包版本）；`samples/templates/Ason.ProjectTemplates.csproj` 的 `PackageVersion 1.0.0` 属模板包自身版本，**不动**。② release notes：仓库现无固定位置 → 新增 `CHANGELOG.md`（英文，一条 `0.9.0`），列出新包 `Ason.Bridge` / `.Grpc` / `.Mcp` / `.OpenApi`（`Ason.Bridge.McpHost` 为样例 exe 不打包）、追加式 API 清单（`IRunnerTransport` 公开、`OperatorInstances` 公开、`KeywordScriptValidator` 公开、`IsAttached`、`TransportFactory`、`AsonBridgeAgent.ToOperatorsLibrary`、`AddAsonGrpcBridge(..., policy)`、`AddAsonMcpBridge(..., requireAuthorization)`、客户端 `headers` 参数、中继 `--key/--header`）、"无破坏性变更"确认。③ 核对 `publish-nuget.yml` 顺序与依赖。④ 核对 README 版本引用。
- **Validate**: `dotnet pack` 干跑四个包；Release 构建 + 版本/包名清单。
- **Risk**: 低（目标版本号需维护者确认）。

#### Task 8（T7）—— CI 加固（CI）· 0.5 天
- **Action**: ① 桥测试步骤加覆盖率采集 + 阈值（coverlet `<Threshold>`/`ThresholdType=line` 或解析 cobertura；建议先 `line ≥ 85%`，分支阈值先只提示不阻断）。② Windows 作业补 `dotnet test tests/Ason.Tests`（hermetic）与 smoke net10 腿（GA 后解除条件）。③ 落地 Task 6 的 FlaUI 决策。
- **Validate**: YAML 自检 + 本地复跑门禁命令确认阈值可过。
- **Risk**: 中（门禁过严会让 PR 变脆，阈值需留余量）。

#### Task 9（T8）—— 文档与证据报告收尾 · 0.5 天
- **Action**: ① `docs/app-agent-separation.*`：能力表（`invokeMcpTool`/`logStream` 的适配器差异）、鉴权小节、已知限制（T4 新口径）。② `docs/configuration.*` 新增适配器选项；`docs/contributing.*` 覆盖率门禁与新测试说明。③ `docs/testing/app-agent-separation.tdd.md` 新增 "Wave 2" 章节（逐项：执行摘要、验证命令、RED/GREEN 证据、保证什么）+ 更新覆盖率表与缺口清单（T1–T4 从缺口移除）。④ 本文件与证据报告互相引用。
- **Validate**: 三语结构一致（标题层级、`i18n` 注释、边界命名 D/E/A′ 一致）；全量验证矩阵复跑。
- **Risk**: 低。

#### Task 10（T10 / 缺口 H）—— 非 .NET 调用 gRPC：契约交付（打包 + 文档）· 0.5 天
- **Action**:
  ① **打包 proto**：`src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj` 增加
  `<None Include="Protos\ason_bridge.proto" Pack="true" PackagePath="protos\" />`
  （实测现状：nupkg 里只有 `lib/net9.0/Ason.Bridge.Grpc.dll`、`icon.png`、`nuspec`，无 proto）。
  ② **反射（已裁决：提供 opt-in、默认关闭）**：`Grpc.AspNetCore.Server.Reflection` + `MapGrpcReflectionService()`，以
  `MapAsonGrpcBridge(enableReflection: true)`（或样例 `--reflection`）**opt-in**，让 `grpcurl` 与非 .NET
  调用方无需本地 proto 即可调用；**默认关闭**，文档写明"反射等于把可调用面再对外广播一次，应与 T1 的鉴权并用"。
  ③ **三语文档**：`docs/app-agent-separation.*` 新增"非 .NET 调用方"小节 —— proto 的两条获取路径
  （NuGet 包内 `protos/ason_bridge.proto`，可用 `GeneratePathProperty=true` 定位 `$(PkgAson_Bridge_Grpc)`，或解包；
  仓库 `src/Ason.Bridge.Grpc/Protos/ason_bridge.proto`）、三种最小示例
  （`python -m grpc_tools.protoc`、`protoc --go_out`/`--java_out`、`grpcurl -proto`）、服务与方法全名
  （`ason.bridge.v1.AsonBridge/{GetManifest,ListInstances,ExecuteScript,InvokeFunction,StreamExecution}`；
  T3 会再加 `InvokeMcpTool`）、以及"**能调什么仍以 manifest 为准**"这一边界。
  ④ **Python 调用示例（本轮追加要求，必须交付）**：`samples/python/` 下给一个可直接运行的 Python 脚本
  （`grpc_tools.protoc` 生成 stub → 连接 → `GetManifest` / `InvokeFunction` / `ExecuteScript`），
  含 `requirements.txt` 与 README 里的三条命令；脚本要能对 `--url` 传参，并在 CI 无法安装 Python 依赖时
  以"文档 + 可选手工验证"的方式交付（是否进 CI 由 T6 决定）。
- **Mirror**: `Ason.ExternalExecutor` 的 `Pack="true"`/`buildTransitive` 投递方式；`Ason.Bridge.Grpc.csproj` 现有 icon 投递；`docs/app-agent-separation.*` 既有"传输"小节与 `i18n` 注释。
- **Validate**: `dotnet pack src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj -c Release -o ./artifacts/pack` 后解包断言存在 `protos/ason_bridge.proto`（脚本化并入 CI）；
  手工 `grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto -d "{\"code\":\"return 1;\"}" localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript` 跑通（若②落地，再验证不带 `-proto` 也可用）；三语结构一致。
- **Risk**: 低（纯投递 + 文档）；唯一决策点是②的反射默认值及其与 T1 鉴权的组合语义。
- **依赖/顺序**: **必须排在 T3 与 T4 之后** —— 那两项会改动 proto（`InvokeMcpTool`、`include_instance_declarations`），否则打包与文档要跟随两次。

#### Task 11（T11 / 缺口 I）—— 示例命名可读性 + 角色图例（重命名）· 0.5 天
- **Action**:
  ① **重命名**：`samples/ConsoleGrpcBridgeHost/` → `samples/ConsoleBridgeAppSample/`（工程 `ConsoleBridgeAppSample.csproj`）、
     `samples/ConsoleGrpcBridgeDemo/` → `samples/ConsoleBridgeCallerSample/`（工程 `ConsoleBridgeCallerSample.csproj`）；同步
     `AssemblyName`/`RootNamespace`（若有）与输出名。
  ② **引用全量更新**：`Ason.sln`（Project 行 + `NestedProjects`）、`.github/workflows/ci.yml`（Linux 作业构建清单）、
     三语文档的所有命令块与形态表、`README.*`、`.agents/plans/app-agent-separation.plan.md`、
     `docs/testing/app-agent-separation.tdd.md`、`tests/Ason.Bridge.Tests/TestSupport/`（路径定位助手）与相关 E2E 断言。
  ③ **角色图例**：在 `docs/app-agent-separation.{md,zh-CN,es}` 形态矩阵上方加一行 ——
     "`App`/`Host` = **应用侧**（暴露端点）；`Caller`/`Demo` = **调用方**（连接端点）；同一程序可以横跨两条边界（例如中继）"。
  ④ **不做别名或兼容**（样例不是公开 API）。
- **Mirror**: §3「样例/工程重命名」；既有命名风格（应用侧样例 `ConsoleMcpSample`/`WpfAppOnlyDemo`，角色即名字）。
- **Validate**: `Select-String -Pattern 'ConsoleGrpcBridgeHost|ConsoleGrpcBridgeDemo'` 在 `git ls-files` 范围内**无残留**（历史提交与 §10 记录除外）；
  `dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release` 仍 **91/91**；
  `dotnet build samples/ConsoleBridgeAppSample -c Release` 与 `samples/ConsoleBridgeCallerSample -c Release` 通过；CI YAML 清单同步。
- **实测爆炸半径（本轮核实，供 T11 定范围）**: 旧名在 **8 个文件、46 行**中出现 —— `docs/app-agent-separation.{md,zh-CN,es}`（各 2 行形态表 + 6–8 行命令块）、`docs/contributing.{md,zh-CN,es}`（layout 表各 1 行）、
  `docs/testing/app-agent-separation.tdd.md`（6 行）、样例工程目录与 `Ason.sln`、CI 清单、`tests/Ason.Bridge.Tests` 的路径定位助手、本计划文件。
- **豁免口径（必须显式写进 T11 的验收，否则 grep 断言永远红）**:
  ① `docs/testing/app-agent-separation.tdd.md` 的**历史记录段落不重写**（那是"当时发生了什么"的事实记录），改为在文件顶部加一行命名沿革注记（`ConsoleGrpcBridgeHost → ConsoleBridgeAppSample`、`ConsoleGrpcBridgeDemo → ConsoleBridgeCallerSample`）；
  ② 本计划文件 §10 的历史行、以及 git 历史提交不参与 grep；
  ③ 可执行的断言范围收窄为：`samples/**`、`Ason.sln`、`.github/workflows/**`、`tests/**` 与 `docs/app-agent-separation.*`、`docs/contributing.*` 的**正文命令块**。
- **命名一致性（需与 §9-5 一并裁决）**: T11 提议的两个新名 `ConsoleBridgeAppSample` / `ConsoleBridgeCallerSample` **不带 `Sample` 后缀**，而仓库既有样例是 `ConsoleMcpSample` / `ConsoleExtractorSample` / `ConsoleAgentSample`（T14 拟新增的 `ConsoleMcpCallerSample` 也带后缀）。二者必居其一：**(i)** 统一保留后缀 → `ConsoleBridgeAppSampleSample` / `ConsoleBridgeCallerSampleSample`（建议，churn 最小、与多数样例一致）；**(ii)** 统一去掉后缀并顺带改既有新样例（churn 大，收益低）。
- **Risk**: 低-中（机械但面广：漏一处会让 CI 构建失败或文档命令失效 → 用 grep 断言收口）。
- **顺序**: 排在 **T8/T9 之前**（否则 CI 与文档要被改两遍）；与 T2/T5/T6 无依赖，可并行。
- **备选（零 churn）**: 若维护者不愿改目录名，退化为"仅加 ③ 角色图例 + 在表头标注旧名"，并在 §10.3 记录该决定；**默认按 ①–④ 执行**。

#### Task 12（T12 / 缺口 J）—— "调用方需要知道/配置什么"对照表（文档）· 0.5 天
- **Action**: 在 `docs/app-agent-separation.{md,zh-CN,es}` 的"不用 Agent…"之后新增一节
  **"调用方接入：需要知道什么、需要配置什么"**：
  ① **表**：按形态（HTTP/OpenAPI、MCP-over-HTTP、gRPC（.NET／非 .NET）、stdio-only MCP）列出——调用方要准备
     （一个 URL／一个客户端包装／**写一条启动命令**）、应用侧要配置（挂哪个 `AddAson*Bridge` + `Map*`、端口、`capabilities`、可选 key、`Execution`）、
     以及"能否零配置接入"的判定：HTTP/OpenAPI 与 MCP-HTTP = ✅ 一个 URL；gRPC = ⚠️ 需客户端包装（非 .NET 还需 proto → T10）；stdio MCP = ❌ 必须写启动配置。
  ② **三条前提显式写出**：**无注册中心/mDNS/自动发现**（URL 必须带外告知：命令行、配置、环境变量；manifest 只是"已知 URL 之后"的发现机制）；
     **handle 是运行期状态**（不先取 instances 就会撞上 `handle-required`/`handle-ambiguous`/`handle-not-found`）；**`proxies` 是快照**（脚本用实例变量时的口径 → T4）。
  ③ 能力开关由应用侧决定、调用方无法改变（关闭时：gRPC→`Unimplemented`、MCP→工具不存在、HTTP→`404`），与 T1 的**鉴权开关**并列说明。
  ④ **交叉链接**：`docs/architecture.*` 的边界 D 段、`docs/index.*`「从哪里开始」行、`docs/execution-modes.*` 的第三轴段。
- **Mirror**: 既有三语表格风格与 `i18n: localize-labels` 注释；`docs/app-agent-separation.*` 的"传输"与"不用 Agent"小节。
- **Validate**: 三语标题层级与表格列数一致；表中结论都引用 **T10/T11 之后**的样例名与可跑命令；`git diff --stat` 仅文档。
- **Risk**: 低（纯文档）；唯一风险是三语漂移 → 由 T9 的跨文档命名核对兜住。
- **顺序**: 与 T11 同批（都在 T9 之前），避免文档被重复编辑两次。

#### Task 13（T13 / 缺口 K）—— 分离部署的执行位置收尾（样例 + 测试）· 1.0 天
- **现状核实（避免重复劳动）**: `external` 已在 `fb00b0f` 交付 —— `samples/ConsoleBridgeAppSample`（原名 `ConsoleGrpcBridgeHost`，见 T11）支持 `--execution inprocess|external`（`ASON_BRIDGE_EXECUTION` 等价），`tests/Ason.Bridge.Tests/ConsoleSamplesEndToEndTests.cs` 已断言 `manifest.Execution == "external-process"` 且 operator 调用仍回到应用侧。**本任务只收尾三件剩下的事**。
- **Action**:
  ① **WPF 应用侧补同一个开关**：`WpfAppOnlyDemo --bridge-only --execution inprocess|external`（`App.xaml.cs` 解析、`Bridge/BridgeHost.cs:49` 由写死 `InProcess` 改为读参数），端点面板/READY 行显示实际 `execution`；理由写进注释：桌面应用最需要"生成代码不在自己进程里跑"，而 operator 调用仍经捕获的 `SynchronizationContext` 回到 UI 线程。
  ② **`remote-runner` 的分离部署 E2E**：新增 `tests/Ason.Bridge.Tests/RemoteRunnerBridgeEndToEndTests.cs` —— 用 `TestPorts` 预留端口 → 起 `samples/RemoteRunnerService/RunnerServiceSample.csproj`（`ASON_REMOTE_RUNNER_URL` 同款就绪探测）→ 起应用侧 `ConsoleBridgeAppSample --execution remote --remote-url <url>`（样例补该参数，`AsonBridgeOptions.RemoteRunnerBaseUrl` 已存在）→ 断言 manifest `execution=remote-runner`、脚本经 SignalR 宿主求值、**函数调用与 operator 回环仍在应用侧**。
     **进程策略（本轮核实后补充）**：测试必须启动**已构建产物**（`TestSupport` 里同款"定位 dll → `dotnet exec`"助手），**不要用 `dotnet run`**（会触发构建、放大时序抖动）；同时把 `samples/RemoteRunnerService/RunnerServiceSample.csproj` **加入 Linux CI 的构建清单**（现清单只含桥项目与 console 样例，缺它这条 E2E 会因找不到产物而跳过）。
  ③ **`docker` 取值**：**计划原文"不启动守护进程、只断言上报"在实现上不成立** —— `AsonBridgeRuntime.GetManifestAsync()` 会 `await Executor.StartAsync()`，而 `ScriptRunnerProcessHost.StartAsync()`（`src/Ason.Runner.Core/ScriptRunnerProcessHost.cs:31`）**当场 spawn** 子进程（Docker 模式下即 `docker run`）。因此改为两件事：
     1. **上报口径用注入执行器验证**：`AsonBridgeOptions.Executor` 已是公开扩展点，测试注入一个 `Name = "docker"` 的桩执行器，断言 manifest/适配器一致地上报 `docker` —— 验证的是"执行位置来自执行器名字"这条插头，不依赖守护进程；
     2. **真实 Docker E2E 与既有策略一致**：需要守护进程的用例沿用 `Ason.Tests` 的排除口径（`--filter "DisplayName!~Docker…"`），并在三语文档写明该取值需要 Docker。
     3. **不做**"延迟启动执行器以免 manifest 失败"的行为改动（那会改变 `external` 的既有语义，属另一件事；如确需，另开任务）。
- **Mirror**: `ConsoleBridgeAppSample`（原名 `ConsoleGrpcBridgeHost`）的 `--execution` 解析与 `ASON_BRIDGE_READY` 行；`ConsoleSamplesEndToEndTests` + `TestSupport/{ConsoleBridgeHost,TestPorts}`；`Ason.Tests` 的 Docker 排除口径（`--filter "DisplayName!~Docker…"`）。
- **Validate**: `dotnet test tests/Ason.Bridge.Tests -c Release`（新增 remote E2E 不需要桌面，Linux/Windows 均可跑）；`dotnet test tests/Ason.Tests -c Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"`；`dotnet build samples/WpfAppOnlyDemo/WpfAppOnlyDemo.csproj -c Release`；手工 `WpfAppOnlyDemo --bridge-only --execution external` 后 `--func EmployeesOperator.GetDiagnostics` 仍返回 `onUiThread=true`（证明执行位置不影响 operator 回环）。
- **Risk**: 中（remote E2E 引入"两个真实进程 + 一条 SignalR 连接"；若出现端口/时序抖动，落到与 WPF E2E 相同的串行 collection 并复用 `TestPorts`）。
- **顺序**: 与 T2/T5/T6 无依赖；**必须在 T11 之后**（T11 会重命名 console 样例，否则命令块与 CI 清单要改两遍），并在 T9 之前。

#### Task 14（T14 / 缺口 L）—— Python MCP 消费端 + OpenAI 驱动的自动调用测试（样例 + 文档 + 自动化）· 1.5 天
- **现状核实**: MCP 服务端（HTTP 直连 / stdio 中继）齐备且有进程级 E2E（`McpRelayHostTests`），但**全仓库没有任何可复制粘贴的 MCP 客户端配置**（`mcp.json`、`claude_desktop_config.json` 均无匹配），文档只有一行 `Ason.Bridge.McpHost --url …` —— stdio-only Agent 这条唯一入口无法照做。
- **已裁决（§9-6 / §0）**: 最小消费端用 **Python**；并且必须用 **OpenAI 官方库**（`openai`）+ **配置** + **命令行传入对话指令**，实现"**模型自动选择并调用 MCP 工具**"的**自动化测试** —— 断言的是"模型真的调用了应用侧的 operator 并拿到结果"，不是"连得上"。
- **Action**:
  ① `samples/mcp/claude_desktop_config.json`：`mcpServers` 一条指向中继（`command=dotnet`、`args=[exec, <Ason.Bridge.McpHost.dll 路径占位>, --url, http://localhost:5222]`），另附 **HTTP MCP** 等价片段（`url: http://localhost:5223/mcp`，供 Claude Code / IDE 类客户端）。
  ② `samples/mcp/README.md`：三步（先起应用侧样例 → 再让 Agent 启动中继 → 工具名 `ason_get_manifest` / `ason_list_instances` / `ason_execute_script` / `ason_invoke_function`），无 key 与 keyed（T1 已交付 `--key`/`--header`）两种写法。
  ③ **Python 消费端（自研 MCP 客户端，最小实现）** `samples/python/ason_mcp_caller/main.py`：以 stdio 启动中继（或直连 HTTP MCP），实现 `initialize` → `tools/list` → `tools/call` 的最小 JSON-RPC 往返；CLI：`--url`、`--transport stdio|http`、`--list`、`--call Operator.Method --args '[2,3]'`；随附 `requirements.txt` 与运行说明。
  ④ **OpenAI 驱动的自动调用测试（本任务的核心交付）** `samples/python/ason_mcp_agent/main.py`：
     - **配置**：`--base-url` / `--model` / `--api-key`（环境变量 `MY_OPEN_AI_BASE_URL` / `MY_OPEN_AI_MODEL` / `MY_OPEN_AI_KEY` 等价，复用仓库既有命名），未配置时以退出码 2 明确报错；
     - **命令行指令**：`--instruction "…"`（例如"把员工 1 改名为 Ada，并告诉我结果"），可重复以跑多条用例；
     - **机制**：`tools/list` 拿到的 MCP 工具**直接喂给 OpenAI 的 function calling**（`tools=[...]`），模型自主决定调用哪个工具、参数由模型给出；脚本执行该工具、把结果回灌给模型，循环直到模型给出最终答复（带最大轮数保护）；
     - **断言**：把"确实发生过 `ason_invoke_function`/`ason_execute_script` 调用"与"应用侧状态确实变了"作为通过条件（退出码 0/1），并把每轮的 tool-call 轨迹打印出来 —— 这就是"第三方 Agent 能否自动用起来"的自动化答案；
     - **可重复性**：总轮数与超时上限写死，避免 CI 里挂死；无 key 时按 §2.4「决定不动」的既有口径明确**报错退出**（而不是伪装成功）。
  ⑤ 三语文档：`docs/app-agent-separation.*` 的 Samples 矩阵新增"接到真实 MCP 客户端"一行 + Python 两条命令（最小客户端 / OpenAI 驱动）写进命令块；`docs/contributing.*` layout 表补 `samples/mcp` 与 `samples/python`。
- **Mirror**: `samples/ConsoleMcpSample`（MCP 用法风格，方向相反：那里是 ASON 去调外部 MCP 服务）；`McpAsonBridgeClient` / `AsonBridgeMcpTools` / `AsonBridgeMcpExtensions`（既有 API，**不新增库代码**）；`AgentPrompts`/`OpenAiCompatibleChatServiceFactory` 的环境变量命名口径（`MY_OPEN_AI_*`）。
- **Validate**: `samples/mcp/claude_desktop_config.json` 用 `ConvertFrom-Json` 解析并断言字段与 `Ason.Bridge.McpHost.dll` 指向；Python 最小客户端对中继跑通 `--list` / `--call`（手工或 CI 有 Python 时）；OpenAI 驱动的自动测试在**有 key 的环境**跑一条"改名并断言结果"的用例（无 key 时明确报错退出，报告里记录为"需 key"）；三语文档结构一致。
- **Risk**: 中（新增 Python 依赖与一条需要 key 的自动化用例）。**缓解**：Python 依赖放进 `requirements.txt` 并在 README 写清；自动测试的"无 key 即报错"行为要有测试固定，避免它被误当成通过；CI 是否纳入由 T6 决定（默认先在本地/可选作业跑）。
- **顺序**: 与 T10（非 .NET 调用 gRPC 的契约投递）互补、可并行；都在 T9 之前。

#### Task 15（T15 / 缺口 M）—— 桥的 `.http` 调用示例（样例 + 文档）· 0.5 天
- **现状核实**: 仓库已有 `.http` 惯例（`samples/RemoteRunnerService/*.http`、`tests/TestRemoteExecutorServer/*.http`），桥**没有**任何 `.http`；文档只有零散 `curl`，无法"一键逐条试"。
- **Action**: 新增 `samples/bridge-examples.http`（若 T11 已把 console 样例改名，则落在应用侧样例目录下），内容按四组组织，文件头写明"先起应用侧样例"：
  ① **发现**：`GET {{base}}/manifest`、`GET {{base}}/instances`、`GET {{base}}/openapi.json`；
  ② **执行**：`POST {{base}}/script`（含/不含 `includeProxyPreamble`）、`POST {{base}}/functions/invoke`（静态模块免 handle / 实例 operator 带 handle 两种）、`POST {{base}}/functions/{operator}/{method}`；
  ③ **错误面**：未知 operator（400 + `operator-not-found`）、缺 handle（`handle-required`）、能力关闭（404）；
  ④ **鉴权**：配 key 时缺头（401）与带头（200）—— 与 T1 落地后同步。
  变量：`@base = http://localhost:5223`、`@key =`（留空＝未开鉴权）。三语文档的 HTTP 片段改为指向该文件。
- **Mirror**: `samples/RemoteRunnerService/RunnerServiceSample.http` 的 `@变量` 与分节注释写法；`docs/app-agent-separation.*` 现有 `curl` 片段（替换为引用）。
- **Validate**: 用 REST Client / `curl` 逐条执行并记录 3–5 条真实响应码（写进证据报告）；三语命令块同步；`git ls-files` 含该文件。
- **Risk**: 低（纯样例文件）。需与 T1（鉴权）、T3（MCP 透传）、T5（日志流）的契约变化保持同步：那三项落地后本文件要补"带 key 的头""透传请求""SSE 流"三条。
- **顺序**: 无硬依赖；排在 **T1/T3/T5 之后**（否则要补两次），并在 T9 之前。

---

## 6. Validation

```bash
# 桥（含进程级 E2E；Windows 上会真正起 WPF 样例）
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release

# 库回归（适配器改动可能触达 RunnerClient/协议）
dotnet test tests/Ason.Tests/Ason.Tests.csproj -c Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj -c Release --framework net9.0

# 覆盖率（含生成源排除口径）
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# 样例构建（与 Linux CI 作业同一清单）
dotnet build samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj -c Release
dotnet build samples/ConsoleAgentSample/ConsoleAgentSample.csproj -c Release
dotnet build samples/ConsoleBridgeCallerSample/ConsoleBridgeCallerSample.csproj -c Release

# T10：契约交付（非 .NET 调用方）
dotnet pack src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj -c Release -o ./artifacts/pack
#   解包断言（nupkg 是 zip，先改扩展名）：应存在 protos/ason_bridge.proto
#   Copy-Item artifacts/pack/Ason.Bridge.Grpc.*.nupkg $env:TEMP\p.zip; Expand-Archive $env:TEMP\p.zip $env:TEMP\p
#   Get-ChildItem -Recurse $env:TEMP\p -Filter *.proto
#   非 .NET 调用方手工验证：
#   grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto \
#           -d "{\"code\":\"return 1;\"}" localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript

# T11：重命名后的"无残留引用"断言
# 口径（与 T11 的豁免一致）：只扫可执行/正文范围，历史记录（本文件 §10 与 tdd 历史段落）与 git 历史不计。
$scope = git ls-files -- samples Ason.sln .github/workflows tests docs/app-agent-separation.md docs/app-agent-separation.zh-CN.md docs/app-agent-separation.es.md docs/contributing.md docs/contributing.zh-CN.md docs/contributing.es.md
$scope | Select-String -Pattern 'ConsoleGrpcBridgeHost|ConsoleGrpcBridgeDemo'   # 期望：无输出
dotnet build samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj -c Release
dotnet build samples/ConsoleBridgeCallerSample/ConsoleBridgeCallerSample.csproj -c Release

# T12：三语文档结构一致性（Case/边界/表格列数）
git diff --stat -- docs/README.*      # 期望：仅文档

# T13：执行位置收尾（WPF 侧开关 + remote-runner 分离部署 E2E + docker 如实上报）
dotnet build samples/WpfAppOnlyDemo/WpfAppOnlyDemo.csproj -c Release
#   手工（external 下 operator 仍回 UI 线程）：
#   WpfAppOnlyDemo --bridge-only --execution external --port 5222
#   ConsoleBridgeCallerSample --url http://localhost:5222 --func EmployeesOperator.GetDiagnostics   # onUiThread=true
#   remote-runner E2E 由套件内的 RemoteRunnerBridgeEndToEndTests 覆盖（起 RunnerServiceSample + 应用侧 --execution remote）
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release

# T14：MCP 消费端接入
#   配置可机读性断言：
#   Get-Content samples/mcp/claude_desktop_config.json -Raw | ConvertFrom-Json   # 必须解析成功且指向 Ason.Bridge.McpHost.dll
#   手工链路：起应用侧样例 → 起中继 → 最小消费端 --list / --call
dotnet build samples/ConsoleMcpCallerSample/ConsoleMcpCallerSample.csproj -c Release

# T15：.http 示例（逐条执行并记录响应码）
#   REST Client：samples/bridge-examples.http（先起应用侧样例；401 用例需应用侧配 key）
git ls-files | Select-String -Pattern 'bridge-examples\.http|samples/mcp/'
```

**Wave 2 依赖顺序**：Task 1（T0）独立 → Task 2（T1）→ Task 3 + Task 4 合并为一次契约变更（proto 1.1）→
**Task 10（T10，必须落在 T3/T4 之后：它交付的就是那份 proto）** → **Task 11 + Task 12（T11/T12，重命名与调用方对照表；都在 T8/T9 之前，避免 CI 与文档被改两遍）** →
**Task 13（T13，执行位置收尾；必须在 T11 之后——它会改样例命令行与 CI 清单）** → Task 5（T2，SSE 必须受 T1 约束）与 **Task 15（T15，`.http` 示例；必须在 T1/T3/T5 之后，否则要补两次）** → Task 14（T14，MCP 消费端接入；与 T10 互补、可并行）→ Task 6（T5，覆盖 1–5、10–15 的新分支）→ Task 7（T6）与 Task 8（T7）→ Task 9（T8）。
合计约 **14.0 人日**（Wave 2；含本轮新增的 T10 0.5 + T11 0.5 + T12 0.5 + T13 1.0 + T14 1.5 + T15 0.5）。

---

## 7. Risks

| Risk | Likelihood | Mitigation |
|---|---|---|
| proto 变更导致客户端/服务端版本错配 | 中 | Task 3/4 合并一次变更；`protocolVersion` 升 1.1；manifest 带版本、客户端启动即校验 |
| 鉴权默认开启导致样例/CI 回归 | 中 | 授权一律 opt-in；既有 E2E 默认路径不变；新增"关闭授权仍可用"测试 |
| `ExecuteScript` 语义扩展误伤既有调用方 | 中 | 新参数默认 `false`（与现状一致）；先加测试固定旧行为 |
| SSE / MCP 日志流的传输差异 | 中 | 先"如实上报能力"，实现后按适配器分别测；MCP 保留工具回退 |
| 覆盖率门禁过严致 PR 变脆 | 中 | 阈值按当前值留余量；分支阈值先提示不阻断 |
| 三语文档不一致累积 | 中 | 每项任务把三语同步列为验收项；`i18n: localize-labels` 为约束 |
| 文档间编号/命名漂移（D/E/A′） | 低 | Task 9 做一次跨文档命名核对 |

---

## 8. Acceptance

**Wave 1（已完成，勾选保留）**
- [x] 保留 `WptDemoApp`；新增应用侧（无 Agent）与 Agent 侧（零 operator）两个 WPF 演示
- [x] 桥核心与传输无关；gRPC / MCP / OpenAPI 各自独立 csproj，`Ason` 不引用任何传输库
- [x] 整段脚本与单函数两种接口可独立开关、可组合；清单如实反映生效能力
- [x] 执行请求可转给内部执行器或 `Ason.ExternalExecutor`（`--execution inprocess|external` 有 E2E）
- [x] 三语用户文档 + 证据报告 + CI（Linux 作业 + `windows-samples` 作业）
- [x] `Ason.Bridge.Tests` 91/91、`Ason.Tests` 105/105、smoke 11/11

**Wave 2（已完成）**
- [x] Task 1：三语 `architecture.*` 含 `Case 1–6`（+ 7），应用侧执行器的 `invoke` 回环已画出，表述不再自相矛盾
- [x] Task 2：gRPC/MCP 授权开关可用且语义正确（Unauthenticated / 401）；类型化 MCP 客户端与中继可携带 key；默认路径不回归
- [x] Task 3：`invokeMcpTool` 在 gRPC 与 MCP 可用；未配置 MCP 客户端时返回 `not-supported`
- [x] Task 4：`includeInstanceDeclarations` 与 `instancesRevision` 有测试；默认行为与 Wave 1 完全一致
- [x] Task 5：manifest 的 `logStream` 与各适配器实际能力一致；HTTP SSE 与 MCP 日志流有测试
- [x] Task 6：适配器行覆盖 ≥87%、分支 ≥70%（有命令与数字记录）；UI 自动化决策已落地并写明理由
- [x] Task 7：版本号与 release notes 就绪；4 个包可打包且顺序正确
- [x] Task 8：CI 有覆盖率门禁；Windows 作业覆盖 `Ason.Tests`
- [x] Task 9：三语文档与证据报告同步；全量验证矩阵通过；每项任务都有 RED→GREEN 检查点提交
- [x] Task 10：`Ason.Bridge.Grpc` 包内含 `protos/ason_bridge.proto`（有解包断言，必要时并入 CI）；三语"非 .NET 调用方"小节含两条 proto 获取路径与最小示例；反射若开启则默认关闭且与 T1 鉴权语义一致
- [x] Task 11：`ConsoleBridgeAppSample` / `ConsoleBridgeCallerSample` 重命名到位、`git ls-files` 无残留引用、sln/CI/三语文档/E2E 助手同步、形态矩阵含角色图例
- [x] Task 12：三语新增"调用方接入：需要知道什么、需要配置什么"节，含形态对照表 + 三条前提（无自动发现／handle 运行期／proxies 快照）+ 交叉链接
- [x] Task 13：`WpfAppOnlyDemo --execution external` 可用且 operator 回环仍 `onUiThread=true`；`remote-runner` 分离部署有进程级 E2E（断言 manifest `execution=remote-runner`）；`docker` 取值的上报有断言且文档写明需要 Docker；`external` 既有交付不被重复实现
- [x] Task 14：`samples/mcp/claude_desktop_config.json` 可被 JSON 解析且指向中继；HTTP MCP 片段与 stdio 片段都在；最小消费端 `--list`/`--call` 就位；三语 Samples 矩阵含"接到真实 MCP 客户端"一行
- [x] Task 15：`samples/bridge-examples.http` 覆盖发现/执行/错误面/鉴权四组，逐条执行有记录；三语 HTTP 片段改为引用该文件

---

## 9. 确认门（WAIT）

规划阶段不写实现代码。请检测本文件后回复：

- `yes` / `proceed` —— 按 Task 1 → 2 → 3+4 → 5 → 6 → 7 → 8 → 9 顺序开工；
- `modify: …` —— 增删任务、调整顺序或缩小范围；
- `skip Tn` —— 跳过某项。

**待裁决的不确定点**

1. **目标版本号**：✅ 已裁决 = `0.9.0`，并且**要更新对应软件的版本号**（T7 落地：`Directory.Build.props` + 文档中写死的版本引用，如 Docker 镜像 tag 需与发布流程一致）。
2. **Task 5 的 MCP 日志流**：走 **progress notification**（更原生，需先验证 stdio/HTTP 一致性）还是 **`ason_stream_script` 工具**（更通用，可作回退）。
   → ✅ 口径已裁决（= (a)，见 §0）；**实现形式**仍按此条二选一，由 T5 实现时先做一次 stdio/HTTP 一致性验证再定（默认先做 `ason_stream_script` 工具，progress 作为后续增强）。
3. **Task 6 的 FlaUI**：接入 Windows 作业并 `continue-on-error` 观察，还是明确不接入并写入文档。
4. **Task 10 的 gRPC 反射**：✅ 已裁决 = **提供 opt-in 开关、默认关闭**；并**追加要求：交付 Python 的 gRPC 调用示例**（见 T10 ④）。
5. **Task 11 的重命名目标名**：✅ 已裁决 = **保留 `Sample` 后缀**（`ConsoleBridgeAppSampleSample` / `ConsoleBridgeCallerSampleSample`）；目录改名照 T11 ①–④ 执行（历史记录不重写，只加沿革注记）。
6. **Task 14 的最小消费端语言**：✅ 已裁决 = **Python**，并**追加要求**：用 **OpenAI 官方库**（`openai`）+ 配置（base URL / key / model，支持环境变量与命令行）+ **命令行传入对话指令**，让模型自己选择并调用 MCP 工具，构成一个**可重复运行的自动化测试**（断言"模型确实调用了应用侧的 operator 并拿到结果"，而不是只断言"连上了"）。
7. **Task 13 的 WPF 侧开关**：给 `WpfAppOnlyDemo` 加 `--execution inprocess|external`（与 console 宿主一致），还是明确"WPF 侧固定 `InProcess`"并在文档写理由？**建议加开关** —— 桌面应用恰是最需要"生成代码不在自己进程里跑"的形态，而 operator 回环由 `SynchronizationContext` 保证，不受执行位置影响。
8. **Task 15 的 `.http` 落点**：仓库根 `samples/bridge-examples.http`（醒目、跨形态通用）还是应用侧样例目录内（与 T11 改名后的目录同处）？**建议后者**（命令与文件同处，避免"文件在别处"的困惑）。
9. **Task 5 的 `logStream` 口径机制（本轮核实后新增，实现前必须裁决）**：`AsonBridgeManifest.Capabilities` 由 `AsonBridgeRuntime` 从宿主配置产出，**运行时并不知道挂了哪些适配器**，所以"各适配器如实上报"没有现成承载点。两条路：
   **(a) 字段保持"运行时支持日志流"，真话由各适配器自己的表面承担** —— gRPC 有 `StreamExecution`、MCP 有无 `ason_stream_script` 工具、HTTP 有无 SSE 路由、OpenAPI 文档有无该 path（这些本来就是各协议的原生表达），文档写明"看你的传输提供了什么"。**建议 (a)**：不新增机制、无版本膨胀、每适配器各自可测。
   **(b) 适配器级能力覆盖层** —— 适配器在取 manifest 时传入覆盖集（新 API + 每适配器各渲染一份 manifest）。更"字面如实"，但新增契约面与版本同步成本，且同一应用对不同调用方给出不同 manifest，解释成本高。
10. **Task 11 的改名目标后缀（与 §9-5 同一议题）**：`ConsoleBridgeAppSample(Sample)` / `ConsoleBridgeCallerSample(Sample)` —— 建议保留 `Sample` 后缀以对齐 `ConsoleMcpSample` / `ConsoleExtractorSample` / `ConsoleAgentSample`（T14 的 `ConsoleMcpCallerSample` 同口径）。

---

## 10. 执行记录

### 10.1 计划文件的位置变更（本轮）

| 变更 | 原因 |
|---|---|
| 删除仓库根目录 `plan.md`（曾提交于 `744d7ca`） | 放错位置：本仓库的 eccplan 产物约定在 `.agents/plans/` |
| 本文件建立于 `.agents/plans/app-agent-separation.plan.md` | 与既有 `wpf-net10-net6-libdemo.plan.md` 同目录、同格式（`# Plan:` + Decision Log + 证据 + Patterns/Files/Tasks/Validation/Risks/Acceptance + 执行记录） |
| 此前临时写的 `.agents/plans/bridge-next-wave.plan.md` | 内容已并入本文件的 Wave 2，已删除，避免两份计划漂移（该文件未曾提交） |
| `docs/testing/app-agent-separation.tdd.md` 的 "Source plan" 指向本文件 | 计划 ↔ 证据互相引用 |

### 10.2 Wave 1 提交清单（供对照）

| 提交 | 内容 |
|---|---|
| `6c9452d` | Wave 1 RED：桥契约失败测试 |
| `0e9efc7` | 桥核心 GREEN（36/36，Ason 101/101，smoke 11/11） |
| `2ee8ee4` | 清理误提交的临时文件 |
| `afc74c2` | 传输缝 GREEN（`IRunnerTransport` 公开 + `UseTransport`） |
| `9c49c8c` | gRPC 适配器 GREEN（46/46） |
| `2b54c58` | MCP 适配器 + stdio 中继 + 外部 gRPC 客户端 |
| `90b2484` | 首版文档 + CI 接线 |
| `7a16497` | WPF 应用侧演示 + `AsonClientOptions.TransportFactory` |
| `4b94c9c` | WPF Agent 演示 + Agent/应用端到端验证（66/66） |
| `7834718` | OpenAPI 适配器 + Windows CI 作业（73/73） |
| `22a5ec5` | 中继 MCP↔MCP + MCP 可选参数真 bug 修复 + gRPC 错误路径（86/86） |
| `a54ff5a` | 三语文档补漏（配置项/拓扑/覆盖率命令） |
| `bd96f79` | 应用/Agent 流程图 `Case 1–5`（三语） |
| `c4991af` | 无 Agent 调用方文档 |
| `18b8613` | 每种形态↔样例↔运行命令映射 |
| `fb00b0f` | 跨平台 Agent 样例 + 外部脚本宿主模式 + `manifest.ToOperatorsLibrary()`（91/91） |

### 10.3 本轮计划修订（只改本文件，未执行任何实现）

| 变更 | 原因 |
|---|---|
| 新增缺口 **H** 与 Wave 2 **Task 10（T10）**：`Ason.Bridge.Grpc` 包内补齐 `ason_bridge.proto`（可选 opt-in gRPC 反射）+ 三语"非 .NET 调用方"小节 | 实测解包 `Ason.Bridge.Grpc.0.8.2.nupkg`：仅含 `lib/net9.0/Ason.Bridge.Grpc.dll` + `icon.png` + nuspec，无 proto；Python/Go/Java/Node 调用方无法从包生成客户端 |
| 复杂度/合计 9.5 → **10.0 人日**；依赖顺序把 T10 排在 **T3/T4 之后** | T3/T4 会改动 proto（`InvokeMcpTool`、`include_instance_declarations`），打包与文档必须跟随**最终契约**，否则要做两次 |
| 本轮**未**纳入计划：示例改名（`ConsoleGrpcBridgeHost`＝应用侧 / `ConsoleGrpcBridgeDemo`＝调用方，名字易反读）与"调用方需要知道/配置什么"对照表 | 首次修订只纳入"非 .NET 调用 gRPC"缺口；**随后你回复"纳入"，这两项已作为 T11 / T12 写入本文件** |
| 后续纳入（同一轮内追加）：缺口 **I / J** → **Task 11**（示例重命名 `ConsoleBridgeAppSample` / `ConsoleBridgeCallerSample` + 形态矩阵角色图例）、**Task 12**（三语新增"调用方接入：需要知道什么、需要配置什么"节）；复杂度合计 10.0 → **11.0 人日** | 你指示"纳入"；两项都是可用性/文档缺口，**无新增功能代码**（T11 纯重命名，T12 纯文档），因此不改动 §4 的功能面 |

### 10.4 本轮计划修订（示例覆盖度盘点 → 新增 T13/T14/T15；只改本文件，未执行任何实现）

| 变更 | 原因（含核实结论） |
|---|---|
| **先核实再纳入**：`fb00b0f` 已交付"分离部署 + `external` 执行位置" —— `ConsoleGrpcBridgeHost --execution inprocess\|external` + `ConsoleSamplesEndToEndTests` 断言 `external-process`；`ConsoleAgentSample` 也已存在 | 避免把已完成的工作重复写成任务；因此**不新增**"external 样例与 E2E"这一项 |
| 新增缺口 **K** → **Task 13**：执行位置收尾 —— ① WPF 应用侧 `--execution`（现写死 `InProcess`，`Bridge/BridgeHost.cs:49`）；② `remote-runner` 的**分离部署**进程级 E2E（现只有单进程覆盖）；③ `docker` 取值的如实上报与文档说明 | 盘点结论：文档承诺四种执行位置，但分离部署下只有 `inprocess`/`external` 有可跑形态；"应用决定隔离"这一半主张缺 `remote`/`docker` 的验证 |
| 新增缺口 **L** → **Task 14**：MCP 消费端接入 —— `samples/mcp/claude_desktop_config.json` + HTTP MCP 片段 + 最小消费端 + 三语文档 | 核实：全仓库无任何 MCP 客户端配置（`mcp.json`/`claude_desktop_config.json` 零匹配），文档仅一行命令行；对 stdio-only Agent 这是**唯一入口** |
| 新增缺口 **M** → **Task 15**：`samples/bridge-examples.http`（发现/执行/错误面/鉴权四组） | 核实：仓库已有 `.http` 惯例（`samples/RemoteRunnerService/*.http`、`tests/TestRemoteExecutorServer/*.http`），但桥没有任何 `.http`；文档只有零散 `curl` |
| 复杂度合计 11.0 → **13.5 人日** | T13 1.0 + T14 1.0 + T15 0.5 |
| 顺序约束写入 §5/§6：**T11 先于 T13**（否则样例命令行与 CI 清单改两遍）；**T1/T3/T5 先于 T15**（否则 `.http` 要补两次）；**T14 与 T10 互补可并行**；三者都在 **T9 之前** | 与前序任务的文件/契约重叠 |
| 决定**不动**（仅记录）：单进程四件套与模板已足够；分离的应用侧/调用方已各有 console + WPF 两种形态；跨平台已由 console 样例 + 中继在 Linux CI 覆盖；`Ason.Bridge.McpHost` 留在 `src/`（随包交付的可执行件，不再做成 sample）；无界面 console Agent 侧（`ConsoleAgentSample`）已交付 | 判据是"文档承诺了但用户无法一眼跑起来"，不是"哪条分支没覆盖" |

### 10.5 开工前的 Wave 2 冲突审计（本轮；只改本文件，未执行任何实现）



| 发现 | 性质 | 处理 |
|---|---|---|
| **T13③ 原写法不可实现**：`GetManifestAsync()` → `Executor.StartAsync()` → `ScriptRunnerProcessHost.StartAsync()` **当场 spawn**（Docker 模式即 `docker run`），所以"不启守护进程却断言上报 docker"做不到 | **计划缺陷（必须在实现前修正）** | 已改写 T13③：注入 `Name="docker"` 的桩执行器验证上报 + 真实 Docker 用例沿用 `DisplayName!~Docker` 排除口径 + 明确**不做**延迟启动的行为改动 |
| **T11 的 grep 验收会永远红**：旧名在 8 个文件 46 行里出现，其中 `docs/testing/app-agent-separation.tdd.md` 的 6 行是**历史事实记录**，重写即篡改证据 | 计划遗漏（缺豁免口径） | 已写入 T11：历史段落不重写 → 顶部加命名沿革注记；断言范围收窄到 `samples/**`、`sln`、`workflows`、`tests/**` 与两篇三语文档的正文命令块 |
| **命名口径不一**：T11 拟 `ConsoleBridgeAppSample`/`ConsoleBridgeCallerSample`（无 `Sample` 后缀）vs 既有 `ConsoleMcpSample`/`ConsoleExtractorSample`/`ConsoleAgentSample` 与 T14 拟新增的 `ConsoleMcpCallerSample` | 需裁决（§9-5/§9-10） | 建议统一保留 `Sample` 后缀（churn 最小） |
| **T13 的 remote E2E 无法直接用 `dotnet run`**：会触发构建且 CI Linux 清单里**没有** `RunnerServiceSample`，会导致该 E2E 在 CI 里因找不到产物而跳过 | 计划遗漏 | 已写入 T13②：测试启动**已构建产物**（定位 dll → `dotnet exec`）+ CI 构建清单补 `samples/RemoteRunnerService/RunnerServiceSample.csproj` |
| **T5 缺承载点**：manifest 的能力集由运行时产出，运行时不知道挂了哪些适配器，"各适配器如实上报 logStream"没有现成机制 | 需裁决（§9-9） | 已给出 (a)/(b) 两条路并建议 (a)：字段保留"运行时支持"，真话由各适配器的原生表面承担（gRPC rpc / MCP 工具 / HTTP 路由 / OpenAPI path） |
| 依赖顺序复核：T0 独立 ✓；T1 先于 T5/T15 ✓；T3+T4 合并为一次 proto 1.1 ✓；T10 在 T3/T4 后 ✓；T11 在 T13/T15 前 ✓；T14 与 T10 可并行 ✓；三者都在 T9 前 ✓ | 无冲突 | 保持现状 |
| 未纳入且**有意不做**：并发/多调用方压力场景（两个 Agent 同时打一个应用、实例 handle 抖动）没有任何任务覆盖；当前设计为"应用侧单实例目录 + `ConcurrentDictionary`" | 已知未测区域 | 记录为"风险知情不做"，若维护者要覆盖则另开任务 |
| 前置条件复核：T1 需要的 API 已核实 —— `HttpClientTransportOptions.AdditionalHeaders` 是 `IDictionary<string,string>`；`MapMcp(...)` 返回 `IEndpointConventionBuilder`（可 `.RequireAuthorization()`）；gRPC 侧用 `GrpcChannel.CreateCallInvoker().Intercept(...)` 注入 metadata | 无阻塞 | T1 可直接开工 |

### 10.6 Wave 2 执行记录（滚动更新）

| 任务 | 状态 | 证据（命令 + 结果） |
|---|---|---|
| T0 —— ASCII 流程图收尾 | ✅ DONE | 三语 `Case`/`情形`/`Caso` 计数一致（各 6 个）+ `git diff --name-only` 仅文档；`Case 1` 补 boundary A′ 全双向流（exec→invoke→invokeResult→execResult→经 D 回调用方）；新增 `Case 6`（remote runner，复用边界 B/C）；"operator 调用不跨越"已限定到调用方边界 D。`Case 7`（日志流）按计划留待 T5 |
| T1 —— 鉴权可达性 | ✅ DONE（待 T8 汇总） | RED：`CS1501`（`AddAsonGrpcBridge`/`AddAsonMcpBridge`/`GrpcAsonBridgeClient.Connect` 无二参重载）、`CS1503`（`McpAsonBridgeClient.ConnectAsync(endpoint, headers)`）→ GREEN：`tests/Ason.Bridge.Tests` **100/100**（91 + 9）；`Ason.Tests` 105/105；`samples/ConsoleGrpcBridgeHost` 仍构建（新参数可选，样例零改动）。实现要点：`AddAsonGrpcBridge(runtime, policy)` + `MapAsonGrpcBridge()` 读注册期选项；`AddAsonMcpBridge(endpoint, requireAuthorization)` + `MapAsonMcpBridge()`；客户端 `Connect(url, headers)`；中继 `--key`/`--header Name=Value`；**鉴权失败按状态抛出**（gRPC `Unauthenticated` 不被折叠成失败结果）。三语安全文档已补"要求调用方通过鉴权"小节 |
| 决策裁决（§10.7） | ✅ 已记录 | 1A / 2A（保留 `Sample` 后缀）/ 3B（+ Python gRPC 示例）/ 4B（Python + OpenAI 驱动自动调用测试）/ 5 = `0.9.0` + 更新仓库版本号；T5/T10/T14/T7 已按裁决改写 |
| T7（部分）—— 版本号更新 | ✅ DONE（release notes 留到 T7 完整执行） | `Directory.Build.props` `0.8.2` → **`0.9.0`**；文档里写死的镜像 tag 一并更新（`README.{md,zh-CN,es}` + `docs/execution-modes.{md,zh-CN,es}` 的 `ghcr.io/alexgoon/ason:0.8.1` → `:0.9.0`，与 `publish-docker.yml` 按 tag 出镜像的口径一致）。验证：`dotnet build src/Ason.Bridge -c Release` → 产出 `Ason.Bridge.0.9.0.nupkg`；全仓库剩余 `0.8.x` 仅存在于历史记录（本计划 §10 与另一份 Wave 1 计划） |
| T3 + T4 —— MCP 透传进入 gRPC/MCP + 实例声明鲜度（合并为一次 proto 1.1 契约变更） | ✅ DONE | RED：`CS0246 InvokeMcpToolRequest`、`CS1061`（`AsonBridgeClient.InvokeMcpToolAsync`／`GrpcAsonBridgeClient`／`McpAsonBridgeClient`／`AsonBridgeManifest.InstancesRevision`／`GrpcAsonBridgeTransport.Proxies` 不存在）→ GREEN：`tests/Ason.Bridge.Tests` **115/115**（100 + 9 `McpPassthroughTests` + 6 `InstanceFreshnessTests`）；`Ason.Tests` 105/105；smoke 11/11；`dotnet build Ason.sln -c Release` 全绿（顺带修掉 T1 遗留：`samples/WpfAgentDemo/Bridge/AgentBridge.cs:53` 把 `cancellationToken` 传给了 `ConnectAsync` 的 `headers` 形参——**样例未在测试项目里编译，所以之前没暴露**，已改为具名实参） |
| T3 实现要点 | ✅ | ① proto 增 `rpc InvokeMcpTool` + `InvokeMcpToolRequest{server,tool,arguments_json}`（工具参数是 JSON**对象**，与服务端 `ParseArgumentObject` 对应；`invokeFunction` 仍是 JSON 数组，两者不冲突）。② `AsonBridgeRuntime.InvokeMcpToolAsync`：能力关闭 → `not-supported`（各适配器映射为 `Unimplemented`／不注册工具）；**能力开启但 `IAsonExecutor.McpServers` 为空 → `not-supported` 并点名 MCP**（新增 `IAsonExecutor.McpServers` 默认成员 + `RunnerClientAsonExecutor.McpServers` 读 `RunnerClient.McpServerNames`，`Ason` 侧只加了一个只读属性）。③ MCP 工具 `ason_invoke_mcp_tool` 仅在能力开启时注册；客户端 `InvokeMcpToolAsync` 在工具不存在时回 `not-supported`（判据是 `tools/list` 里没有该工具，而不是解析错误文本——工具自身执行失败仍照常抛出）。④ `GrpcAsonBridgeEndpoint`／`McpAsonBridgeEndpoint` 由本地拒绝改为**转发**，中继因此自动继承远端能力 |
| T4 实现要点 | ✅ | ① `AsonBridgeManifest.InstancesRevision`（实例集合的 SHA256 前 16 位；同集合稳定、增删即变）+ proto `ManifestReply.instances_revision`。② `includeInstanceDeclarations`（runtime/gRPC proto/MCP 工具/HTTP body/四适配器与类型化客户端全通）：**只发 body** 模式，由应用拼上“代理层 + 当前实例声明”，因此调用方快照过期也能跑；默认 `false`，Wave 1 行为逐字不变。③ `GrpcAsonBridgeTransport`／`McpAsonBridgeTransport` 新增 `Proxies`（= `manifest.Proxies`）：设置后把调用方快照层**精确剥离**再请求应用重建，零额外往返（`InstanceFreshnessTests.The_runner_transport_hands_the_callers_snapshot_layer_back_to_the_application` 同时断言“不设置=旧行为失败／设置=成功”）。④ 协议版本 `1.0` → **`1.1`**（纯追加，1.0 客户端仍可用，已写进三语文档与 `AsonBridgeProtocol` 注释） |
| T3/T4 文档 | ✅ | 三语 `docs/app-agent-separation.*`：manifest 表补 `instancesRevision` 与 1.1 兼容说明；能力表 `invokeMcpTool` 补 gRPC/MCP 表面；“两种拒绝”清单（能力关闭 vs 未注册 MCP 服务）；新增“实例鲜度与清单过期／Live instances and manifest freshness”整节；gRPC 小节补 proto 契约字段；已知限制改写（删除“gRPC 契约不含 MCP 透传”，新增“透传不镜像工具列表”这一真实限制）；安全小节补透传承载应用凭据的说明 |
| T10 —— 非 .NET 调用 gRPC（proto 投递 + 反射 opt-in + Python 示例） | ✅ DONE（Python 已端到端实跑，见"补跑"） | RED：`CS1501 AddAsonGrpcBridge 无 3 参重载`、`CS1501 MapAsonGrpcBridge 无 1 参重载` → GREEN：`tests/Ason.Bridge.Tests` **119/119**（115 + 4 `ReflectionAndContractTests`）。① 打包：`Ason.Bridge.Grpc.csproj` 增 `<None Include="Protos\ason_bridge.proto" Pack="true" PackagePath="protos\" />`；实测 `dotnet pack -c Release -o ./artifacts/pack` 后解包列出 `protos/ason_bridge.proto`（4563 bytes）——nupkg 内容：`lib/net9.0/Ason.Bridge.Grpc.dll`、`icon.png`、`protos/ason_bridge.proto`。② 反射：新增 `Grpc.AspNetCore.Server.Reflection` 2.71.0；`AddAsonGrpcBridge(runtime, policy, enableReflection)` + `MapAsonGrpcBridge(bool?)`（映射期可覆盖），**默认关闭**；开启时反射端点**继承同一鉴权策略**（测试按端点元数据断言 `IAuthorizeData.Policy`；反射服务实际映射 v1 与 v1alpha 两个端点，测试用 `Assert.All`）。样例 `--reflection` 已接。③ 三语文档新增"非 .NET 调用方"节：两条 proto 获取路径（`GeneratePathProperty` → `$(PkgAson_Bridge_Grpc)\protos\...`、`unzip 'protos/*'`）、`python -m grpc_tools.protoc`／`protoc --go_out/--java_out`／`grpcurl -proto` 三例、服务与方法全名（含 `InvokeMcpTool`）、"能调什么仍以 manifest 为准"的边界。④ Python 示例：`samples/python/{ason_bridge_client.py,requirements.txt,README.md}`（首次运行自动 `grpc_tools.protoc` 生成到 `.gen/`；子命令 `manifest/instances/call/script [--stream]/mcp`；`--url/--proto/--header`） |
| 补跑（T10/T14 的 Python 端到端；本轮追加） | ✅ DONE，并**修掉 3 个真实缺陷** | 首轮因 `files.pythonhosted.org` 读超时只做了静态验证；改用**清华镜像**成功安装（`python -m pip install --user -i https://pypi.tuna.tsinghua.edu.cn/simple grpcio grpcio-tools` → grpcio/grpcio-tools 1.84.0 + protobuf 7.36.2；系统目录无写权限，故需 `--user`）。真跑一遍抓出静态检查抓不到的问题：① `ason_bridge_client.py` 把 `call Operator Method` 声明成两个位置参数，而所有文档示例都用点号形式 `Operator.Method` → **argparse 直接拒绝文档里的命令**，现两种写法都支持；② `ensure_stubs()` 在 `.gen/` 已最新时提前返回却**没有把 `.gen` 加进 `sys.path`** → 第二次运行必 `ModuleNotFoundError: ason_bridge_pb2`，现两条分支都加；③ `samples/mcp/claude_desktop_config.json` 与 README 里的中继路径写成 `src/Ason.Bridge.McpHost/bin/Release/net9.0/`，但本仓库项目共用 `src/bin` 输出根 → 正确路径是 `src/bin/Release/net9.0/Ason.Bridge.McpHost.dll`，已改并让 `McpClientConfigTests` 断言 `src/bin/`。补跑结果（对已构建的 `ConsoleBridgeAppSample`）：gRPC `manifest`（protocol `1.1`）、`call LibDemoStaticOperator.Add` → **42**、`script … --stream` → 日志 + **42**；MCP over HTTP `--list` → **6 个工具**、`--call ason_invoke_function` → **42**；MCP over stdio（真起中继进程）→ **6 个工具** 且 **42**。另：`--args` 新增 `@file.json` 形式（Windows PowerShell 会改写参数内引号，`--args @args.json` 一定完整送达） |
| T5 —— `logStream` 口径落地（HTTP SSE + MCP 工具） | ✅ DONE | RED：`McpAsonBridgeClient.StreamScriptAsync` 不存在 → GREEN：**126/126**（+7 `LogStreamingTests`）。① 口径 (a) 写进 `AsonBridgeCapabilities.LogStream` 注释（运行时能力；各适配器原生表面负责"给不给"）。② HTTP：`POST {base}/script/stream`（SSE：`log* → result\|error`），用 `Channel` + 并发 pump 复刻 gRPC `StreamExecution` 的写法（避免 log 回调里 sync-over-async），受 `ApiKey` 与 `ExecuteScript`+`LogStream` 双重门控；OpenAPI 文档新增该 path（`text/event-stream` 响应）。③ MCP：`ason_stream_script` 工具（`{success,result,error,errorCode,logs}`，因 MCP 工具调用不能推送；stdio/HTTP 一致性天然成立），客户端 `StreamScriptAsync` 返回新的核心类型 `AsonBridgeStreamedResult`。④ **中继取应用的日志而不是自己的**：`IAsonBridgeEndpoint.ExecuteScriptWithLogsAsync` 默认实现（订阅 `Log`），`GrpcAsonBridgeEndpoint` 覆写为 `StreamExecution` 收集、`McpAsonBridgeEndpoint` 覆写为远端 `ason_stream_script`（远端不具备时回退到普通调用且如实返回空日志）——两条中继路径都有测试。⑤ 三语"看着脚本跑起来"节 + `Case 7` 图 |
| T11 —— 示例重命名 + 角色图例 | ✅ DONE | `git mv` 两个样例目录与 csproj：`samples/ConsoleGrpcBridgeHost` → `samples/ConsoleBridgeAppSample`（工程同名 csproj）、`samples/ConsoleGrpcBridgeDemo` → `samples/ConsoleBridgeCallerSample`；同步 `Ason.sln`、`.github/workflows/ci.yml`、三语 `docs/app-agent-separation.*`（含样例矩阵的角色图例 + 新增"另一种语言的调用方 = `samples/python`"一行）、三语 `docs/contributing.*`、样例自身注释、`tests/Ason.Bridge.Tests/TestSupport/{ConsoleBridgeHost,RequiresConsoleSamplesFactAttribute}.cs`、`samples/python/README.md`。**豁免按计划执行**：`docs/testing/app-agent-separation.tdd.md` 历史段落不重写，改为在文件顶部加"命名沿革"注记；本计划 §10 历史行与 git 历史不计。断言口径（已写进 §6）：只扫 `samples/**`、`Ason.sln`、`.github/workflows/**`、`tests/**`、三语 `docs/app-agent-separation.*` 与 `docs/contributing.*` 正文 → **NO-LEFTOVER-REFERENCES**（越界命中仅剩 `.agents/plans/...`（§2.4/§4/T11 任务文本本身在描述重命名）与 `docs/testing/...tdd.md`（历史证据））。验证：两个新 csproj `-t:Rebuild` 通过；`ConsoleSamplesEndToEndTests` **5/5 通过**（重命名后跑通的进程级 E2E）；全量桥测试 **126/126**。踩坑记录：`git mv` 后**增量构建**因旧 obj 缓存不再产出 dll（0 error 但 bin 为空），`-t:Rebuild` 后正常 —— CI 全新检出不受影响，但本地改名后请 Rebuild |
| T13 —— 分离部署执行位置收尾 | ✅ DONE | ① WPF 应用侧：`WpfAppOnlyDemo --bridge-only --execution inprocess\|external`（`App.xaml.cs` 解析 + `BridgeHost.Start(..., execution)`，`BridgeHost.Execution` 暴露；README 行/`ASON_BRIDGE_READY` 带 `execution=`）；csproj 补 `Ason.ExternalExecutor` 引用 + host 文件拷贝目标（否则 `external` 找不到子执行器）。新增测试 `WpfApplicationEndToEndTests.The_wpf_application_can_evaluate_scripts_in_a_child_process_and_still_keep_ui_affinity`：manifest `external-process` + 脚本在子进程求值 + operator 回环仍 `onUiThread=true`。② `remote-runner` 进程级 E2E：样例补 `--execution remote --remote-url`（`ASON_BRIDGE_REMOTE_URL` 等价，缺参退出码 2）；新增 `TestSupport/RemoteRunnerHost.cs`（定位已构建 dll → `dotnet exec` + `ASPNETCORE_URLS`/`ASPNETCORE_ENVIRONMENT=Development`，用 `/openapi/v1.json` 作就绪探测，**不用 `dotnet run`**）与 `RemoteRunnerBridgeEndToEndTests`（断言 `execution=remote-runner`、脚本经 SignalR 宿主求值、函数调用仍在应用侧）。**踩坑（真实缺陷）**：`RunnerServiceSample` 输出目录里没有 `Ason.ExternalExecutor.*`，导致远程宿主无法 spawn 子执行器、脚本调用**永久挂起**（首轮 14 分钟无产出）→ 给 `RunnerServiceSample.csproj` 补引用 + host 文件拷贝目标后通过；`RunnerServiceSample.csproj` 已加入 Linux CI 构建清单。③ `docker` 取值：按修订口径用**注入执行器**验证上报链路（`ExecutionReportingTests`：`FakeAsonExecutor{Name="docker"}` → runtime/gRPC/MCP/HTTP 四处 manifest 一致报 `docker`；另有 4 个 `InlineData` 断言 `AsonBridgeExecution` 四种取值 → `RunnerClientAsonExecutor.Name` 的映射），真实 Docker 仍需守护进程、沿用既有排除口径，不做"延迟启动执行器"的行为改动 |
| T14 —— MCP 示例 + Python 消费端 + OpenAI 自动调用 | ✅ DONE（MCP 消费端已端到端实跑；agent 需密钥） | ① `samples/mcp/claude_desktop_config.json`（stdio 中继，`<repo>` 占位，注释说明 `--key`/`--header`）与 `samples/mcp/http_mcp_config.json`（HTTP MCP 等价片段）。② `samples/mcp/README.md`：三步（先起应用侧 → 客户端配置 → 工具清单表），列出全部 7 个工具名、"能力关闭=没有该工具"的口径、无 key/有 key 两种写法，并写明中继 DLL 的真实输出位置。③ `samples/python/ason_mcp_caller/main.py`：**仅标准库**的最小 MCP 客户端（stdio：`dotnet exec` 启动中继 + 逐行 JSON-RPC；HTTP：Streamable HTTP POST + `Mcp-Session-Id` + `text/event-stream` 解析），`initialize` → `tools/list` → `tools/call`，CLI `--transport/--list/--call/--args/--key/--header`。④ `samples/python/ason_mcp_agent/main.py`：**OpenAI function calling 驱动** —— `tools/list` 得到的 MCP 工具直接作为 `tools=[...]` 喂给模型，模型自选工具与参数，执行后回灌，直到给出最终答复（`--max-rounds` 保护）；配置 `--base-url/--model/--api-key` 与 `MY_OPEN_AI_BASE_URL/MY_OPEN_AI_MODEL/MY_OPEN_AI_KEY` 等价，**未配置密钥时退出码 2 并明确报错**；`--instruction` 可重复，`--expect` 逐条断言"模型真的调用了 operator 且结果出现"。⑤ 可跑的 .NET 侧断言 `McpClientConfigTests`（4 条）：两份配置是严格 JSON、stdio 配置 `command=dotnet`/`exec`/`<repo>` 占位/`src/bin/` 路径/`--url` 指向应用；HTTP 配置指向 `/mcp`；README 覆盖全部工具名与 `--key`/`--header`；两个脚本与 requirements 文件存在且 agent 含 `MY_OPEN_AI_KEY`/`--instruction`/`return 2`。⑥ 三语样例矩阵新增两行（stdio/HTTP 客户端配置、模型驱动工具调用的自动化测试）+ MCP 小节补 `samples/mcp` 指路。**实跑**：`ason_mcp_caller` 两种传输都对真应用跑通（HTTP 与 stdio 各 6 个工具、`ason_invoke_function` → 42，见 T10 的"补跑"行）；`ason_mcp_agent` 未跑（需真实模型密钥），其"无密钥退码 2"行为已实测 |
| T12 —— 调用方接入矩阵 | ✅ DONE | 三语新增 "调用方接入：需要知道什么、需要配置什么 / What a caller has to know and configure / Qué necesita saber y configurar un llamador" 整节：① 形态表（HTTP+OpenAPI ✅ 一个 URL；MCP-HTTP ✅ 一个 URL；gRPC .NET ⚠️ 需客户端包装；gRPC 非 .NET ⚠️ 需契约，反射可免；stdio MCP ❌ 必须写启动命令），三列＝调用方准备／应用侧配置／能否零配置；② 三条前提（无注册中心-mDNS-自动发现；handle 是运行期状态；`proxies` 是快照）；③ 两个"只能读不能改"的开关（capabilities、authorization）与各自的失败形状；④ 交叉链接：`docs/architecture.*` 边界 D 段、`docs/index.*` 新增一行、`docs/execution-modes.*` 第三轴段 |
| T15 —— `bridge-examples.http` | ✅ DONE | 新增 `samples/bridge-examples.http`（四组：发现／执行／错误面／鉴权，外加第 5 组 SSE 流；`@base=5223`、`@key=` 留空），文件头写明先起应用侧样例。**逐条实跑并记录真实响应**（对已构建的 `ConsoleBridgeAppSample` + curl）：manifest/instances/openapi.json → **200**；`POST /ason/script` → **200** `{"success":true,"result":42}`；静态模块 `functions/invoke` → **200** `42`；未知 operator → **400** `operator-not-found`；path 形式 → **200** `42`；body-only（`includeInstanceDeclarations:true`）→ **200** `3`；空 `code` → **400** `invalid-arguments`；`POST /ason/script/stream` → **200** 且依次收到 2 条 `event: log` 与 1 条 `event: result`。坑：样例的 HTTP/OpenAPI 路由挂在 **MCP 端口（port+1）**，打到 gRPC 端口会得到 "An HTTP/1.x request was sent to an HTTP/2 only endpoint"；PowerShell 直传内嵌引号给 `curl.exe` 会被改写，需用 `--data-binary @file`。三语文档的 curl 片段已补指向该文件 |
| T6 —— 覆盖率 | ✅ DONE（门禁并入 T8） | 命令：`dotnet test tests/Ason.Bridge.Tests -c Release --collect:"XPlat Code Coverage" --settings coverlet.runsettings`。结果（四个适配器，行／分支）：`Ason.Bridge` **89.4% / 82.3%**、`Ason.Bridge.Grpc` **93.6% / 75.0%**、`Ason.Bridge.Mcp` **95.6% / 79.3%**、`Ason.Bridge.OpenApi` **98.3% / 80.4%** —— 全部达到 **行 ≥87%、分支 ≥70%**。手段：新增两批分支测试（`AdapterBranchTests` 14 条、`McpBranchTests` 7 条）专打"正常部署走不到"的路径（转发端点全量、runner 传输收到不该来的消息、注册守卫、线级参数错误、可选/缺失工具参数），使 Mcp 分支从 57.8%→79.3%、Grpc 从 66.5%→75.0%。**样例程序集不设门禁**（每个样例是独立进程，coverlet 在测试宿主内采集，跨进程不可得），其覆盖口径改为进程级 E2E（console 5 + WPF 3 + remote-runner 1 + relay/agent 若干），已写进三语 contributing 与证据报告。**FlaUI 决策**：接入 Windows 作业并 `continue-on-error: true`（观察不阻断），理由与替代证据写入 CI 注释、三语 contributing 与证据报告 |
| T7 —— 发布卫生 | ✅ DONE | `Directory.Build.props` 版本 `0.9.0`（前一轮已改）；新增 `CHANGELOG.md`（英文，一条 `0.9.0`：四个新包与各自职责 + 桥的能力清单 + 追加式 API 清单（`IRunnerTransport`／`RunnerClient.OperatorInstances`／`McpServerNames`／`KeywordScriptValidator`／`IsAttached`／`TransportFactory`／`ToOperatorsLibrary`／鉴权与反射重载／客户端 `headers`／中继 `--key`-`--header`／`McpServers`／`AsonBridgeStreamedResult`／样例改名）+ "无破坏性变更"确认 + 1.0/1.1 兼容说明）；`dotnet pack -c Release` 产出 8 个包的 `0.9.0`（`Ason`、`Ason.Abstractions`、`Ason.Runner.Core`、`Ason.ExternalExecutor`、`Ason.RemoteBridge`、`Ason.Bridge`、`Ason.Bridge.Grpc`、`Ason.Bridge.Mcp`、`Ason.Bridge.OpenApi`），`Ason.Bridge.McpHost` 为样例 exe 不打包 |
| T8 —— CI 加固 | ✅ DONE | `.github/workflows/ci.yml` ① Linux：桥测试步骤改为**带覆盖率采集**（`--collect` + `coverlet.runsettings` + `--results-directory artifacts/coverage`），随后 `scripts/check-bridge-coverage.ps1`（`shell: pwsh`，逐适配器断言行 ≥87%／分支 ≥70%，脚本本地实跑通过）；② 新增"契约随包发布"步骤：`dotnet pack Ason.Bridge.Grpc` 后 `unzip -l … \| grep -q 'protos/ason_bridge.proto'`；③ Windows 作业新增 `tests/Ason.Tests`（hermetic 过滤口径）与以 `continue-on-error: true` 运行的 FlaUI UI 测试，并写明 net10.0 分支待 GA 的原因；④ `RunnerServiceSample.csproj` 加入 Linux 构建清单（否则 remote-runner E2E 会跳过） |
| T9 —— 文档与证据收尾 | ✅ DONE | `docs/testing/app-agent-separation.tdd.md` 新增 **Wave 2 report（T0–T15）**：执行摘要表、验证命令与结果、覆盖率表与手段、逐项保证→证据映射、**诚实限制**（Python 端到端未跑：PyPI 不可达；真实 Docker 需守护进程、只断言上报；FlaUI 观察不阻断；反射默认关闭且继承鉴权）、缺口清单（Wave 1 的 A–M 全部关闭）与 Wave 2 检查点提交表；三语 `docs/contributing.*` 补覆盖率门禁脚本、样例覆盖口径、CI 现状与新环境变量；三语 `docs/app-agent-separation.*` 覆盖全部 Wave 2 契约变化；`docs/index.*`、`docs/architecture.*`、`docs/execution-modes.*` 交叉链接同步 |
| 补跑（T14 的 OpenAI 驱动自动调用；本轮追加） | ✅ DONE，并修掉 2 个真实缺陷 | 用**真实模型**（OpenAI 兼容端点，`--base-url`/`--model` 指向国内服务）把 `ason_mcp_agent` 端到端跑通，四个用例全部 `exit=0` 且都以 `--expect 42` 断言"应用真的算了 40+2"：① 指令点名 operator → 模型调 `ason_invoke_function(LibDemoStaticOperator.Add,[40,2])` → 42；② 指令不点名（要求自行发现）→ 模型先 `ason_get_script_api` 读出 operator，再调用 → 42；③ **走 stdio** 且先撞错 → `NoSuchOperator` 得到 `operator-not-found` → 模型读 `ason_get_manifest` → 换对 operator → 42（证明错误回灌与自我纠正有效）；④ 中文指令 → 中文回答"40 加 2 的结果是 42。"。真跑抓出的两个缺陷（静态检查抓不到）：① `run_instruction()` 把同一个 `client` 参数既当 OpenAI 客户端又当 MCP 传输 → 模型选中工具后 `AttributeError: 'dict' object has no attribute 'headers'`，现拆成 `llm`/`mcp` 两个参数并写明原因；② 本机 Python stdout 默认 **GBK**，中文回答/中文 operator 描述会 `UnicodeEncodeError`，三个脚本现统一在启动时把 stdout/stderr 强制为 UTF-8。**密钥仅通过本次运行的进程环境变量传入，未写入仓库、PR 描述或证据报告任何位置**（已 `git grep` 复核） |
| 最终验证矩阵（本机 Windows / .NET SDK 9.0.306） | ✅ 全绿 | `dotnet build Ason.sln -c Release` **0 errors**；`Ason.Bridge.Tests` **158/158**；`Ason.Tests`（hermetic 过滤）**105/105**；`LibDemo.SmokeTests` net9.0 **11/11**、net6.0 **11/11**；`Ason.Runner.Tests` **1/1**；`Ason.RemoteRunner.Tests` **1/1（1 skipped：需活动 runner host）**；覆盖率门禁脚本 4/4 包达标；`dotnet pack` 后 nupkg 内含 `protos/ason_bridge.proto`；`bridge-examples.http` 的 8 条请求逐条实跑并记录真实响应码 |
| 下一步 | ✅ Wave 2 全部任务（T0–T15）已完成 | 仅剩"计划外"的可选项：在有 Docker 守护进程的机器上跑 Docker 用例；把 FlaUI 从观察改为门禁 |

### 10.7 决策裁决记录（你已拍板；T0/T1 已按此开工）

| 决策点 | 裁决 | 对任务的影响 |
|---|---|---|
| §9-1 版本号 | **`0.9.0`**，并要求**更新仓库内对应版本号** | T7 ① 扩写：`Directory.Build.props` + 文档里写死的版本引用（含 Docker 镜像 tag 与发布流程一致性）+ README 版本提及 |
| §9-9 `logStream` 口径 | **(a)**：字段保留"运行时支持"，真话由各适配器原生表面承担 | T5 ① 定稿（不新增"按适配器改写 manifest"的机制）；实现仍是 HTTP SSE + MCP `ason_stream_script` 工具 |
| §9-4 gRPC 反射 | **opt-in、默认关闭**，并**追加要求：交付 Python 的 gRPC 调用示例** | T10 ② 定稿；T10 **新增 ④**（`samples/python/` 下可运行脚本 + `requirements.txt` + 三命令文档） |
| §9-5/§9-10 改名 | **保留 `Sample` 后缀**：`ConsoleBridgeAppSample` / `ConsoleBridgeCallerSample` | 本文件已全量替换该命名（§4/§5/§6 同步） |
| §9-6 最小 MCP 消费端 | **Python**；并**追加要求**：用 OpenAI 官方库 + 配置 + 命令行指令，实现"模型自动选择并调用 MCP 工具"的**自动化测试** | T14 重写（1.0 → **1.5 人日**）：③ Python 最小客户端；④ OpenAI 驱动自动调用测试（断言"真的调了 operator 且状态变了"，带轮数/超时保护；无 key 明确报错退出） |
| §9-2 T5 实现形式（progress vs 工具）、§9-3 FlaUI、§9-7 WPF 开关、§9-8 `.http` 落点 | 仍待你裁决（不挡当前顺序） | 到对应任务时再问；默认按计划里的建议执行 |

### 10.8 CI 失败复盘与"可匿名读取的失败"（本轮；不属于 Wave 2 任务，是你选定的 B 选项授权的修复）

| 项 | 内容（含核实结论） |
|---|---|
| 现象 | `main` 上最近两次运行（`6b5194f`、`f69f9a4`）同一处红：job `Build & unit tests`（ubuntu）的 `Unit tests` 步骤 `dotnet test tests/Ason.Tests` 失败；其后 `Bridge tests (with coverage)`、`Coverage floor (bridge adapters)`、`The contract ships with the package` **全部 skipped**。 |
| 附带结论（比这次红更严重） | 作业在 `Unit tests` 就中止，因此**桥测试在 Ubuntu 上从未真正执行过**。"桥在 Linux 上可用"此前只有 Windows 作业的证据。 |
| 为什么只能"推"不能"读" | 匿名可读：`api.github.com/repos/.../check-runs/<id>/annotations` ✅；作业日志 ❌ `403 Must have admin rights`（HTML／代理／Jina 抓取均失败）。所以只能从"哪一步失败"倒推，再到本机复现根因。 |
| 根因 | `tests/Ason.Tests/Client/AnswerLanguageTests.cs` 的 `A_well_formed_but_unregistered_language_is_passed_through` 断言 `Assert.Contains("in zz", directive)`；该子串来自 `CultureInfo.GetCultureInfo("zz").EnglishName`，由运行时 globalization 提供：本机默认 → `zz`；`DOTNET_SYSTEM_GLOBALIZATION_USENLS=1` → `Unknown Locale (zz)`（本机实测 1 failed / 104 passed）；Linux runner 的 ICU → 预期 `Unknown Language (zz)`（**未在本机验证**，但该测试自身的注释已预言这一差异）。结论：**是平台差异，不是回归**。 |
| 修复（`4fa33c0`） | 断言收敛到库真正承诺的内容：`StartsWith("Language rule")`、`Contains("(zz)")`（标签原样进入提示词）、`Contains("instead of copying")`（规则正文完整）、`DoesNotContain("zz / zz")`；注释写明显示名由运行时提供。验证：默认 **105/105**、强制 NLS **105/105**。 |
| 可匿名读取的失败（`708712b`） | 新增 `scripts/emit-test-failures.ps1`：TRX → `::error title=<测试名>::<断言 + 行号>`，`%` 与换行按 GitHub 注解规则转义，永不使作业失败，纯 ASCII（PS 5.1 亦可解析）；两个作业的每个 `dotnet test` 均写 TRX，末尾各加一个 `if: failure()` + `shell: pwsh` 的注解步骤。三语 `docs/contributing.*` 记录了该机制与用途。 |
| 本机验证 | 红样本 TRX → 输出 `::error title=Ason.Tests.Client.AnswerLanguageTests.A_well_formed_but_unregistered_language_is_passed_through::Assert.Contains() Failure: Sub-string not found ... Not found: "in zz" ... line 57`（退码 0）；绿样本 TRX → `No failed tests in the TRX files.`（退码 0）；`ci.yml` 经 PyYAML 解析通过，两个作业的注解步骤都是最后一步且带 `if: failure()`。 |
| 下一步（待观察） | 推送后 Ubuntu 作业将**首次**执行桥测试：WPF 端到端按 `OperatingSystem.IsWindows()` 跳过，console 与 remote-runner 端到端用"定位已构建产物 → `dotnet exec`"启动（理论跨平台，**尚无 Linux 实测**）；若红，注解会直接给出测试名与断言。 |
