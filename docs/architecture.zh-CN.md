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

ASON 能够处理比传统 tool-calling 系统或独立的 MCP server 更复杂的工作流。  
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
