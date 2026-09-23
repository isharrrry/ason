# Plan: ASON Bridge —— 应用 / Agent 分离（Wave 1 已交付 · Wave 2 待执行）

**Created**: 2026-09-23
**Mode**: conversational `/plan`（eccplan → `references/commands/plan.md`）
**Branch**: `feat/agent-app-separation-bridge`
**Baseline**: `fb00b0f`（撰写本文件时的 HEAD，工作树干净）
**Complexity**: Large（Wave 1 已落地 5 个新项目 + 5 个样例 + 91 个桥测试；Wave 2 约 11.0 人日）
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
| 目标版本号 | **待定**（建议 `0.9.0`） | `Ason` 的改动全是追加式 API，至少 minor（→ §9 待裁决） |

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
| `samples/ConsoleGrpcBridgeHost/**` → `samples/ConsoleBridgeApp/**`、`samples/ConsoleGrpcBridgeDemo/**` → `samples/ConsoleBridgeCaller/**` | RENAME/CREATE/DELETE | **T11** 消除"Host/Demo 谁是哪一侧"的反读；示例非公开 API，纯重命名 |
| `Ason.sln`、`.github/workflows/ci.yml`、`docs/**`、`README.*`、`.agents/plans/**`、`docs/testing/**`、`tests/Ason.Bridge.Tests/TestSupport/**` | UPDATE | **T11** 重命名后的全量引用更新（含 CI 构建清单、三语命令块、E2E 助手与测试定位） |
| `tests/Ason.Bridge.Tests/*` | CREATE/UPDATE | T1–T5 的新测试；T4 鲜度测试；T2 流式测试 |
| `coverlet.runsettings`、`.github/workflows/ci.yml` | UPDATE | T5 样例覆盖率采集；T7 门禁与 Windows 作业补测；**T10 打包校验**（pack 后断言 nupkg 内含 proto） |
| `Directory.Build.props` | UPDATE | T6 版本递增 |
| `docs/app-agent-separation.*`、`docs/configuration.*`、`docs/contributing.*`、`docs/architecture.*`、`README.*` | UPDATE | T0 图 + T1/T2/T4 的能力与安全说明（三语） |
| `docs/testing/app-agent-separation.tdd.md` | UPDATE | T8 增补 "Wave 2" 章节 + 覆盖率表 + 缺口更新 |

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
- **Action**: ① `Capabilities.LogStream` 反映**适配器实际提供**的能力（适配器覆盖上报并加注释："这是端到端可见性，不是运行时开关"）。② HTTP：`POST {base}/script/stream`（SSE，`log* → result|error`），受 T1 授权约束。③ MCP：`ason_stream_script` 工具（或 SDK progress notification，需先在 stdio/HTTP 下验证一致）。④ 三语能力表补"各适配器如何拿到日志"。
- **Mirror**: `GrpcAsonBridgeService.StreamExecution`（channel + `runtime.Log`，复用事件形状）。
- **Validate**: `OpenApiStreamingTests`、`McpStreamingTests`、`CapabilityTests`（关闭 executeScript 时 stream 也不可用）+ 全量套件。
- **Risk**: 中（SSE 缓冲/代理；MCP progress 的传输差异）。

#### Task 6（T5）—— 覆盖率（测试）· 1.5 天
- **Action**: ① 逐条补候选分支：MCP 取消脚本、空参数调用、**每个适配器**能力关闭、中继 `--transport` 非法值、OpenAPI `handle` 经 query、流式错误事件、客户端自有 channel 释放（核对）。② 样例程序集覆盖率采集（console 三件套，**不设门禁**）。③ UI 自动化决策落地：FlaUI 接入并 `continue-on-error`，或明确"不接入 + 理由"（二选一，必须写进证据报告）。
- **Mirror**: `GrpcErrorPathTests` 命名与结构；`coverlet.runsettings`。
- **Validate**: `dotnet test … --collect:"XPlat Code Coverage" --settings coverlet.runsettings`；目标：四适配器**行 ≥87% 保持、分支 ≥70%**（`.Grpc` 从 60.5% 起）。
- **Risk**: 低（若某分支确实不可达，在报告里说明而不是硬凑）。

#### Task 7（T6）—— 发布卫生（发布）· 0.5 天
- **Action**: ① `Directory.Build.props` 版本由 `0.8.2` 递增（建议 `0.9.0`）。② release notes（先核对仓库既有位置）：新包 `Ason.Bridge` / `.Grpc` / `.Mcp` / `.OpenApi`（`Ason.Bridge.McpHost` 为样例 exe 不打包）、追加式 API 清单（`IRunnerTransport` 公开、`OperatorInstances` 公开、`KeywordScriptValidator` 公开、`IsAttached`、`TransportFactory`、`AsonBridgeAgent.ToOperatorsLibrary`）、"无破坏性变更"确认。③ 核对 `publish-nuget.yml` 顺序与依赖。④ 核对 README 版本引用。
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
  ② **可选反射**：`Grpc.AspNetCore.Server.Reflection` + `MapGrpcReflectionService()`，以
  `MapAsonGrpcBridge(enableReflection: true)`（或样例 `--reflection`）**opt-in**，让 `grpcurl` 与非 .NET
  调用方无需本地 proto 即可调用；**默认关闭**（反射等于把可调用面再对外广播一次，须与 T1 的鉴权一起评估）。
  ③ **三语文档**：`docs/app-agent-separation.*` 新增"非 .NET 调用方"小节 —— proto 的两条获取路径
  （NuGet 包内 `protos/ason_bridge.proto`，可用 `GeneratePathProperty=true` 定位 `$(PkgAson_Bridge_Grpc)`，或解包；
  仓库 `src/Ason.Bridge.Grpc/Protos/ason_bridge.proto`）、三种最小示例
  （`python -m grpc_tools.protoc`、`protoc --go_out`/`--java_out`、`grpcurl -proto`）、服务与方法全名
  （`ason.bridge.v1.AsonBridge/{GetManifest,ListInstances,ExecuteScript,InvokeFunction,StreamExecution}`；
  T3 会再加 `InvokeMcpTool`）、以及"**能调什么仍以 manifest 为准**"这一边界。
- **Mirror**: `Ason.ExternalExecutor` 的 `Pack="true"`/`buildTransitive` 投递方式；`Ason.Bridge.Grpc.csproj` 现有 icon 投递；`docs/app-agent-separation.*` 既有"传输"小节与 `i18n` 注释。
- **Validate**: `dotnet pack src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj -c Release -o ./artifacts/pack` 后解包断言存在 `protos/ason_bridge.proto`（脚本化并入 CI）；
  手工 `grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto -d "{\"code\":\"return 1;\"}" localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript` 跑通（若②落地，再验证不带 `-proto` 也可用）；三语结构一致。
- **Risk**: 低（纯投递 + 文档）；唯一决策点是②的反射默认值及其与 T1 鉴权的组合语义。
- **依赖/顺序**: **必须排在 T3 与 T4 之后** —— 那两项会改动 proto（`InvokeMcpTool`、`include_instance_declarations`），否则打包与文档要跟随两次。

#### Task 11（T11 / 缺口 I）—— 示例命名可读性 + 角色图例（重命名）· 0.5 天
- **Action**:
  ① **重命名**：`samples/ConsoleGrpcBridgeHost/` → `samples/ConsoleBridgeApp/`（工程 `ConsoleBridgeApp.csproj`）、
     `samples/ConsoleGrpcBridgeDemo/` → `samples/ConsoleBridgeCaller/`（工程 `ConsoleBridgeCaller.csproj`）；同步
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
  `dotnet build samples/ConsoleBridgeApp -c Release` 与 `samples/ConsoleBridgeCaller -c Release` 通过；CI YAML 清单同步。
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
dotnet build samples/ConsoleGrpcBridgeHost/ConsoleGrpcBridgeHost.csproj -c Release
dotnet build samples/ConsoleAgentSample/ConsoleAgentSample.csproj -c Release
dotnet build samples/ConsoleGrpcBridgeDemo/ConsoleGrpcBridgeDemo.csproj -c Release

# T10：契约交付（非 .NET 调用方）
dotnet pack src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj -c Release -o ./artifacts/pack
#   解包断言（nupkg 是 zip，先改扩展名）：应存在 protos/ason_bridge.proto
#   Copy-Item artifacts/pack/Ason.Bridge.Grpc.*.nupkg $env:TEMP\p.zip; Expand-Archive $env:TEMP\p.zip $env:TEMP\p
#   Get-ChildItem -Recurse $env:TEMP\p -Filter *.proto
#   非 .NET 调用方手工验证：
#   grpcurl -plaintext -proto src/Ason.Bridge.Grpc/Protos/ason_bridge.proto \
#           -d "{\"code\":\"return 1;\"}" localhost:5222 ason.bridge.v1.AsonBridge/ExecuteScript

# T11：重命名后的"无残留引用"断言（历史提交与 §10 记录除外）
git ls-files | Select-String -Pattern 'ConsoleGrpcBridgeHost|ConsoleGrpcBridgeDemo'   # 期望：无输出
dotnet build samples/ConsoleBridgeApp/ConsoleBridgeApp.csproj -c Release
dotnet build samples/ConsoleBridgeCaller/ConsoleBridgeCaller.csproj -c Release

# T12：三语文档结构一致性（Case/边界/表格列数）
git diff --stat -- docs/README.*      # 期望：仅文档
```

**Wave 2 依赖顺序**：Task 1（T0）独立 → Task 2（T1）→ Task 3 + Task 4 合并为一次契约变更（proto 1.1）→
**Task 10（T10，必须落在 T3/T4 之后：它交付的就是那份 proto）** → **Task 11 + Task 12（T11/T12，重命名与调用方对照表；都在 T8/T9 之前，避免 CI 与文档被改两遍）** →
Task 5（T2，SSE 必须受 T1 约束）→ Task 6（T5，覆盖 1–5、10–12 的新分支）→ Task 7（T6）与 Task 8（T7）→ Task 9（T8）。
合计约 **11.0 人日**（Wave 2；含本轮新增的 T10 0.5 + T11 0.5 + T12 0.5）。

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

**Wave 2（待执行）**
- [ ] Task 1：三语 `architecture.*` 含 `Case 1–6`（+可选 7），应用侧执行器的 `invoke` 回环已画出，表述不再自相矛盾
- [ ] Task 2：gRPC/MCP 授权开关可用且语义正确（Unauthenticated / 401）；类型化 MCP 客户端与中继可携带 key；默认路径不回归
- [ ] Task 3：`invokeMcpTool` 在 gRPC 与 MCP 可用；未配置 MCP 客户端时返回 `not-supported`
- [ ] Task 4：`includeInstanceDeclarations` 与 `instancesRevision` 有测试；默认行为与 Wave 1 完全一致
- [ ] Task 5：manifest 的 `logStream` 与各适配器实际能力一致；HTTP SSE 与 MCP 日志流有测试
- [ ] Task 6：适配器行覆盖 ≥87%、分支 ≥70%（有命令与数字记录）；UI 自动化决策已落地并写明理由
- [ ] Task 7：版本号与 release notes 就绪；4 个包可打包且顺序正确
- [ ] Task 8：CI 有覆盖率门禁；Windows 作业覆盖 `Ason.Tests`
- [ ] Task 9：三语文档与证据报告同步；全量验证矩阵通过；每项任务都有 RED→GREEN 检查点提交
- [ ] Task 10：`Ason.Bridge.Grpc` 包内含 `protos/ason_bridge.proto`（有解包断言，必要时并入 CI）；三语"非 .NET 调用方"小节含两条 proto 获取路径与最小示例；反射若开启则默认关闭且与 T1 鉴权语义一致
- [ ] Task 11：`ConsoleBridgeApp` / `ConsoleBridgeCaller` 重命名到位、`git ls-files` 无残留引用、sln/CI/三语文档/E2E 助手同步、形态矩阵含角色图例（或按备选记录"仅图例"决定）
- [ ] Task 12：三语新增"调用方接入：需要知道什么、需要配置什么"节，含形态对照表 + 三条前提（无自动发现／handle 运行期／proxies 快照）+ 交叉链接

---

## 9. 确认门（WAIT）

规划阶段不写实现代码。请检测本文件后回复：

- `yes` / `proceed` —— 按 Task 1 → 2 → 3+4 → 5 → 6 → 7 → 8 → 9 顺序开工；
- `modify: …` —— 增删任务、调整顺序或缩小范围；
- `skip Tn` —— 跳过某项。

**待裁决的不确定点**

1. **目标版本号**：建议 `0.9.0`（追加式 API 至少 minor）；release notes 落盘位置需确认。
2. **Task 5 的 MCP 日志流**：走 **progress notification**（更原生，需先验证 stdio/HTTP 一致性）还是 **`ason_stream_script` 工具**（更通用，可作回退）。
3. **Task 6 的 FlaUI**：接入 Windows 作业并 `continue-on-error` 观察，还是明确不接入并写入文档。
4. **Task 10 的 gRPC 反射**：是否提供 opt-in 反射（`grpcurl`/非 .NET 调用方无需本地 proto 即可调用）；默认关闭；
   若开启，需与 T1 的鉴权一起评估（反射会把可调用面再对外广播一次）。
5. **Task 11 的重命名目标名**：建议 `ConsoleBridgeApp` / `ConsoleBridgeCaller`（角色即名字）；
   是否接受这套名字、或退化为"仅加角色图例、不改目录"（备选路径已写入 T11）。

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
| 后续纳入（同一轮内追加）：缺口 **I / J** → **Task 11**（示例重命名 `ConsoleBridgeApp` / `ConsoleBridgeCaller` + 形态矩阵角色图例）、**Task 12**（三语新增"调用方接入：需要知道什么、需要配置什么"节）；复杂度合计 10.0 → **11.0 人日** | 你指示"纳入"；两项都是可用性/文档缺口，**无新增功能代码**（T11 纯重命名，T12 纯文档），因此不改动 §4 的功能面 |
