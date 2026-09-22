# Plan: WptDemoApp 多目标 net10/net9/net6 + LibDemo(net6.0) + Ason.Abstractions

**Created**: 2026-09-22
**Mode**: conversational `/plan`（eccplan → references/commands/plan.md）
**Complexity**: Medium（若 Phase 0 门判定为 FAIL 则降级为方案 B）
**Status**: 已确认方向，待开工

---

## 0. 已拍板的决策（Decision Log）

| 决策 | 选择 | 理由 |
|---|---|---|
| net6 腿如何存活 | **方案 A**：`Ason` / `Ason.Runner.Core` 多目标 `net6.0;net9.0` | 三条腿代码完全一致，AI/算子/InProcess 执行全可用 |
| WPF 目标框架 | `net10.0-windows;net9.0-windows;net6.0-windows` | 保留 net9 既有验证路径 |
| 是否抽 Ason.Abstractions | **是**，`netstandard2.0`（**单目标**），**只含标记类型** | LibDemo 只需标记，不必拖入 SK/Roslyn/SignalR；单目标保证 ns2.0/net6/net9/net10 消费者看到**同一份**程序集，属性类型标识不漂移 |
| ns2.0 库能否引用标记 | **必须能**（用户硬要求） | `Ason.Abstractions` 本身即 ns2.0，天然满足；再用 LibDemo 加一条 ns2.0 腿自证 |
| `OperatorBase`/`RootOperator` 是否搬走 | **不搬**，留在 `Ason` | 满是 `internal` 成员，属运行时语义而非"标记"；搬走需额外放开 `InternalsVisibleTo` |
| net10 运行时 | 本机仅 `10.0.100-rc.2` | 已知前提，记录在案 |

---

## 1. 需求复述

1. `samples/WptDemoApp`（现 `net9.0-windows`，`WpfSampleApp.csproj:5`）→ 同时支持 **net10 / net9 / net6** 编译。
2. 新增 **LibDemo** 库（`net6.0`），可使用 `[AsonOperator]` / `[AsonMethod]` / `[AsonModel]` 标记。
3. 抽出 **Ason.Abstractions**，供 net6 侧引用标记类型。
4. **[硬要求]** `[Ason*]` 标记必须能被 **netstandard2.0 库**的代码引用（不只是 net6/net9/net10）。

---

## 2. 证据（本仓库 + 本机实测）

### 2.1 为什么必须抽 Abstractions

| 事实 | 出处 |
|---|---|
| `Ason` 只产出 `net9.0` | `src/Ason/Ason.csproj:4` |
| 三个标记 + 基类零外部依赖（仅 BCL `Attribute`） | `src/Ason/ProxyOperators/ProxyAttributes.cs:3-39` |
| net6.0 项目无法引用 net9.0 程序集 | NuGet TFM 兼容规则 |
| 纯标记算子（不继承 `OperatorBase`）是一等公民 | `src/Ason/CodeGen/OperatorBuilder.cs:65`、`src/Ason/ProxyOperators/BuiltInOperators.cs:9` |

### 2.2 方案 A 的可行性证据

Ason 现有全部运行时依赖均带 `netstandard2.0` 资产：

| 包 | 版本 | 资产 |
|---|---|---|
| Microsoft.SemanticKernel | 1.45.0 | net8.0, netstandard2.0 |
| Microsoft.SemanticKernel.Connectors.OpenAI | 1.45.0 | net8.0, netstandard2.0 |
| Microsoft.SemanticKernel.Agents.Core | 1.45.0 | net8.0, netstandard2.0 |
| Microsoft.SemanticKernel.Agents.Abstractions | 1.45.0 | net8.0, netstandard2.0 |
| Microsoft.AspNetCore.SignalR.Client | 8.0.7 | net462, net8.0, netstandard2.0 |
| ModelContextProtocol.Core | 0.4.0-preview.3 | net10.0, net9.0, net8.0, netstandard2.0 |
| Microsoft.CodeAnalysis.CSharp.Scripting | 4.14.0 | net8.0, net9.0, netstandard2.0 |

### 2.3 WPF 三方包的 net6 支持

| 包 | 版本 | net6 资产 |
|---|---|---|
| CommunityToolkit.Mvvm | 8.4.0 | netstandard2.0（可用） |
| Microsoft.Xaml.Behaviors.Wpf | 1.1.135 | net6.0-windows7.0 |
| ScottPlot.WPF | 5.0.56 | net6.0-windows7.0 |

### 2.4 本机工具链

- SDK：`10.0.100-rc.2.25502.107`、`9.0.306`、`6.0.417`（另有 3.1/5.0/7.0）
- 引用包：`Microsoft.WindowsDesktop.App.Ref` 与 `Microsoft.NETCore.App.Ref` 的 `6.0.12/6.0.25/6.0.36` + `10.0.0-rc.2` **均已安装** → 三条腿离线可编
- 运行时：`Microsoft.WindowsDesktop.App` 6.0.36 / 9.0.10 / 10.0.0-rc.2
- `api.nuget.org` 可达（302）

### 2.5 已知编译障碍

- `src/` 内有 **10 处原始字符串字面量（C#11）**、2 处 `scoped`/`file`；net6.0 默认 LangVersion 为 **C#10** → 必须显式设 `LangVersion`。
- WPF 样例的 VM 使用**主构造函数（C#12）**（`ViewModels/ChatViewModel.cs:16`、`CalendersViewModel` 等）→ WPF 项目同样要设 `LangVersion`。

### 2.6 既有故障（本次不修，但要知道）

`tests/Ason.Tests/Ason.Tests.csproj` 引用 `..\..\samples\SimpleConsoleApp\MyConsoleApp.csproj`，**该文件全盘不存在** → 整 sln 构建 / `dotnet test` 现在就是坏的。
→ 验证只跑指定项目，或另行决定是否顺手修。

### 2.7 netstandard2.0 的 LangVersion 陷阱（必须处理）

`netstandard2.0` 的**默认 LangVersion 是 C# 7.3**，而待搬迁的 `src/Ason/ProxyOperators/ProxyAttributes.cs` 用了：

- `namespace Ason;` 文件级命名空间（C# 10）→ `:1`，C# 7.3 下**编译失败**
- `string?` 可空引用类型（C# 8）→ `:5,7,16,17,26,34`，同样失败

→ `Ason.Abstractions.csproj` 必须显式 `<LangVersion>latest</LangVersion>` + `<Nullable>enable</Nullable>`。
→ 若 LibDemo 也加 ns2.0 腿，同理；且要避开 `record`/`init`（ns2.0 需 `IsExternalInit` polyfill）。

---

## 3. Patterns to Mirror

| 类别 | 出处 | 模式 |
|---|---|---|
| 项目骨架 | `src/Ason/Ason.csproj:1-15` | SDK 式 + `BaseOutputPath ..\bin` + `GeneratePackageOnBuild`/`IsPackable`/`Authors`/`PackageId`/`Description`/`PackageIcon` |
| 标记用法 | `samples/WptDemoApp/AI/AppOperators.cs:8-14` | `[AsonOperator(description: "...")]` + `[AsonMethod("...")]` |
| 纯标记算子 | `src/Ason/ProxyOperators/BuiltInOperators.cs:9-14` | 类上打标记 + 方法上 `[AsonMethod(...)]`，不继承 `OperatorBase` |
| 项目引用 | `samples/WptDemoApp/WpfSampleApp.csproj:29-31` | 相对路径 `ProjectReference` |
| 算子发现 API | `src/Ason/CodeGen/OperatorBuilder.cs:15-19,36-58,109-112` | `AddAssemblies(...)` → `Build()` → `OperatorsLibrary.BuildTask` 取 `(proxies, signatures, cache)` |
| 测试 | `tests/Ason.Tests/Ason.Tests.csproj:33-37` | xunit + `ProjectReference` |

---

## 4. Files to Change

| 文件 | 动作 | 原因 |
|---|---|---|
| `src/Ason.Abstractions/Ason.Abstractions.csproj` | CREATE | `netstandard2.0`，只含标记类型；包元数据照 `Ason.csproj:1-15` |
| `src/Ason.Abstractions/ProxyAttributes.cs` | CREATE | 由 `src/Ason/ProxyOperators/ProxyAttributes.cs` 平移，命名空间仍为 `Ason` |
| `src/Ason/ProxyOperators/ProxyAttributes.cs` | DELETE | 已迁出 |
| `src/Ason/TypeForwards.cs` | CREATE | `[assembly: TypeForwardedTo(...)]` 保住已编译消费者的二进制兼容 |
| `src/Ason/Ason.csproj` | UPDATE | 引用 Abstractions；`TargetFrameworks=net6.0;net9.0`；`LangVersion` |
| `src/Ason.Runner.Core/Ason.Runner.Core.csproj` | UPDATE | 同上多目标，否则被 Ason 的 net6 腿拖不动 |
| `samples/LibDemo/LibDemo.csproj` | CREATE | `net6.0`，只引用 Abstractions |
| `samples/LibDemo/DemoModel.cs` | CREATE | `[AsonModel]` 演示模型 |
| `samples/LibDemo/DemoOperator.cs` | CREATE | `[AsonOperator]` + `[AsonMethod]` 纯标记算子（避开 C#12 语法） |
| `samples/WptDemoApp/WpfSampleApp.csproj` | UPDATE | `TargetFrameworks=net10.0-windows;net9.0-windows;net6.0-windows` + `LangVersion` + 条件引用 |
| `samples/WptDemoApp/ViewModels/ChatViewModel.cs` | UPDATE | `AddAssemblies(..., typeof(LibDemoOperator).Assembly)` 证明跨程序集/跨 TFM 发现 |
| `tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj` + `LibDemoDiscoveryTests.cs` | CREATE | net9 xunit：断言 `BuildTask.signatures` 含 LibDemo 算子 → 让"支持标记"可验证 |
| `Ason.sln` | UPDATE | `dotnet sln add` 三个新项目 |

---

## 5. Tasks

### Phase 0 — 去风险 Spike（决策门）
- **Task 0.1 基线**：`dotnet build src/Ason/Ason.csproj -c Release` 先绿并记录警告数。
- **Task 0.2 撞 net6**：`Ason.csproj` + `Ason.Runner.Core.csproj` 改 `net6.0;net9.0` + `<LangVersion>latest</LangVersion>`，跑 `dotnet build src/Ason/Ason.csproj -c Release -f net6.0`，收集错误清单。
  - **Validate**: 错误分类落表（BCL 缺失 / 包资产缺失 / 语法）。
  - **门（Gate）**：错误全是机械性、可用 polyfill 或 API 替换解决 → 继续方案 A；出现 SK/Roslyn 层面不可解缺口 → 停下汇报，转方案 B。

### Phase 1 — 抽 Ason.Abstractions
- **Task 1.1** 建 `src/Ason.Abstractions/Ason.Abstractions.csproj`（netstandard2.0 + 包元数据 + **`LangVersion=latest`**，见 2.7）。
  - **Mirror**: `src/Ason/Ason.csproj:1-15`
  - **Validate**: `TargetFramework` 只有 `netstandard2.0`，且项目编过
- **Task 1.2** 平移 `ProxyAttributes.cs`（`ProxyAttributeBase` / `AsonOperatorAttribute` / `AsonMethodAttribute` / `AsonModelAttribute`），删除原文件。
- **Task 1.3** 加 `src/Ason/TypeForwards.cs`（`System.Runtime.CompilerServices.TypeForwardedTo`）。
- **Task 1.4** `Ason.csproj` 加 `ProjectReference` 到 Abstractions；复验 `Ason.ExternalExecutor` / `Ason.RemoteBridge` 仍能编。
  - **Validate**: `dotnet build src/Ason/Ason.csproj -c Release` + 两个下游项目构建通过。

### Phase 2 — LibDemo(net6.0)
- **Task 2.1** 建 `samples/LibDemo/LibDemo.csproj`（`Nullable`/`ImplicitUsings` 开、引用 Abstractions、`IsPackable=false`、`LangVersion=latest`）；TFM 见 Task 2.4 的取舍。
- **Task 2.2** 写 `DemoModel.cs`（`[AsonModel]`）与 `DemoOperator.cs`（`[AsonOperator]` 纯标记算子，照 `BuiltInOperators.cs:9-14`）。
  - **Mirror**: `samples/WptDemoApp/AI/AppOperators.cs:8-14`
  - **Validate**: `dotnet build samples/LibDemo/LibDemo.csproj -c Release`
- **Task 2.3** `dotnet sln add`。
- **Task 2.4（ns2.0 自证）** 让 LibDemo 多目标 `net6.0;netstandard2.0`（同一份源码同时编两条腿），证明"ns2.0 库的代码也能引用 `[Ason*]` 标记"。
  - **Validate**: `dotnet build samples/LibDemo/LibDemo.csproj -c Release` 两条腿全绿；`samples/LibDemo/bin/Release/` 下同时出现 `net6.0/` 与 `netstandard2.0/`。

### Phase 3 — WPF 三目标
- **Task 3.1** `WpfSampleApp.csproj`：`TargetFrameworks` 三条腿 + `LangVersion` + 逐 TFM 条件 `ProjectReference`（net6 腿必须落到 Ason 的 net6 资产）。
- **Task 3.2** `ChatViewModel.cs` 的 `OperatorBuilder` 加入 `typeof(LibDemoOperator).Assembly`。
- **Task 3.3** 修逐腿问题（net6 腿注意 `CommunityToolkit.Mvvm` 落 netstandard2.0 资产；net10 腿注意 `ModelContextProtocol.Core` 会选 net10.0 资产）。
  - **Validate**: 三条腿分别 `dotnet build -f <tfm>` 全绿。

### Phase 4 — 验证与收尾
- **Task 4.1** 建 `tests/LibDemo.SmokeTests`（net9 xunit，引用 Ason + LibDemo），断言跨 TFM 发现。
  - **Validate**: `dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj`
- **Task 4.2**（可选）`.github/workflows/publish-nuget.yml`：加 Abstractions 打包步；注意 CI 现为 ubuntu + `9.x`，编 net10 需补 10.x preview SDK。

---

## 6. Validation

```powershell
dotnet build src/Ason.Abstractions/Ason.Abstractions.csproj -c Release
dotnet build src/Ason/Ason.csproj -c Release -f net6.0
dotnet build src/Ason/Ason.csproj -c Release -f net9.0
dotnet build samples/LibDemo/LibDemo.csproj -c Release                    # net6.0 + netstandard2.0 两条腿
dotnet build samples/LibDemo/LibDemo.csproj -c Release -f netstandard2.0  # ns2.0 消费者的硬要求自证
dotnet build samples/WptDemoApp/WpfSampleApp.csproj -c Release -f net6.0-windows
dotnet build samples/WptDemoApp/WpfSampleApp.csproj -c Release -f net9.0-windows
dotnet build samples/WptDemoApp/WpfSampleApp.csproj -c Release -f net10.0-windows
dotnet test tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj
```

---

## 7. Risks

| 风险 | 概率 | 缓解 |
|---|---|---|
| Ason net6 腿撞 net8/9 独有 BCL API | 中 | Phase 0 门先撞；`LangVersion` + 少量 polyfill；不行转方案 B |
| SK 的 netstandard2.0 依赖图需新拉包（国内网络） | 中 | NuGet 国内镜像（腾讯/华为）；先 restore 预热 |
| 标记类型换程序集 → 已编译消费者运行时报错 | 中 | `[TypeForwardedTo]` + 版本号提升 |
| net10 本机仅 RC2（10.0.100-rc.2） | 高 | 已知前提；可选加 `global.json` 锁 rollForward |
| WPF net10 腿 MCP.Core 选 net10.0 资产、Ason 按 net9 编译 | 低 | 运行期验证；必要时钉版本 |
| `tests/Ason.Tests` 引用缺失的 `samples/SimpleConsoleApp/MyConsoleApp.csproj` | 确定 | **实测：仅 MSB9008 警告并跳过该引用，不阻断构建**（`dotnet build Ason.sln` 0 错误）。本计划早先"整 sln 是坏的"结论**已纠正**；仅当测试真正用到该项目的类型时才会失败 |
| net6 腿的传递依赖包官方声明不支持 net6（`Microsoft.Bcl.Memory 9.0.10`、`Microsoft.Bcl.Numerics 9.0.0`、`System.Collections.Immutable 9.0.0`、`System.Net.ServerSentEvents 10.0.0-rc.2`、`System.Numerics.Tensors 9.0.0`、`System.Reflection.Metadata 9.0.0`） | 高 | 编译通过，且**net6 宿主实测跑通**（冒烟测试 2/2）；仍属"厂商未测"配置。可 `<SuppressTfmSupportBuildWarnings>true</SuppressTfmSupportBuildWarnings>` 静音，或把这些包钉到支持 net6 的版本 |
| net6.0 已 EOL → `NETSDK1138` 警告 ×4 | 确定 | 保留可见（诚实信号），如需静音用 `<CheckEolTargetFramework>false</CheckEolTargetFramework>` |

---

## 8. Acceptance

- [x] `Ason.Abstractions`(netstandard2.0) 只含标记类型，Ason 引用它且下游项目仍可编
- [x] `Ason` 在 net6.0 与 net9.0 两个资产下均构建通过
- [x] `LibDemo`(net6.0) 用 `[Ason*]` 标记编译通过
- [x] **netstandard2.0 库**能引用 `[Ason*]` 标记并编译通过（LibDemo 的 ns2.0 腿自证）
- [x] `WpfSampleApp` 在 net6.0-windows / net9.0-windows / net10.0-windows 三条腿全绿
- [x] 宿主侧经 `OperatorBuilder` 能发现 LibDemo 的算子方法（冒烟测试断言）
- [x] 附加：冒烟测试在 **net6.0 / net9.0 / net10.0 三个宿主**上各 2/2 通过（不只是编译，是真跑）
- [x] 全解决方案 `dotnet build Ason.sln -c Release` 0 错误
- [x] 模式照抄既有代码，未另起一套

---

## 9. 执行记录（2026-09-22）

| 步骤 | 结果 |
|---|---|
| Phase 0.1 Ason net9 基线 | ✅ 0 错误 / 14 警告 |
| **Phase 0.2 net6.0 撞编译（决策门）** | ✅ **PASS** — 0 错误 / 21 警告；`LangVersion=latest` 必需 |
| Phase 1 Ason.Abstractions(netstandard2.0) | ✅ 0 错误 0 警告，产出 `Ason.Abstractions.0.8.2.nupkg` |
| Phase 1 Ason 多目标 | ✅ net6.0 + net9.0 双资产构建成功 |
| Phase 2 LibDemo(net6.0;netstandard2.0) | ✅ 双资产 0 错误 0 警告（ns2.0 硬要求自证通过） |
| Phase 3 WPF 三目标 | ✅ net10.0-windows / net9.0-windows / net6.0-windows 三条腿 0 错误（4 轮修复：ThemeMode → AccentColor → AccentColorBrushKey ×2） |
| Phase 4 冒烟测试 | ✅ `net6.0` / `net9.0` / `net10.0` 三个宿主各 2/2 通过 |
| 下游复验 | ✅ `Ason.ExternalExecutor`、`Ason.RemoteBridge` 0 错误（类型转发未破坏二进制兼容路径） |
| 全解决方案 | ✅ `dotnet build Ason.sln -c Release` 0 错误 / 41 警告 |
| Task 4.2 CI 打包步 | ⏸ 未做（按需；CI 现为 ubuntu + `9.x`，编 net10 需补 10.x preview SDK） |

### 为兼容 net6 必须处理的 .NET 9 专属 API（实测清单）

| 位置 | 问题 | 处理 |
|---|---|---|
| `App.xaml:6` | `ThemeMode="Light"`（.NET 9）→ net6 报 `MC3072` | 由 XAML 移入 `App.xaml.cs`，包在 `#if NET9_0_OR_GREATER`；C# 用法触发 `WPF0001`（XAML 用法不触发），用 `#pragma warning disable/restore WPF0001` 抑制 |
| `Views/ChartsView.xaml.cs:14` | `SystemColors.AccentColor`（.NET 9）→ `CS0117` | 新增 `AccentColor` 属性：net9+ 取 `AccentColor`，net6 退 `HighlightColor` |
| `Views/ChatView.xaml:105`、`Views/EmployeeEditView.xaml:147` | `SystemColors.AccentColorBrushKey`（.NET 9，XAML 编译期 `MC3011`） | 改为 `{DynamicResource AsonAccentBrush}`；画刷在 `App.OnStartup` 按 TFM 提供（net9+ 用 AccentColor，net6 用 HighlightColor），net9/net10 观感不变 |

### 施工中发现的事实（影响设计）

`OperatorInvoker` 只按 `handleId` 从 `RootOperator.OperatorInstances` 取**实例**（`OperatorInvoker.cs:27-32`、`AsonClient.cs:98`），且 `OperatorBuilder.BuildMethodCache` 只扫实例方法（`OperatorBuilder.cs:67`）。因此**纯标记算子（含静态类）可被发现、可进入 API 签名文本，但当前 InProcess 路径无法真正调用** —— 这是仓库既有约束，非本次引入。

→ LibDemo 因此定位为"只依赖 Abstractions 的标记库"：证明 net6/ns2.0 代码能引用 `[Ason*]`、宿主能发现；不承诺可调用。
→ 若要求"可调用"，LibDemo 必须引用 `Ason` 并继承 `OperatorBase<T>`，代价是**失去 netstandard2.0 腿**。

---

## 10. 第二次迭代：可自定义 URL 覆盖 + FlaUI 界面测试（2026-09-22）

### 10.1 触发原因

原先 9 处新建 `OpenAIChatCompletionService` 的代码把 **modelId 写死**（`gpt-4.1-mini`）、**endpoint 走默认 OpenAI**，无法接 DeepSeek 等 OpenAI 兼容端点。

DeepSeek 官方接入参数（[api-docs.deepseek.com](https://api-docs.deepseek.com/)）：`base_url = https://api.deepseek.com`（请求 `<base>/chat/completions`），模型名 `deepseek-flash` / `deepseek-v4-pro`。

### 10.2 实现

| 项 | 内容 |
|---|---|
| 新增 | `src/Ason/Client/OpenAiCompatibleChatServiceFactory.cs` |
| 环境变量 | `MY_OPEN_AI_KEY`（回退 `OPENAI_API_KEY`）、**`MY_OPEN_AI_BASE_URL`**（回退 `OPENAI_BASE_URL`）、**`MY_OPEN_AI_MODEL`**（回退 `OPENAI_MODEL`，默认 `gpt-4.1-mini`） |
| 兼容性 | `BASE_URL` 为空时行为与改造前完全一致（走连接器默认 endpoint） |
| 改造调用点 | WPF 样例、ConsoleExtractor、ConsoleMcp、BlazorAdvanced（**4 个 samples，走本地工程引用**）→ 改用 `OpenAiCompatibleChatServiceFactory.FromEnvironment()` |
| 模板（另 5 个） | 模板引用的是**已发布的 NuGet 包**（`<PackageReference Include="Ason" Version="*" />`），拿不到未发布的新 API → 改为**模板内自包含**的环境变量解析（含 `#pragma warning disable SKEXP0010`），今天即可编译 |
| 顺带修复 | WPF 样例在缺少密钥时**不再崩溃**，改为在聊天面板显示可操作的错误信息（原先是未处理异常直接终止进程） |

**踩坑记录（我自己引入又修掉的回归）**：最初把 5 个模板也改成调用新工厂 → `CS0103/CS0234`。A/B 对照显示 **WinForms 模板基线 0 错误、被我改成 2 错误**，而 Console/Wpf/BlazorServer 基线本就各 2 错误。改为自包含写法后 A/B 完全对齐（WinForms 回到 0 错误，其余错误集与基线逐一相同）。

**模板既有损坏（与本次无关，A/B 逐条相同）**：

| 模板 | 既有错误 |
|---|---|
| Console | `Ason.Console` 命名空间与 `Console.WriteLine` 冲突（CS0234） |
| Wpf | `AsonClientOptions.AnswerInstructions` 不存在（CS0117） |
| BlazorServer | `AsonRegistrationOptions.RunnerMode` 不存在（CS1061） |
| WinForms | 无（基线即绿，现已保持绿） |

**坑 1**：端点构造函数带 `[Experimental("SKEXP0010")]`，被提升为 error；按官方做法用 `#pragma warning disable SKEXP0010` 抑制并注明原因。

### 10.3 FlaUI 界面测试

| 项 | 内容 |
|---|---|
| 新增 | `tests/WpfDemoApp.UiTests`（net9.0-windows，xunit + `FlaUI.UIA3 5.0.0`） |
| 复用策略 | xunit collection fixture 全程只启动**一个** app 实例，避免多实例抢焦点 |
| TFM 可切换 | 环境变量 `WPF_DEMO_TFM`（默认 `net9.0-windows`）→ 同一套测试覆盖三条腿 |
| 用例 | ①主窗口与导航列表存在 ②四个视图来回切换（EmployeesGrid / CalendarAppointments / EmailsList / ChartsViewHint）③**缺密钥时面板给出提示**（`MissingApiKeyFact`）④**真实端点问答**（`LiveEndpointFact`，无密钥自动 skip，断言答复非空、不含鉴权/异常标记、且含数字） |
| 可测性改造 | 为 7 个元素补 `AutomationProperties.AutomationId` |
| 顺带修复 | 导航项的 **UIA Name 是 CLR 类型名**（`DisplayMemberPath` 不影响可访问性名称）→ 加 `ItemContainerStyle` 设 `AutomationProperties.Name`，**同时修掉一个读屏软件会念错名称的无障碍缺陷** |

**坑 2**：FlaUI 5.0 的 `Application` 与属性名冲突 → 用 `FlaUI.Core.Application` 全限定 + 属性名 `App`。

### 10.4 实测结果（真实 DeepSeek 调用）

`MY_OPEN_AI_BASE_URL=https://api.deepseek.com`、`MY_OPEN_AI_MODEL=deepseek-flash`，三个 app 腿各跑一遍 UI 测试：

| app 腿 | UI 测试 | 模型实际回答（FlaUI 从界面读回） |
|---|---|---|
| net6.0-windows | ✅ 3 通过 / 1 跳过 | `There are 30 employees in the app.` |
| net9.0-windows | ✅ 3 通过 / 1 跳过 | `30` |
| net10.0-windows | ✅ 3 通过 / 1 跳过 | `30` |

链路：FlaUI 驱动界面 → ASON 生成脚本 → 脚本在应用内执行（数出 30 名员工）→ 回答渲染回界面 → 断言通过。
无密钥场景：`Chat_panel_explains_that_the_api_key_is_missing` 通过，live 用例自动 skip。

### 10.5 环境变量已按用户要求持久化（HKCU\Environment）

```
MY_OPEN_AI_KEY       = <DeepSeek key>
MY_OPEN_AI_BASE_URL  = https://api.deepseek.com
MY_OPEN_AI_MODEL     = deepseek-flash
```

清除方式：`[Environment]::SetEnvironmentVariable('MY_OPEN_AI_KEY',$null,'User')`（其余两个同理）。
**安全提醒**：该 key 曾出现在对话记录中，建议轮换。

---

## 11. 第三次迭代：全量编译通过 + CI 打包 + InProcess 可调用（2026-09-22）

### 11.1 全量编译

| 工程 | 状态 | 修法 |
|---|---|---|
| `Ason.Console.Template` | ✅ 0 错误 | 模板命名空间 `Ason.Console` 遮蔽 `System.Console` → 调用点用**全限定名**。注意 `using Console = System.Console;` **无效**：C# 中命名空间成员优先于 using 别名（`Ason` 里有 `Console` 成员） |
| `Ason.Wpf.Template` | ✅ 0 错误 | `AsonClientOptions.AnswerInstructions` → **`ReceptionInstructions`**（该提示词是"直接回答还是生成脚本"的路由指令，属 reception 而非 explainer） |
| `Ason.BlazorServer.Template` | ✅ 0 错误 | `AsonRegistrationOptions.RunnerMode` → **`ExecutionMode`** |
| `Ason.WinForms.Template` | ✅ 0 错误 | 基线即绿，保持不变 |
| `Ason.Maui.Template.Server` | ✅ 0 错误 | — |
| `Ason.ProjectTemplates`（打包工程） | ✅ 0 错误 | 它只打包 `Content/**`，但默认 Compile 通配符把模板源码全编进来 → 1308 错误；加 **`EnableDefaultCompileItems=false`** |
| `Ason.sln`（15 个工程） | ✅ 0 错误 | — |
| `tests/Ason.RemoteRunner.Tests` | ✅ 编译通过并可运行 | `ProxyMethodAttribute`→`AsonMethodAttribute`、`SkipAnswerAgent`→`SkipExplainerAgent`、`RunnerMode`→`ExecutionMode`；集成用例改为**需 `ASON_REMOTE_RUNNER_URL` 才跑**（它会去连 SignalR hub，本机无服务必然拒连），否则带原因 skip |
| `Ason.Maui.Template` | ⏳ 需 `maui` workload | `NETSDK1147: 必须安装工作负载 maui-android` —— 环境前置条件，非代码问题；源码改动与其余模板同构 |

### 11.2 CI 打包

`.github/workflows/publish-nuget.yml` 新增 `Build & Pack Ason.Abstractions`，**排在 `Ason` 之前**（Ason 的 nupkg 对它有包依赖）。

### 11.3 InProcess 路径可调用 —— 解决方案

根因：`OperatorInvoker` 只按 `handleId` 从 `RootOperator.OperatorInstances` 取**实例**，而纯标记算子既没有 handle 也没有实例；静态类的代理又不传 handle。

**双轨方案（已实现）**：

1. **纯标记实例算子 → 宿主自动注册为单例**（`AsonClient.RegisterMarkerOnlyOperators`，键 = 类型名）。
   随后 `BuildExistingOperatorVariableDeclarations` 会为它生成脚本变量
   `libDemoOperator = new LibDemoOperator("LibDemoOperator");`，脚本即可正常调用。
   由于调用始终发生在宿主侧，该方案对 InProcess / ExternalProcess / Docker / Remote 四种模式**一律有效**。
   无公共无参构造函数的标记类会被记一条 warning 跳过（而不是静默失效）。
2. **静态算子模块（`static class` + `[AsonOperator]`）→ 静态分发**：
   - `IOperatorMethodCache` 新增 `TryGetStatic(typeName, method, argCount, out entry)`
   - `OperatorBuilder.BuildMethodCache` 额外扫描静态方法并建立「类型名 → Type」索引
   - `OperatorInvoker` 在 `handleId` 为空时按类型名解析并静态调用；解析不到时抛出带**可操作提示**的异常

**顺带修掉一个从未被触发过的生成器 bug**：静态算子模块的代理类生成为 `public static class X`，但成员方法没有 `static` 修饰符 → `CS0708: 不能在静态类中声明实例成员`。
`ProxySerializer.EmitRuntimeMethod` / `EmitSignatureMethod` 现按 `isInstance` 决定 `public` 或 `public static`（签名文本同步为 `public static`，让脚本 agent 知道这是静态模块而不是实例）。

### 11.4 本轮验证

| 项 | 结果 |
|---|---|
| `LibDemo.SmokeTests` | **7/7 通过 × net6/net9/net10**；其中 3 个是**真调用**（实例算子回传字符串、实例算子返回时间戳、静态模块 `Add`/`Repeat`） |
| FlaUI（无密钥） | 3 通过 / 1 跳过 |
| FlaUI（DeepSeek live 三腿） | 全部通过，回答 `30` / `30` / `**30**` |
| `Ason.sln` | 0 错误 |
| 4 个模板 + ProjectTemplates | 0 错误 |
| `Ason.Tests` | 59 通过 / 8 失败（**全部 Docker 依赖**）；基线 60/9 → **无回归** |
| `Ason.Runner.Tests` | 1/1 通过 |
| `Ason.RemoteRunner.Tests` | 1 通过 / 1 跳过（需远程 runner 服务） |

LibDemo 现在同时演示两种可调用写法：`LibDemoOperator`（实例、标记式）与 `LibDemoStaticOperator`（静态模块）。

---

## 12. 第四次迭代：分支 / CI / README 国际化（2026-09-22）

### 12.1 分支

`feat` → **`feat/net6-net10-multitarget-and-abstractions`**（已切换）。
取名覆盖本次 PR 的两个头条变更（net6/net10 多目标 + 抽出 Ason.Abstractions），其余成果写在 PR 描述里。

推送注意：`origin` 为 github.com，本机直连不通；国内镜像只支持拉取不支持推送 → 需代理/VPN 或换机能连 GitHub 的机器执行 `git push -u origin <branch>`。

### 12.2 CI（原仓库没有任何 PR/push CI）

排查结论：`.github/workflows/` 只有 `publish-nuget.yml` 与 `publish-docker.yml`，**两者都只在 `push: tags` 触发**；`.github/` 下无 PR 模板、无 CI 工作流。fork 的 HEAD 与 `upstream/main` 同为 `5d78aec`，故上游同样没有。

新增 `.github/workflows/ci.yml`（ubuntu）：

| 步骤 | 内容 |
|---|---|
| Build | 6 个跨平台工程：Ason.Abstractions / Ason.Runner.Core / Ason / Ason.ExternalExecutor / Ason.RemoteBridge / LibDemo |
| Smoke | `LibDemo.SmokeTests` 分别以 net6.0 与 net9.0 运行 |
| Unit | `Ason.Runner.Tests`、`Ason.RemoteRunner.Tests`、`Ason.Tests`（带过滤器） |
| 过滤器 | `DisplayName!~Docker&FullyQualifiedName!~McpClientTests` |

**实测（本地跑 CI 原样命令）**：6/6 构建 exit=0；冒烟 net6.0 7/7、net9.0 7/7；Runner 1/1；RemoteRunner 1 通过 1 跳过；`Ason.Tests` **66 通过 0 失败**。

**重要修正**：此前记的"Ason.Tests 共 67 个用例"是 **`--blame-hang` 中断导致的截断计数**。用 `--list-tests` 实测真实为 **83** 个：16 个 Docker 用例 + 1 个 McpClientTests + **66 个 hermetic 用例**（过滤器恰好排除前两者，66 个全绿）。本机确认无 docker 命令，Docker 用例失败确属环境性。

未纳入 ubuntu CI（已在 workflow 内注释说明）：WPF 样例与 FlaUI UI 测试（`*-windows`）、以及需要 .NET 10 SDK 的 net10.0 腿 —— 这些属于 Windows job。

### 12.3 README 国际化

| 文件 | 行数 | 说明 |
|---|---|---|
| `README.zh-CN.md` | 419 | 简体中文 |
| `README.es.md` | 419 | 西班牙语（中性拉美） |
| `README.md` | 419 | 头部新增切换行 `**English** \| [中文](README.zh-CN.md) \| [Español](README.es.md)` |

机检（两份都过）：17 个代码块**逐字节一致**；21 个标题同层级同顺序；正文链接目标集与英文版完全一致；1 个内部锚点可解析；切换行就位。另做人眼抽查确认术语与行文自然。

**过程中修掉的两个"验证陷阱"**：

1. 中译子代理**自述**锚点为 `#ason-相较于-tool-calling--mcp-的优势`，但文件里实际是漏掉后半段的 `#...--mcp` —— 该锚点在 GitHub 上点不动。**子代理自述不能当验证**，按机检结果修正。
2. 我自己的校验脚本**也错了一版**：GitHub 的 slug 规则是*每个空格各转一个连字符、不合并*，我最初写成 `\s+`→`-`，把正确锚点误判为 BROKEN。修正后自洽。

### 12.4 仓库卫生与提交规范

- `.agents/` 已加入 `.gitignore` 并从索引剔除（原有 **31 个** `.agents/skills/**` 已 staged，会污染 PR）。当前 index 共 53 个文件，`.agents` 为 0。
- 行尾检查：`core.autocrlf=true`，暂存改动均为行级（XAML 仅 2/3、11/2、5/2 行），**无整文件重写**，PR diff 干净（53 文件 / +1957 / -79）。
- 提交规范：上游 **无 `CONTRIBUTING.md`**（经镜像取 `raw.githubusercontent.com/Alexgoon/ason/main/CONTRIBUTING.md` 返回 404），近 15 条提交均为自由句式祈使句、**无 `feat:`/`fix:` 前缀** → 建议跟随仓库风格，不用 Conventional Commits。

---

## 13. README 待补清单（本次未实施，仅记录结论与草稿）

用户决定"先不改，只要结论"。以下为差距分析 + 可直接粘贴的英文草稿，供后续一次性落地。

### 13.1 差距清单（行号基于当前 `README.md`）

| # | 位置 | 问题 | 性质 |
|---|---|---|---|
| 1 | `README.md:41-50` Quick start「3. Configure your AI provider」 | **文档与代码矛盾**：只写 `MY_OPEN_AI_KEY`，但模板/sample 已会读 `MY_OPEN_AI_BASE_URL` / `MY_OPEN_AI_MODEL`；文案只说"OpenAI 或 Ollama" | 必须改 |
| 2 | 全文无 `Ason.Abstractions` | 本次 PR 头条变更，用户无法得知"只标类型、不引运行时"的用法 | 应补 |
| 3 | `README.md:63-135` Operators 章节 | 只讲继承 `OperatorBase` 的视图型算子；本次新增的**静态算子模块**与**纯标记类自动单例**完全没写 | 应补 |
| 4 | 全文无受支持框架说明 | 库双目标 `net6.0`+`net9.0`，标记包 `netstandard2.0` | 应补（一行） |
| 5 | 无构建/测试章节 | 本次加了 CI、FlaUI 界面测试、需 Docker 的 E2E；贡献者无从得知前置条件 | 可选 |
| 6 | `README.md:178` 构造函数参数说明 | "Supports OpenAI, Azure, Gemini, Ollama, Anthropic" 可加"以及任何 OpenAI 兼容端点（如 DeepSeek）" | 可选（一句话） |

### 13.2 DeepSeek 需要写 —— 理由

1. **环境变量是魔法字符串**，不写进文档等于不存在（用户猜不到 `MY_OPEN_AI_BASE_URL`）。
2. 它是本 PR 新增的**用户可见能力**，且顺带修掉第 1 处"文档与代码矛盾"；加能力不带文档，review 大概率被要求补。
3. 读者画像相关：README 原本只往 OpenAI/Ollama 引，而 DeepSeek 是 OpenAI 接口兼容、对中文用户尤其顺手。
4. 依据：[api-docs.deepseek.com](https://api-docs.deepseek.com/) —— `base_url = https://api.deepseek.com`，模型 `deepseek-flash` / `deepseek-v4-pro`，请求路径 `<base>/chat/completions`（与 SK 连接器从 endpoint 拼出的路径一致）。

### 13.3 草稿（英文，可直接粘贴）

**草稿 A —— 放在 Quick start 顶部（对应 #4）：**

````markdown
**Requirements:** `Ason` targets **.NET 6.0** and **.NET 9.0**. The marker-only package
`Ason.Abstractions` targets **netstandard2.0**, so it can be referenced from .NET Framework 4.6.1+,
.NET Core and .NET 6+ class libraries.
````

**草稿 B —— 替换 `README.md:41-50`（对应 #1、#6，含 DeepSeek）：**

````markdown
**3. Configure your AI provider**

Any OpenAI-compatible endpoint works. The templates and samples read three environment variables:

| Variable | Purpose | Example |
|---|---|---|
| `MY_OPEN_AI_KEY` | API key (required) | `sk-...` |
| `MY_OPEN_AI_BASE_URL` | Endpoint override (optional). When unset, the connector default (`api.openai.com`) is used. | `https://api.deepseek.com` |
| `MY_OPEN_AI_MODEL` | Model id (optional, defaults to `gpt-4.1-mini`) | `deepseek-flash` |

Using **DeepSeek** (OpenAI-compatible API):

```powershell
$env:MY_OPEN_AI_KEY      = "sk-..."
$env:MY_OPEN_AI_BASE_URL = "https://api.deepseek.com"
$env:MY_OPEN_AI_MODEL    = "deepseek-flash"
```

```bash
export MY_OPEN_AI_KEY=sk-...
export MY_OPEN_AI_BASE_URL=https://api.deepseek.com
export MY_OPEN_AI_MODEL=deepseek-flash
```

Requests are sent to `<MY_OPEN_AI_BASE_URL>/chat/completions`, which matches DeepSeek's
[API docs](https://api-docs.deepseek.com/). Model names change over time — check their docs
(`deepseek-flash` / `deepseek-v4-pro` at the time of writing).

The templates create the service through the helper that ships with `Ason`:

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

With no base URL configured it falls back to the plain `OpenAIChatCompletionService(modelId, apiKey)`
constructor, so existing OpenAI-only setups keep working unchanged. You can still plug in any other
**Semantic Kernel** chat service (Azure OpenAI, Ollama, Gemini, Anthropic, …) by constructing it yourself.
````

**草稿 C —— 放在 Operators / Domain model 附近（对应 #2）：**

````markdown
### Marker attributes without the runtime (`Ason.Abstractions`)

The marker attributes live in a small package of their own:

| Package | Targets | Contains |
|---|---|---|
| `Ason` | net6.0, net9.0 | the runtime: `AsonClient`, `OperatorBase`, code generation, proxies |
| `Ason.Abstractions` | netstandard2.0 | only `AsonOperatorAttribute`, `AsonMethodAttribute`, `AsonModelAttribute` |

If a class library only needs to annotate its operators and models — for example a `netstandard2.0`
domain package — reference **`Ason.Abstractions`** instead of the full runtime. `Ason` declares type
forwards for these attributes, so already-compiled code that resolved them from `Ason.dll` keeps working.
````

**草稿 D —— Operators 章节新增子节（对应 #3）：**

````markdown
### Stateless operators

Not every operator is bound to a view. Two shapes work without any view lifecycle:

```csharp
// A static class becomes an "operator module":
// scripts call it as LibDemoStaticOperator.Add(2, 4)
[AsonOperator(description: "Stateless helpers")]
public static class LibDemoStaticOperator {
    [AsonMethod("Adds two integers")]
    public static int Add(int left, int right) => left + right;
}

// A class with a public parameterless constructor is materialised once and registered as a
// singleton, so scripts can call libDemoOperator.Echo("hi") with no AttachChildOperator call.
[AsonOperator]
public class LibDemoOperator {
    [AsonMethod]
    public string Echo(string text) => $"LibDemo received: {text}";
}
```

Both shapes are callable in every execution mode (in-process, external process, Docker, remote runner).
A marker-only class **without** a public parameterless constructor cannot be invoked, and ASON logs a
warning for it instead of failing silently.
````

**草稿 E —— 新增贡献者章节（对应 #5，可选）：**

````markdown
## Building and testing

```bash
dotnet build Ason.sln --configuration Release
```

| Suite | Notes |
|---|---|
| `tests/LibDemo.SmokeTests` | runs on net6.0 / net9.0 / net10.0 |
| `tests/Ason.Tests` | 16 Docker-mode cases need a Docker daemon; `McpClientTests` need live MCP servers |
| `tests/Ason.Runner.Tests`, `tests/Ason.RemoteRunner.Tests` | the remote-runner case is skipped unless `ASON_REMOTE_RUNNER_URL` is set |
| `tests/WpfDemoApp.UiTests` | FlaUI UI tests — need a **Windows desktop session** |

CI (`.github/workflows/ci.yml`) builds the cross-platform projects and runs the hermetic tests on
Ubuntu; the WPF sample, the UI tests and the net10.0 leg are Windows-only.
````

### 13.4 落地时必须注意：三语同步

README 现有三份（`README.md` / `README.zh-CN.md` / `README.es.md`，各 419 行）。**英文一改，两份翻译立刻开始漂移**，因此：

1. 先定英文，再把同样章节插入两份翻译的**相同位置**（标题层级保持一致，锚点按 GitHub slug 规则生成：每个空格各转一个连字符、**不合并**）。
2. 改完用现成脚本机检：`F:\TEMP\verify-readme.ps1`（行数/标题数/链接目标集/锚点可解析/切换行）与 `F:\TEMP\verify-readme-code.ps1`（代码块逐字节比对）。两份脚本在本次会话中已修正过 slug 与语言切换行的误报。
3. 若时间不允许同步翻译，应在 PR 描述里明确"翻译版落后一版"，而不是让静默漂移。

### 13.5 覆盖度自查（修正 13.1 与"本 PR 范围"的差额）

用户追问"前面说的 5 处都覆盖了吗"，核对结论：**没有全覆盖**。此前给出的"本 PR 只做两件事（① 修 §3、② 新增能力两段）"实际只覆盖 #1–#3。

| # | 待改项 | 草稿 | 原"本 PR"方案 | 修正后处置 |
|---|---|---|---|---|
| 1 | §3 环境变量矛盾 + DeepSeek（`README.md:41-50`） | B | 覆盖 | 本 PR 内联（3 语） |
| 2 | `Ason.Abstractions` 未文档化 | C | 覆盖 | 本 PR 内联（3 语） |
| 3 | 静态算子模块 / 纯标记单例（`README.md:63-135`） | D | 覆盖 | 本 PR 内联（3 语） |
| 4 | 无受支持框架说明 | A | **漏** | **本 PR 应补**（3 行，高价值） |
| 5 | 无构建/测试章节（贡献者向） | E | 推到 docs | 刻意延后 → `docs/testing.md`（docs 方案首批迁移对象） |
| — | `README.md:178` 补"任何 OpenAI 兼容端点（如 DeepSeek）" | **缺** | 未提 | **本 PR 应补**（一行）→ 见下方草稿 F |

**修正后的本 PR 范围**：#1 + #2 + #3 + #4（3 语）+ 草稿 F（英文 1 行，两份译文各 1 行）；#5 留给 docs。
（规模估算：英文约 45–55 行改动，中/西各约 45–55 行；用 §13.4 的两个脚本机检。）

**草稿 F —— 修订 `README.md:178` 该条参数说明（末尾追加一句）：**

````markdown
- `IChatCompletionService defaultChatCompletion` — A chat completion service used by ASON agents. Supports OpenAI, Azure, Gemini, Ollama, Anthropic, **and any OpenAI-compatible endpoint such as DeepSeek** (see step 3 of the Quick start above), plus other providers supported by Semantic Kernel (see: [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp)).
````

⚠️ **不要**给这句加 `#3-configure-your-ai-provider` 之类的内部锚点：`README.md:41` 的「**3. Configure your AI provider**」是**加粗行而非 markdown 标题**，GitHub 不会为它生成锚点（全文因此只有 1 个内部锚点）。若确实想要可点链接，需先把该行改成 `### 3. Configure your AI provider`（会改动 Quick start 的排版风格，属可选动作）。

**译文对应注意**：若确实新增了内部锚点，译文中必须改用**译后标题的 slug**（GitHub 规则：每个空格各转一个连字符、**不合并**，保留中日韩字符与重音字母）—— 这正是 §12.3 里踩过的坑。

---

## 14. 文档重构：`docs/` 目录 + 三语文档（2026-09-22）

### 14.1 决策与文件地图

按**变更频率**而非长度拆分；篇数压到 6 篇以控制三语成本；用户选择 **6 篇全三语**。

| 文件 | 内容 | 语言 |
|---|---|---|
| `docs/ai-providers.md` | 三环境变量契约、DeepSeek、其他 OpenAI 兼容端点、SK 替代品、排错 | 三语 |
| `docs/operators.md` | Operators 全章 + Root operator/关系 + Domain model + **无状态算子** + **`Ason.Abstractions`** | 三语 |
| `docs/configuration.md` | 构造参数 + `AsonClientOptions` 全量表 + `AddAson` + Logging | 三语 |
| `docs/execution-modes.md` | 四种执行模式 + 远程执行 + 选型表 | 三语 |
| `docs/architecture.md` | How ASON works + agents + Benefits 全文（两张图） | 三语 |
| `docs/contributing.md` | 构建、测试矩阵、Windows/Docker 前置、环境变量、CI | 三语 |
| `README.md` | 入口：简介 / Requirements / Quick start（自足）/ 概览 / 最小算子示例 / Documentation 索引 / Benefits 摘要 | 三语 |

命名约定：同目录后缀式（`x.md` / `x.zh-CN.md` / `x.es.md`）；docs 内图片路径为 `../images/...`。

### 14.2 做法与结果

脚本按行区间**逐字搬运**（不重打、不改写），再用占位符填新内容：
`%TEMP%\split-readme.ps1`（提取 + 组装 README）→ `fill-readme.ps1`（填 14 处占位）→ `fix-mojibake.ps1`（修编码 + 4 处内容修正）→ `verify-docs.ps1` / `verify-i18n.ps1`（机检）。

结果：README 419 → **161** 非空行；6 篇 docs（44–97 非空行）；10 个搬运区间**逐字重现**；**内容无损 0 行丢失**；跨文件链接 0 坏、锚点 0 坏。

### 14.3 顺带修掉的内容问题

| 问题 | 处理 |
|---|---|
| §3 只写 `MY_OPEN_AI_KEY`，与代码矛盾 | 三环境变量表 + DeepSeek（PowerShell/Bash）+ 工厂示例 + 指向 `docs/ai-providers.md` |
| 无受支持框架说明 | 新增 Requirements（net6.0/net9.0 + netstandard2.0） |
| 无状态算子 / 纯标记单例未文档化 | README 最小示例 + `docs/operators.md` 完整小节 |
| `Ason.Abstractions` 未文档化 | `docs/operators.md` 小节（含类型转发与向后兼容） |
| 构造函数参数漏了"任何 OpenAI 兼容端点" | 补 DeepSeek 具名提及（草稿 F） |
| **搬走后失效的交叉引用** | "See the next section for configuration details." 原文下一节已变成 MCP → 改为指向 `docs/execution-modes.md` |

### 14.4 两个必须记住的坑

**坑 A（严重）：Windows PowerShell 5.1 把无 BOM 的 UTF-8 脚本按 GBK 解码。**
我脚本里**字面量**的 `—` 与 `中文`/`Español` 全被写坏（`鈥?` / `涓枃` / `Espa帽ol`）；而**从 README 按 UTF-8 读入后搬运的内容完全正常**，用 write 工具直接写的两篇 doc 也正常 —— 这三点正好定位了根因。
对策：**PowerShell 脚本只用 ASCII**，非 ASCII 用 `[char]0x2014` / `[char]0x4E2D` 这类码点构造。

**坑 B：校验器本身也会错。** 原 README 是 CRLF、重建文档是 LF → 行尾 `\r` 使内容比对全失配，假报"278/285 行丢失"；图片路径有意改成 `../images/` → 2 行误报。两处归一化后均为 0。
与 §12.3「我自己的 slug 实现错了」属同一类教训：**校验器必须被校验**。
