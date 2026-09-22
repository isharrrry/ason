# Architecture, agents and why ASON

[English](architecture.md) | [中文](architecture.zh-CN.md) | [Español](architecture.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

## How ASON works

Below is a simplified overview of the ASON architecture.

![ASON Flow Overview](../images/flow-overview.jpg)
1. Your application defines **operators** that execute methods in your code.  
2. The **ASON Client** creates APIs (operator signatures) and passes them to the **Script Agent** along with the user’s task.  
3. The **Script Agent** generates a script and sends it to the **execution environment** (same process, external process, Docker container, or remote server).  
4. The **execution environment** calls dynamically generated proxies that invoke real operator methods.


> How operators are declared and attached is described in [writing operators](operators.md).

## ASON agents

ASON uses multiple internal AI agents to coordinate the workflow, generate scripts, extract data, and explain results:

- **Reception Agent** – Accepts the conversation thread and prepares input information for the Script Agent.  
  You can skip it by setting `AsonClientOptions.SkipReceptionAgent` to `true`.

- **Script Agent** – Generates the script based on the user task and available API.

- **Extractor Agent** – Extracts data from unstructured text.  
  The Script Agent can use it when a task involves parsing text, such as extracting a company name from an email.  
  Enable it with `OperatorBuilder.AddExtractor`.

- **Explainer Agent** – Explains the results of an executed script based on the original user request.  
  You can disable it by setting `AsonClientOptions.SkipExplainerAgent` to `true`.


## Benefits of ASON over Tool Calling / MCP

### Flexible logic

ASON can handle more complex workflows than traditional tool-calling systems or standalone MCP servers.  
It provides access to your model entities and can chain multiple method calls in a single AI-generated script.

**Example scenario:**  A user asks your system to update the status of all leads created before June 2024 to *Inactive*.

#### Traditional approach (MCP / Tool Calling)

You would typically define specialized methods such as `GetLeadsBeforeDate(date)` and `UpdateLeadsStatus(leadIds, newStatus)`.  

1. The user’s request is passed to the LLM along with all available tools.  
2. The LLM analyzes the request and available tools, then decides to call `GetLeadsBeforeDate` with a specific date as a parameter.  
3. Your application executes the `GetLeadsBeforeDate` method.  
4. The results are passed back to the LLM (the entire collection returned by `GetLeadsBeforeDate`).  
5. The LLM then decides to call `UpdateLeadsStatus` using the IDs obtained from the previous step.  
6. Your application executes the `UpdateLeadsStatus` method.  
7. Finally, the LLM generates the text output.  

If the user later modifies their request, these methods may no longer fit — requiring you to add new ones or make existing ones more generic, which increases the risk of invalid LLM-generated input. Also note that in step 4, the entire result set must be passed to the LLM so it can construct parameters for step 5.



#### ASON approach

ASON dynamically generates and executes a script to complete the task. For example, it might retrieve leads created before a certain date, update their status to *Inactive*, and then save changes back using available model methods. 

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
 
ASON understands your model (for example, `Lead`) and available operations (such as `GetLeads` and `UpdateLead`), allowing the AI to compose flexible logic on the fly.


### Performance

With traditional MCP or tool-calling systems, **each tool call triggers a separate LLM request**.  
For example, if you expose a `DeleteOrder` method and a user asks to delete 10 orders, your client must make at least 10 LLM round trips — significantly impacting performance.

ASON, by contrast, generates a single script and executes it **without further LLM involvement**. 

```csharp
var lastOrders = ordersOperator.GetOrders()
    .OrderByDescending(o => o.CreatedDate)
    .Take(10);

foreach (var order in lastOrders)
{
    ordersOperator.DeleteOrder(order);
}
```

It can, for instance, obtain a list of recent orders and delete them all in one locally executed script.

This reduces network latency and token usage while improving throughput for large-scale operations.

![ASON Performance Benefits](../images/ason-performance.png)

### Token usage and API capacity

AI services typically charge based on token consumption.  
Traditional tool-calling mechanisms often waste tokens due to the following reasons:

- **Large data processing:**  
  When an AI tool (for example, *GetOrders*) returns thousands of records, the LLM must handle all the data, quickly consuming token limits.
- **Full tool descriptions on each request:**  
  Each tool call includes the full definition and description of every available tool, even if most are unused.

ASON eliminates both issues.  
Once a script is generated, it executes locally — **without additional LLM calls** — allowing unlimited data processing without extra token costs.

ASON also provides a **compact API model** where the LLM receives concise method signatures represented as code. 

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

Since LLMs are trained on programming syntax, they easily understand and operate with such APIs — enabling robust, context-aware script generation even for APIs with hundreds of methods.
