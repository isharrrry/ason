# plan.md —— ASON Bridge 分阶段计划（Wave 1 已完成 · Wave 2 待执行）

**本文件是这条分支的唯一计划入口**，同时包含：
- **Wave 1（已完成）**：在不含计划文件的情况下执行的那一波，按证据报告与 git 历史**事后重建**为"计划-执行对照"；
- **Wave 2（待执行）**：下一波任务 T0–T8（含此前明确要求纳入的 D 项）。

| 项 | 值 |
|---|---|
| 分支 | `feat/agent-app-separation-bridge` |
| 基线提交 | `fb00b0f`（撰写本文件时的 HEAD，工作树干净） |
| 状态 | **Wave 1 已完成并已提交；Wave 2 待确认 —— 本文件不含实现代码，检测通过后再开工** |
| Wave 1 证据 | `docs/testing/app-agent-separation.tdd.md`（含覆盖率、RED/GREEN 证据、缺口清单） |
| Wave 1 用户文档 | `docs/app-agent-separation.{md,zh-CN,es}`、`docs/architecture.*`、`docs/configuration.*`、`docs/execution-modes.*`、`docs/contributing.*`、`README.*` |
| Wave 2 计划来源 | 本文件 §B（此前也单独写在 `.agents/plans/bridge-next-wave.plan.md`，现已合并到本文件，见 §E 变更记录） |

> 追溯说明：Wave 1 的原始计划是**内联**给出的（未落盘），因此 §A 的任务分解是按当时确认的范围 + 实际提交记录重建的，
> 不是事后改写：每个任务的"证据"列都是真实执行过的命令与结果。

---

## A. Wave 1 —— 已完成（计划-执行对照）

### A.0 当时确认的范围

保留 `samples/WptDemoApp`；新增"应用侧 WPF 演示（不含 Agent）"与"WPF Agent 演示"，实现应用/Agent 分离；
新增**桥**，要求：传输无关核心 + 可插拔适配器（gRPC / MCP / Swagger(OpenAPI) / 未来其它），成熟库不得进入 `Ason`；
桥既要对外暴露 API 列表，又要把执行请求转给执行器（内部或 `Ason.ExternalExecutor`）并回传；
整段脚本与单函数两种接口粒度可组合；TDD 测试先行；先做 Phase 0–3，随后按追加要求补齐 gRPC 客户端 exe demo 与其余全部内容。

### A.1 任务与结果

| 任务 | 结果 | 关键证据 |
|---|---|---|
| P0 分支 + `src/Ason.Bridge` 骨架 + sln 注册 | ✅ | `dotnet build src/Ason.Bridge` 成功（移除两个引发 NU1605 的显式包引用） |
| P1 桥核心：清单/能力/执行器/运行时 + `Ason` 追加式改动 | ✅ | RED：`error CS0246`（计划类型全缺）；GREEN：`Ason.Bridge.Tests` 36/36，`Ason.Tests` 101/101，smoke 11/11 |
| P2 传输缝：`IRunnerTransport` 公开 + `RunnerClient.UseTransport`/`RunnerTransportSettings.TransportFactory` | ✅ | RED：`error CS1061: RunnerClient 未包含 UseTransport`；GREEN：`Ason.Tests` 104/104 |
| P3 gRPC 适配器：`ason_bridge.proto` + service/client/transport/endpoint | ✅ | RED：`CS1061/CS0103`；GREEN：46/46；环境风险（Kestrel h2c + Grpc.Tools 代码生成）先用一次性 spike 排除 |
| P3b MCP 适配器（Streamable HTTP 工具面 + 客户端 + 传输）+ `Ason.Bridge.McpHost` stdio 中继 | ✅ | RED：`CS0234: 命名空间 Ason.Bridge 中不存在 Mcp`；GREEN：54/54 |
| P3c 外部请求侧 exe demo（`ConsoleGrpcBridgeDemo`）+ 应用侧 console 宿主（`ConsoleGrpcBridgeHost`） | ✅ | 60/60 + 手工 E2E：`--func LibDemoStaticOperator.Add --args "[2,3]"` → `OK 5`；`--instances` 列出 marker-only operator |
| P4a WPF 应用侧（无 Agent；gRPC+MCP+OpenAPI；`--bridge-only` 无窗口） | ✅ | 构建通过（WPF + `Microsoft.AspNetCore.App` 同进程风险排除）；手工 E2E：`{"onUiThread":true,"threadId":2}`、脚本改名后状态可见 |
| P4b WPF Agent 侧（零 `[AsonOperator]`）+ `AsonClientOptions.TransportFactory` | ✅ | RED：`CS0117: AsonClientOptions 未包含 TransportFactory`；GREEN：66/66；`agent-operators=0` 且 `remote-call=42`（gRPC 与 MCP 各一遍） |
| P5 OpenAPI/Swagger 适配器 + 文档 + CI + NuGet | ✅ | 73/73；`Ason.Bridge.OpenApi` 行覆盖 98.3%；手工 HTTP：`openapi=3.0.3`、函数调用 42、脚本调用 3 |
| FO1 中继 MCP↔MCP（`McpAsonBridgeEndpoint`）+ **MCP 可选参数真 bug 修复** + gRPC 错误路径 + 覆盖率口径 | ✅ | 86/86；`Ason.Bridge.Grpc` 行覆盖 53.8% → 93.4%；新增 `coverlet.runsettings` |
| FO2 文档审计补漏（三语）：`configuration`/`architecture`/`execution-modes`/`contributing`/`README` | ✅ | `Ason.Tests` 105/105；顺带删除 `Ason.Tests` 中悬空的 `SimpleConsoleApp` 项目引用（既有 MSB9008） |
| FO3 ASCII 流程图 `Case 1–5` + 无 Agent 调用方文档 + 形态↔样例映射（并行提交） | ✅ | `bd96f79`、`c4991af`、`18b8613` |
| FO4 跨平台 Agent 样例 + 外部脚本宿主模式 + `manifest.ToOperatorsLibrary()`（并行提交） | ✅ | `fb00b0f`；`ConsoleSamplesEndToEndTests` 5 例；`Ason.Bridge.Tests` **91/91** |

### A.2 Wave 1 最终基线（Wave 2 的出发点）

| 项 | 值 |
|---|---|
| 桥测试套件 | 91/91（5 console E2E + 2 WPF 应用 E2E + 2 WPF Agent E2E + 2 中继 E2E） |
| `Ason.Tests`（hermetic 过滤集） | 105/105 |
| `LibDemo.SmokeTests`（net9.0） | 11/11 |
| 覆盖率（`coverlet.runsettings`，已排除 protoc 生成源） | `Ason.Bridge` 88.9%/79.9% · `.Grpc` 93.4%/60.5% · `.Mcp` 87.1%/51.4% · `.OpenApi` 98.3%/70.0% |
| 新增库/项目 | `Ason.Bridge`、`Ason.Bridge.Grpc`、`Ason.Bridge.Mcp`、`Ason.Bridge.OpenApi`、`Ason.Bridge.McpHost` |
| 新增样例 | `WpfAppOnlyDemo`、`WpfAgentDemo`、`ConsoleGrpcBridgeHost`、`ConsoleGrpcBridgeDemo`、`ConsoleAgentSample` |
| 对 `Ason` 的改动 | 全部为追加式：`IRunnerTransport` 公开、`RootOperator.OperatorInstances` 公开、`KeywordScriptValidator` 公开、`OperatorBase.IsAttached`、`AsonClientOptions.TransportFactory` |

### A.3 Wave 1 遗留缺口（→ 直接构成 Wave 2 输入）

| 编号 | 缺口 | 是否纳入 Wave 2 |
|---|---|---|
| A | 鉴权可达性：只有 HTTP 适配器有 key；gRPC/MCP 无授权钩子；类型化 MCP 客户端与中继**发不出** key | ✅ T1 |
| B | `logStream` 只有 gRPC 真正实现，MCP/HTTP 没有，但 manifest 处处声明 | ✅ T2 |
| C | `invokeMcpTool` 不在 gRPC 契约、MCP 也无对应工具 | ✅ T3 |
| **D** | `manifest.proxies` 是快照：脚本用实例变量时会因视图开关而失效 | ✅ **T4（本次明确纳入）** |
| E | 适配器分支覆盖 51–80%；样例程序集覆盖率未采集；UI 自动化未接入 | ✅ T5 |
| F | 发布卫生：版本、release notes、包清单 | ✅ T6 |
| G | CI：无覆盖率门禁；Windows 作业未跑 `Ason.Tests` | ✅ T7 |
| — | 其它已记录但决定不动：Agent 聊天需模型 key、`--verify`/`--bridge-only` 为样例级钩子、集成测试单项目 | ❌ 保留为已知项（§C） |

---

## B. Wave 2 —— 待执行（T0–T8，含指定纳入的 D）

### B.0 目标

把 Wave 1 记录在案的缺口从"已知且已文档化"推进到"已实现且有测试"；让 gRPC / MCP / HTTP 三者在能力上对等、鉴权可达、口径诚实。

### B.1 任务清单

> TDD 纪律：每项先写失败测试（DEL，编译期或运行期皆可，须留证据）→ 最小实现（GREEN）→ 同分支检查点提交；Wave 2 的证据在 T8 增补进 `docs/testing/app-agent-separation.tdd.md`。

#### T0 —— ASCII 流程图收尾（文档，无代码）· 0.5 天
- **动作**：① `docs/architecture.{md,zh-CN,es}` 的 `Case 1` 增补 **boundary A′ 双向流**：`exec` 下行 → 应用内求值 → 执行器 **`invoke` 上行到应用** → 真实方法执行 → **`invokeResult` 下行** → **`execResult` 上行回调用方**；并把紧随其后那句 "operator calls do not [cross]" 明确限定为"**在调用方边界 D 上**不跨越"，避免与 A′ 图自相矛盾。② 新增 `Case 6 —— 远程运行器变体`（应用 → `Ason.RemoteBridge`(SignalR) → 服务器侧执行器，`invoke` 回到应用）。③ 可选（与 T2 联动）`Case 7 —— 日志流`，标注 T2 完成前后差异。④ 三语结构一致性（编号/箭头/缩进一致，仅标签翻译，遵守文件内 `i18n: localize-labels` 注释）。
- **参照**：`docs/architecture.md:101-161`（现有 Case 1–5）与边界表。
- **验证**：三语 `Case` 数量与框编号一致；`git diff --stat` 仅动文档。
- **风险**：低（唯一风险就是与既有"operator calls 不跨越"表述冲突 —— 这正是要修的）。

#### T1 —— 鉴权可达性（安全）· 1.5 天
- **动作**：① `AddAsonGrpcBridge(..., policyName)` / `MapAsonGrpcBridge(policyName)`：未授权返回 gRPC `StatusCode.Unauthenticated`（不得是 `Unimplemented`，否则被误判为"能力未启用"）。② `AddAsonMcpBridge(..., requireAuthorization)`：走 ASP.NET `RequireAuthorization`/`AddAuthorizationFilters`，未授权 401。③ `McpAsonBridgeClient.ConnectAsync(endpoint, headers)` 补 `HttpClientTransportOptions.AdditionalHeaders`。④ `GrpcAsonBridgeClient.Connect(address, headers)` 用 `CallCredentials`/`Metadata` 注入。⑤ `Ason.Bridge.McpHost --key`（或可重复 `--header Name=Value`；`ASON_BRIDGE_KEY` 等价）。⑥ 三语文档 Security 小节补"每个适配器如何开启鉴权、中继如何携带"。
- **参照**：`Ason.RemoteBridge` 的 `MapAson(pattern, requireAuthorization)`；`AsonOpenApiBridgeOptions.ApiKey` 及其测试。
- **验证**：新增 `GrpcAuthTests` / `McpAuthTests` / `RelayAuthTests`；`dotnet test tests/Ason.Bridge.Tests -c Release` 全绿且既有 console E2E 默认无鉴权路径不回归；手工：`--key` 双向成功、去掉失败。
- **风险**：中（授权默认关闭，必须确认样例默认路径不变；Unauthenticated vs Unimplemented 语义要被测死）。

#### T2 —— `logStream` 口径与实现（契约）· 1.5 天
- **动作**：① `Capabilities.LogStream` 反映**适配器实际提供**的能力（适配器按自身能力覆盖并加注释说明"这是端到端可见性，不是运行时开关"）。② HTTP 日志流：`POST {base}/script/stream`（SSE，`log* → result|error`），受 T1 授权约束。③ MCP 日志流：新增 `ason_stream_script` 工具（或 SDK progress notification，需先在 stdio/HTTP 两种传输下验证一致性）。④ 三语文档能力表补"每种适配器如何拿到日志"。
- **参照**：`GrpcAsonBridgeService.StreamExecution`（channel + `runtime.Log` 的既有实现，直接复用事件形状）。
- **验证**：`OpenApiStreamingTests`、`McpStreamingTests`、`CapabilityTests`（关闭 executeScript 时 stream 也不可用）+ 全量桥套件。
- **风险**：中（SSE 缓冲/代理；MCP progress 在不同传输下的差异）。

#### T3 —— `invokeMcpTool` 进入 gRPC 与 MCP（契约）· 1.5 天
- **动作**：① `ason_bridge.proto` 增 `rpc InvokeMcpTool` + request message。② `AsonBridgeMcpTools` 增 `ason_invoke_mcp_tool`（仅当 `Capabilities.InvokeMcpTool` 为真时注册）。③ `GrpcAsonBridgeEndpoint` / `McpAsonBridgeEndpoint` 由"not-supported"改为**转发**；桥未配置 MCP 客户端时仍 `not-supported`（错误来源从"契约缺失"变为"运行时未配置"）。④ `AsonBridgeProtocol.Version` → `1.1`，文档写明"1.0 客户端仍可用（全部为新增）"。
- **参照**：`AsonBridgeRuntime.InvokeMcpToolAsync`（已存在）+ `RunnerClient.InvokeMcpToolAsync`；测试替身 `tests/TestMcpServer`。
- **验证**：`GrpcMcpPassthroughTests`、`McpPassthroughTests`（含"未注册 MCP 客户端 → not-supported"）+ manifest `invokeMcpTool` 一致性断言。
- **风险**：中（proto 变更需端到端同步升级）。

#### T4 ——（D，本次指定纳入）实例声明鲜度（契约/行为）· 1.0 天
- **动作**：① `ExecuteScriptRequest` 增 `bool include_instance_declarations`（proto + MCP 工具参数 + HTTP body）：`true` 时由**应用**把当前实例变量声明拼到脚本体前（调用方只发 body）。② `AsonBridgeManifest` 增 `instancesRevision`（实例目录版本/计数），供调用方判断清单是否过期。③ `ason_get_script_api` 保持最新 `proxies/signatures`；文档"已知限制"改写为："脚本使用实例变量时用 `includeInstanceDeclarations`（或先取最新 manifest），否则可能因实例开关而编译失败；函数级接口始终解析活 handle。"④ 首选路径在样例落地：`ConsoleAgentSample --send` 与 WPF Agent 使用该开关。
- **参照**：`AsonBridgeRuntime.BuildProxyPreamble()` / `BuildInstanceDeclarations()`（已存在，含 `OperatorVariableDeclarations.Build`）。
- **验证**：`InstanceFreshnessTests`（快照 → 新建实例 → 不带开关失败 / 带开关成功；`instancesRevision` 变化）+ `ConsoleSamplesEndToEndTests` 增一条实例变量脚本 E2E + 全量套件。
- **风险**：中（默认 `false` 必须与 Wave 1 行为完全一致，先加测试固定旧行为）。

#### T5 —— 覆盖率（测试）· 1.5 天
- **动作**：① 逐条补候选分支：MCP 取消脚本、空参数调用、**每个适配器**能力关闭、中继 `--transport` 非法值、OpenAPI `handle` 经 query、流式错误事件、客户端自有 channel 释放（核对）。② 样例程序集覆盖率采集（console 三件套，**不设门禁**，只作可见性）。③ UI 自动化决策落地：FlaUI 接入 Windows 作业并 `continue-on-error`，或明确写入文档"不接入，理由：交互式桌面 + flaky，已由进程级 E2E 覆盖" —— 二选一必须写进证据报告。
- **参照**：`GrpcErrorPathTests` 的命名与结构；`coverlet.runsettings`。
- **验证**：`dotnet test ... --collect:"XPlat Code Coverage" --settings coverlet.runsettings`；目标：四适配器**行 ≥87% 保持、分支 ≥70%**（`.Grpc` 从 60.5% 起）。
- **风险**：低（若某分支确实不可达，在报告里说明而不是硬凑）。

#### T6 —— 发布卫生（发布）· 0.5 天
- **动作**：① `Directory.Build.props` 版本由 `0.8.2` 递增（建议 `0.9.0`，因 `Ason` 有追加式公开 API）。② release notes（先核对仓库既有位置）：新包 `Ason.Bridge` / `.Grpc` / `.Mcp` / `.OpenApi`（`Ason.Bridge.McpHost` 为样例 exe 不打包）、追加式 API 清单、"无破坏性变更"确认。③ 核对 `publish-nuget.yml` 打包顺序与依赖。④ 核对 README 中的版本引用（如 `ghcr.io/alexgoon/ason:<version>`）。
- **验证**：`dotnet pack` 干跑四个包；逐项目 Release 构建 + 版本/包名清单。
- **风险**：低（目标版本号需与维护者确认）。

#### T7 —— CI 加固（CI）· 0.5 天
- **动作**：① 覆盖率门禁：桥测试步骤加采集 + 阈值（coverlet `<Threshold>`/`ThresholdType=line` 或解析 cobertura 的脚本；建议先 `line ≥ 85%`，分支阈值先只提示不阻断）。② Windows 作业补 `dotnet test tests/Ason.Tests`（hermetic）与 smoke 的 net10 腿（.NET 10 GA 后解除条件）。③ 落地 T5 的 FlaUI 决策。
- **验证**：YAML 自检 + 本地复跑门禁命令确认阈值可过。
- **风险**：中（门禁过严会让 PR 变脆，阈值需留余量）。

#### T8 —— 文档与证据报告收尾 · 0.5 天
- **动作**：① `docs/app-agent-separation.{md,zh-CN,es}`：能力表（`invokeMcpTool`/`logStream` 的适配器差异）、鉴权小节、已知限制（T4 新口径）。② `docs/configuration.*` 新增适配器选项（授权开关、headers）；`docs/contributing.*` 覆盖率门禁与新测试说明。③ `docs/testing/app-agent-separation.tdd.md` 新增 "Wave 2" 章节（逐项：执行摘要、验证命令、RED/GREEN 证据、保证什么）+ 更新覆盖率表与缺口清单（T1–T4 从缺口移除）。④ 本文件与证据报告互相引用。
- **验证**：三语结构一致（标题层级、`i18n` 注释、边界命名 D/E/A′ 一致）；全量验证矩阵复跑。
- **风险**：低。

### B.2 依赖与执行顺序

```
T0 (docs, 无依赖)
T1 (鉴权) ──► T2 (日志流：SSE 必须受 T1 授权约束)
T3 + T4 (合并为一次契约变更：proto 版本 1.1，MCP 工具与 HTTP body 同批) ──► T5 (覆盖这两批新增分支)
T6 (发布) 与 T7 (CI) 在 T1–T5 全部落地后进行（版本与门禁要描述最终状态）
T8 最后（文档与证据报告汇总）
```

建议开工顺序：**T0 → T1 → T3/T4 → T2 → T5 → T6 → T7 → T8**（约 **9.5 人日**）。

### B.3 验证矩阵（每项完成后必须跑的最小子集）

```bash
# 桥（含进程级 E2E；Windows 上会真正起 WPF 样例）
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release

# 库回归（适配器改动可能触达 RunnerClient/协议）
dotnet test tests/Ason.Tests/Ason.Tests.csproj -c Release --filter "DisplayName!~Docker&FullyQualifiedName!~McpClientTests"
dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj -c Release --framework net9.0

# 覆盖率（含生成源排除口径）
dotnet test tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj -c Release \
  --collect:"XPlat Code Coverage" --settings coverlet.runsettings

# 样例构建（与 Linux 作业同一清单）
dotnet build samples/ConsoleGrpcBridgeHost/ConsoleGrpcBridgeHost.csproj -c Release
dotnet build samples/ConsoleAgentSample/ConsoleAgentSample.csproj -c Release
dotnet build samples/ConsoleGrpcBridgeDemo/ConsoleGrpcBridgeDemo.csproj -c Release
```

### B.4 风险

| 风险 | 概率 | 影响 | 缓解 |
|---|---|---|---|
| proto 变更导致客户端/服务端版本错配 | 中 | 高 | T3/T4 合并一次变更；`protocolVersion` 升 1.1；manifest 携带版本、客户端启动即校验 |
| 鉴权默认开启导致样例/CI 回归 | 中 | 中 | 授权一律 opt-in；既有 E2E 默认路径不变；新增"关闭授权仍可用"测试 |
| `ExecuteScript` 语义扩展误伤既有调用方 | 中 | 中 | 新参数默认 `false`（与现状一致）；先加测试固定旧行为 |
| SSE/MCP 日志流的传输差异 | 中 | 中 | 先"如实上报能力"，实现后按适配器分别测；MCP 保留工具回退 |
| 覆盖率门禁过严致 PR 变脆 | 中 | 低 | 阈值按当前值留余量；分支阈值先提示不阻断 |
| 三语文档不一致累积 | 中 | 低 | 每项任务把三语同步列为验收项；`i18n: localize-labels` 为约束 |
| 文档间编号/命名漂移（D/E/A′） | 低 | 中 | T8 做一次跨文档命名核对 |

### B.5 验收标准

- [ ] **T0**：三语 `architecture.*` 含 `Case 1–6`（+可选 7），应用侧执行器的 `invoke` 回环已画出，"operator calls never cross" 已限定到调用方边界
- [ ] **T1**：gRPC/MCP 授权开关可用且语义正确（Unauthenticated / 401）；类型化 MCP 客户端与中继可携带 key；默认路径不回归
- [ ] **T2**：manifest 的 `logStream` 与各适配器实际能力一致；HTTP SSE 与 MCP 日志流有测试
- [ ] **T3**：`invokeMcpTool` 在 gRPC 与 MCP 可用；未配置 MCP 客户端时返回 `not-supported`
- [ ] **T4**：`includeInstanceDeclarations` 与 `instancesRevision` 有测试；默认行为与 Wave 1 完全一致
- [ ] **T5**：适配器行覆盖 ≥87%、分支 ≥70%（有命令与数字记录）；UI 自动化决策已落地并写明理由
- [ ] **T6**：版本号与 release notes 就绪；4 个包可打包且顺序正确
- [ ] **T7**：CI 有覆盖率门禁；Windows 作业覆盖 `Ason.Tests`
- [ ] **T8**：三语文档与证据报告同步；全量验证矩阵通过；每项任务都有 RED→GREEN 检查点提交

### B.6 Wave 2 确认门（等人工检测本文件）

回复方式：
- `yes` / `proceed` —— 按 T0 → T1 → T3/T4 → T2 → T5 → T6 → T7 → T8 顺序开工；
- `modify: …` —— 增删任务、调整顺序或缩小范围；
- `skip Tn` —— 跳过某项。

**待裁决的三个不确定点**

1. **目标版本号**：建议 `0.9.0`（追加式 API），备选 `1.0.0`；release notes 落盘位置需确认。
2. **T2 的 MCP 日志流**：走 **progress notification**（更原生，需先验证 stdio/HTTP 行为一致）还是 **`ason_script_stream_script` 工具**（更通用，可作回退）。
3. **T5 的 FlaUI**：接入 Windows 作业并 `continue-on-error` 观察，还是明确不接入并写入文档。

---

## C. 已知项（决定不做，仅记录）

- 独立 ASP.NET Core 应用侧 / MAUI / Blazor 应用侧样例 —— 证据报告已判定"不值得新增样例"（宿主形态与 console host 相同）。
- `--verify` / `--bridge-only` 提升为库 API —— 保持样例级测试钩子。
- 拆分 `tests/Ason.Bridge.IntegrationTests` —— 已决策单测试项目。
- CI 中跑真实模型聊天 —— 需 key；桥接逻辑已由脚本化聊天服务测试 + `--verify` 覆盖。
- `Ason.Bridge.Grpc` 命名空间遮蔽 `Grpc` 根命名空间 —— 已在文档给出写法（`using Grpc.Net.Client;` 后用 `GrpcChannel`）。

---

## D. Wave 1 提交清单（供对照）

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

---

## E. 变更记录

| 日期/触发 | 变更 |
|---|---|
| Wave 1 结束 | 计划为内联形式，未落盘（用户指出缺少 plan.md） |
| 本文件创建 | 合并两波：按证据报告与 git 历史重建 Wave 1（§A），并纳入 Wave 2（§B）；原单独文件 `.agents/plans/bridge-next-wave.plan.md` 已被本文件取代并删除，避免两份计划漂移 |
