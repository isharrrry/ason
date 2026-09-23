# 架构、代理以及为什么选择 ASON

[English](architecture.md) | **中文** | [Español](architecture.es.md)

> 属于 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## ASON 如何工作

以下是 ASON 架构的简化概览。

![ASON Flow Overview](../images/flow-overview.jpg)
1. 你的应用程序定义 **operator**，它们执行你代码中的方法。  
2. **ASON Client** 创建 API（operator 签名），并将它们连同用户的任务一起传递给 **Script Agent**。  
3. **Script Agent** 生成脚本，并将其发送到**执行环境**（同一进程、外部进程、Docker 容器或远程服务器）。  
4. **执行环境**调用动态生成的代理，由这些代理调用真正的 operator 方法。


> operator 如何声明和附加，在[编写 operator](operators.zh-CN.md)中有详细描述。

## 部署拓扑

`ExecutionMode` 与 `UseRemoteRunner` 是彼此独立的设置：前者说明生成的代码*如何*被隔离，后者说明脚本宿主*位于何处*。二者组合起来，会在三个进程边界之上形成五条真实的链路。

<!-- i18n: localize-labels - 标签本地化，保留结构（编号、箭头、缩进） -->

```
[1] 客户端宿主  （你的应用：AsonClient、RootOperator、算子、LLM agents、MCP 客户端）
      |
      |  边界 A：stdio，一条消息一行 JSON                     （本地）
      |  边界 B：SignalR，承载同样的 JSON 行                   （远程）
      |
      +--> [2] 客户端侧外部执行程序                    本地 ExternalProcess / Docker
      |        客户端机器上的 Ason.ExternalExecutor 子进程
      |        （Docker 模式：这个子进程是 "docker run --rm -i <镜像>"）
      |
      +--> [3] 远程运行器服务                          远程
               ASP.NET Core + /scriptRunnerHub  (Ason.RemoteBridge)
                  |
                  |  边界 C：与边界 A 完全相同的 stdio 协议，由服务器发起
                  |
                  +--> [4] 服务器侧外部执行程序          远程 ExternalProcess / Docker
                  |        服务器上的 Ason.ExternalExecutor 子进程
                  |
                  +--> [4'] 服务器进程内求值              远程 InProcess
                           ScriptExecutor 在 Web 服务器进程内运行

operator 调用始终回到 [1]：脚本调用某个 operator，该调用跨越一条或多条边界回到你的进程，
真实方法在那里执行，然后结果返回。
```

| 边界 | 协议 | 方向 | 由谁掌控 |
|---|---|---|---|
| A：客户端 ↔ 客户端侧执行器 | 通过子进程 stdin/stdout 传递的 ASON JSON 消息 | 双向（脚本下行，operator 调用上行） | 客户端（`ScriptRunnerProcessHost`；进程树会在 dispose 时被终止） |
| B：客户端 ↔ 远程运行器服务 | 封装在 SignalR 中的同一批 JSON 消息，外加一次 `StartRunner` 握手 | 双向，包括服务器发起的回调（`OnRunnerMessage`、`OnRunnerClosed`） | 服务器（每个连接一个会话，空闲会话会被回收）；客户端会自动重连 |
| C：远程运行器服务 ↔ 服务器侧执行器 | 与边界 A 完全相同的 stdio 协议 | 双向 | 服务器（复用同一个 `ScriptRunnerProcessHost`） |

因为脚本宿主接收到的始终只有*文本* —— 也就是生成的脚本 —— 并且总是回调获取 operator，所以两种传输方式共用同一套消息（`exec`、`execResult`、`invoke`、`invokeResult`、`log`、`mcpInvoke` 等）。因此移动脚本宿主并**不会**移动你的 operator 或数据；[执行模式](execution-modes.zh-CN.md)介绍了如何选择一种配置。

### 凭据与数据边界

| 内容 | 所在位置 | 是否跨越边界？ |
|---|---|---|
| LLM API 密钥与模型配置 | 仅客户端主机 | 否 |
| MCP 客户端凭据 | 仅客户端主机 | 否 |
| 可经 operator 访问的业务数据 | 仅客户端主机 | 否，operator 调用本身的参数与结果除外 |
| 生成的脚本文本（`exec`） | 在客户端产生 | 是 —— 它会被发送到脚本宿主 |
| 日志消息、operator 参数与结果 | 双方 | 是 |

非客户端组件不依赖 LLM：`Ason.RemoteBridge`、`Ason.ExternalExecutor` 和 `Ason.Runner.Core` 中不包含任何聊天补全代码，执行器也只需要 Roslyn。extractor agent 本身就是一个 operator，因此连它的模型调用都发生在客户端。

实际影响：

- 远程运行器主机**不需要**能够出站访问你的模型提供商，桌面客户端也不需要为脚本宿主准备本地运行时或 Docker；
- 凭据始终留在 operator 所在之处，因此放在 operator 内部的 LLM 或 HTTP 调用会把密钥保留在客户端；
- 任何进入模型上下文的内容都可能出现在脚本文本中，而该文本确实会跨越边界 —— 因此要据此选择你向 agent 暴露哪些 operator 返回值。

> 静态分析（`AsonClientOptions.ForbiddenScriptKeywords`，它会拒绝 `System.IO`、
> `Process.Start`、`System.Reflection`、`Environment.GetEnvironmentVariable` 等）只是关键字过滤，
> 而不是沙箱。在 **In-process** 模式下，脚本在你自己的进程内运行，所以该过滤器是唯一的屏障 ——
> 这也是不建议将 In-process 用于不可信输入的原因。

### 应用 / Agent 分离（桥）

[应用 / Agent 分离](app-agent-separation.zh-CN.md)描述了本文客户端/宿主划分之外的第二种拓扑：operator 留在应用侧，
而模型与编排位于独立的 Agent 进程。桥（`Ason.Bridge` + 每种传输一个适配器）把 operator API 以清单形式发布出去，
并通过 gRPC、MCP 或 HTTP/OpenAPI 转发执行，因此边界从一个变成两个：

| 边界 | 协议 | 跨越什么 |
|---|---|---|
| Agent ↔ 应用 | gRPC、MCP 或 HTTP/OpenAPI，承载清单、`exec` 请求与函数调用 | 生成的脚本文本、结果，以及单函数调用的参数与返回值 |
| 应用 ↔ 它自己的执行器（可选） | 当应用运行 `Ason.ExternalExecutor` 时，与边界 A 相同的 stdio 协议 | 仅脚本文本 |

凭据规则并未改变，而是**随 operator 一起移动**：模型密钥留在模型所在处（Agent），operator 数据留在 operator 所在处
（应用）。真正改变的是：执行面从此可经网络到达，因此桥端点是特权端点 —— 处理方式见该指南的安全章节。

### UI 线程关联性

operator 方法始终通过构造 `AsonClient` 时所捕获的 `SynchronizationContext` 来调用。在 WPF 应用程序中，这意味着每次 operator 调用都在 UI 线程上运行 —— 无论本地还是远程都是如此 —— 因此 operator 无需额外封送即可操作与 UI 绑定的对象。要让这一点成立，请在 UI 线程上构造 `AsonClient`（示例正是这样做的）。

## ASON 代理

ASON 使用多个内部 AI 代理来协调工作流、生成脚本、提取数据并解释结果：

- **Reception Agent** – 接收对话线程，并为 Script Agent 准备输入信息。  
  你可以通过将 `AsonClientOptions.SkipReceptionAgent` 设置为 `true` 来跳过它。

- **Script Agent** – 根据用户任务和可用 API 生成脚本。

- **Extractor Agent** – 从非结构化文本中提取数据。  
  当任务涉及解析文本时（例如从电子邮件中提取公司名称），Script Agent 可以使用它。  
  通过 `OperatorBuilder.AddExtractor` 启用它。

- **Explainer Agent** – 根据原始用户请求解释已执行脚本的结果。  
  你可以通过将 `AsonClientOptions.SkipExplainerAgent` 设置为 `true` 来禁用它。


## ASON 相对于 Tool Calling / MCP 的优势

### 灵活的逻辑

ASON 能够处理比传统 tool-calling 系统或独立的 MCP 服务器更复杂的工作流。  
它提供对你模型实体的访问，并可以在单个 AI 生成的脚本中串联多个方法调用。

**示例场景：** 用户要求你的系统将 2024 年 6 月之前创建的所有潜在客户的状态更新为 *Inactive*。

#### 传统方式（MCP / Tool Calling）

你通常会定义专门的方法，例如 `GetLeadsBeforeDate(date)` 和 `UpdateLeadsStatus(leadIds, newStatus)`。  

1. 用户的请求连同所有可用工具一起传递给 LLM。  
2. LLM 分析请求和可用工具，然后决定以一个具体日期作为参数调用 `GetLeadsBeforeDate`。  
3. 你的应用程序执行 `GetLeadsBeforeDate` 方法。  
4. 结果被传回 LLM（`GetLeadsBeforeDate` 返回的整个集合）。  
5. 然后 LLM 决定使用上一步获得的 ID 调用 `UpdateLeadsStatus`。  
6. 你的应用程序执行 `UpdateLeadsStatus` 方法。  
7. 最后，LLM 生成文本输出。  

如果用户之后修改了他们的请求，这些方法可能不再适用 —— 你需要添加新方法，或者让现有方法更通用，而这会增加 LLM 生成无效输入的风险。另请注意，在第 4 步中，必须将整个结果集传递给 LLM，以便它为第 5 步构造参数。



#### ASON 方式

ASON 动态生成并执行一个脚本以完成任务。例如，它可能会检索在某个日期之前创建的潜在客户，将他们的状态更新为 *Inactive*，然后使用可用的模型方法保存更改。

```csharp
var cutoffDate = new DateTime(2024, 6, 1);

leadsOperator.GetLeads()
    .Where(lead => lead.CreatedDate < cutoffDate)
    .ToList()
    .ForEach(lead =>
    {
        lead.Status = "Inactive";
        leadsOperator.UpdateLead(lead);
    });
```
 
ASON 理解你的模型（例如 `Lead`）和可用操作（例如 `GetLeads` 和 `UpdateLead`），从而让 AI 能够即时组合出灵活的逻辑。


### 性能

在使用传统 MCP 或 tool-calling 系统时，**每次 tool call 都会触发一次单独的 LLM 请求**。  
例如，如果你暴露一个 `DeleteOrder` 方法，而用户要求删除 10 个订单，你的客户端必须至少进行 10 次 LLM 往返 —— 这会显著影响性能。

相比之下，ASON 只生成一个脚本，并在**无需 LLM 进一步参与**的情况下执行它。

```csharp
var lastOrders = ordersOperator.GetOrders()
    .OrderByDescending(o => o.CreatedDate)
    .Take(10);

foreach (var order in lastOrders)
{
    ordersOperator.DeleteOrder(order);
}
```

例如，它可以获取最近订单的列表，并在一个本地执行的脚本中全部删除它们。

这减少了网络延迟和 token 用量，同时提升大规模操作的吞吐量。

![ASON Performance Benefits](../images/ason-performance.png)

### Token 用量与 API 容量

AI 服务通常根据 token 消耗量收费。  
传统的 tool-calling 机制常常因为以下原因浪费 token：

- **大数据处理：**  
  当 AI 工具（例如 *GetOrders*）返回数千条记录时，LLM 必须处理所有数据，从而迅速消耗 token 上限。
- **每次请求都携带完整的工具描述：**  
  每次 tool call 都包含每个可用工具的完整定义和描述，即使其中大部分并未被使用。

ASON 消除了这两个问题。  
脚本一旦生成，就会在本地执行 —— **无需额外的 LLM 调用** —— 从而可以无限制地处理数据，而不会产生额外的 token 成本。

ASON 还提供**紧凑的 API 模型**，其中 LLM 接收到的是以代码表示的简洁方法签名。

```csharp
public class BarValue
{
    public string Caption;
    public double Value;
}

public class ChartsViewOperator
{
    private ChartsViewOperator();
    public void CreateBarChart(BarValue[] barValues, string xAxisCaption, string yAxisCaption);
}
```

由于 LLM 是在编程语法上训练的，它们能够轻松理解并使用这样的 API —— 即使 API 包含数百个方法，也能生成健壮且具备上下文感知能力的脚本。
