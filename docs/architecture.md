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

## Deployment topology

`ExecutionMode` and `UseRemoteRunner` are independent settings: the first says *how* the generated
code is isolated, the second says *where* the script host lives. Together they produce five real link
paths across three process boundaries.

<!-- i18n: localize-labels - translate the labels, keep the structure (numbering, arrows, indentation) -->

```
[1] Client host  (your app: AsonClient, RootOperator, operators, LLM agents, MCP clients)
      |
      |  boundary A: stdio, one JSON line per message          (local)
      |  boundary B: SignalR carrying the same JSON lines      (remote)
      |
      +--> [2] Client-side external executor               local ExternalProcess / Docker
      |        Ason.ExternalExecutor child process on the client machine
      |        (Docker mode: the child is "docker run --rm -i <image>")
      |
      +--> [3] Remote runner service                       remote
               ASP.NET Core + /scriptRunnerHub  (Ason.RemoteBridge)
                  |
                  |  boundary C: identical stdio protocol, initiated by the server
                  |
                  +--> [4] Server-side external executor     remote ExternalProcess / Docker
                  |        Ason.ExternalExecutor child process on the server
                  |
                  +--> [4'] Server in-process evaluation      remote InProcess
                           ScriptExecutor runs inside the web server process

Operator calls always travel back to [1]: the script calls an operator, the invocation crosses the
boundary or boundaries back to your process, the real method runs there, and the result returns.
```

| Boundary | Protocol | Direction | Owned by |
|---|---|---|---|
| A: client ↔ client-side executor | ASON JSON messages over the child's stdin/stdout | both ways (script down, operator calls up) | the client (`ScriptRunnerProcessHost`; the process tree is killed on dispose) |
| B: client ↔ remote runner service | the same JSON messages inside SignalR, plus a `StartRunner` handshake | both ways, including server-initiated callbacks (`OnRunnerMessage`, `OnRunnerClosed`) | the server (one session per connection, idle sessions reclaimed); the client reconnects automatically |
| C: remote runner service ↔ server-side executor | the identical stdio protocol of boundary A | both ways | the server (reuses the same `ScriptRunnerProcessHost`) |

Because the script host only ever receives *text* — the generated script — and always calls back for
operators, both transports share one message set (`exec`, `execResult`, `invoke`, `invokeResult`,
`log`, `mcpInvoke`, …). Moving the script host therefore does **not** move your operators or your data;
[execution modes](execution-modes.md) covers how to pick a configuration.

### Credentials and data boundaries

| Thing | Where it lives | Crosses a boundary? |
|---|---|---|
| LLM API key and model configuration | the client host only | no |
| MCP client credentials | the client host only | no |
| Business data reachable through operators | the client host only | no, apart from the arguments and results of the operator calls themselves |
| Generated script text (`exec`) | produced on the client | yes — it is sent to the script host |
| Log messages, operator arguments and results | both sides | yes |

The non-client components have no LLM dependency: `Ason.RemoteBridge`, `Ason.ExternalExecutor` and
`Ason.Runner.Core` contain no chat-completion code, and the executor only needs Roslyn. The extractor
agent is itself an operator, so even its model call happens on the client.

Practical consequences:

- a remote runner host does **not** need outbound access to your model provider, and a desktop client
  does not need a local runtime or Docker for the script host;
- credentials stay wherever your operators live, so an LLM or HTTP call placed inside an operator
  keeps its secrets on the client;
- anything that reaches the model's context can end up inside the script text, and that text does
  cross the boundary — choose what operator return values you expose to agents accordingly.

> Static analysis (`AsonClientOptions.ForbiddenScriptKeywords`, which denies `System.IO`,
> `Process.Start`, `System.Reflection`, `Environment.GetEnvironmentVariable`, …) is a keyword filter,
> not a sandbox. In **In-process** mode the script runs inside your own process, so that filter is the
> only barrier — which is why In-process is not recommended for untrusted input.

### Application / agent split (the bridge)

[Application / agent separation](app-agent-separation.md) describes a second topology this document's
client/host split does not cover: the operators stay in an application, while the model and the orchestration
live in a separate agent process. The bridge (`Ason.Bridge` plus one adapter per transport) publishes the
operator API as a manifest and forwards execution over gRPC, MCP or HTTP/OpenAPI.

<!-- i18n: localize-labels - translate the labels, keep the structure (numbering, arrows, indentation) -->

```
Case 1 - a .NET agent that keeps ASON's own orchestration

[1] Agent host      model, prompts, orchestration; AsonClient, no operators at all
      |
      |  boundary D: gRPC / MCP / HTTP-OpenAPI
      |    --> manifest (what can be called), "exec" (the script text), JSON arguments
      |    <-- results and logs
      |
      +--> [2] Application host   operators, data, UI; Ason.Bridge runtime + one adapter per transport
                |
                |  boundary A': the identical stdio protocol, initiated by the application
                |    1. exec          the script text              [2] --> [3]
                |    2. invoke        target, method, arguments    [3] --> [2]   the call comes back
                |    3. invokeResult  the real method ran in [2]   [2] --> [3]
                |    4. execResult    what the script returned     [3] --> [2]
                |    5. over D: the result (and the logs) travels back to [1]
                |
                +--> [3] Application-side executor       ExternalProcess / Docker
                         Ason.ExternalExecutor child process of the application

Case 2 - an agent that speaks MCP over HTTP (no relay, no chaining)

[1] MCP agent      Claude Desktop, an IDE, any HTTP MCP client
      |
      |  Streamable HTTP: ason_get_manifest, ason_execute_script, ason_invoke_function, ...
      v
[2] Application host   Ason.Bridge.Mcp

Case 3 - an agent that can only start a stdio MCP server

[1] stdio-only MCP agent      it started the relay as its MCP server
      |
      |  stdio, MCP protocol
      v
[3] Relay process   Ason.Bridge.McpHost - owns no operators of its own
      |
      |  boundary E: gRPC (--transport grpc) or MCP (--transport mcp)
      v
[2] Application host

Case 4 - a generic HTTP client

[1] HTTP client      curl, Postman, Swagger UI
      |
      |  GET /ason/manifest, GET /ason/openapi.json,
      |  POST /ason/script, POST /ason/functions/{operator}/{method}
      v
[2] Application host   Ason.Bridge.OpenApi      optional: shared key header

Case 5 - an ordinary program with no model (test harness, CI step, automation, another service)

[1] Caller program   gRPC client, HTTP client or McpAsonBridgeClient - no model, no prompts, no scripts written
      |              by a model: the program composes the calls itself
      |
      |  boundary D again: manifest (discovery), invokeFunction, optionally a script, logs
      |
      +--> [2] Application host   the same endpoints the agents use

Case 5 is case 1 with a different caller: nothing in the bridge is agent-specific. The program can read the
manifest to discover what is callable, call one operator method, or run a script it wrote itself - and it keeps
all of the determinism an automation wants. See the guide's section on
[using the bridge without an agent](app-agent-separation.md#using-the-bridge-without-an-agent).

Case 6 - the same application, with a remote script host (the manifest reports remote-runner)

[2] Application host   operators, data, UI; Ason.Bridge runtime + one adapter per transport
      |
      |  boundary B: SignalR to the runner host, carrying the same JSON lines as boundary A
      v
[R] Remote runner host   ASP.NET Core /scriptRunnerHub  (Ason.RemoteBridge; samples/RemoteRunnerService)
      |
      |  boundary C: the identical stdio protocol, initiated by the server
      |
      +--> [R'] Server-side executor      ExternalProcess / Docker on the runner host
               (or the server evaluates the script in its own process, InProcess there)

Operator calls travel the whole way back ([R'] -> B -> [2]), where the real method runs. Nothing else changes:
the same adapters, the same manifest - only the value of `execution` differs.

Case 7 - any caller above, watching the logs (the logStream capability)

[caller]  -- StreamExecution (gRPC) / POST /script/stream (SSE) / ason_stream_script (MCP) -->  [2] Application host
                                                                                                    |
    log* events, then exactly one result | error  <--- the executor raises them in [2] -------------+
        |
        +-- through a relay ([R]) the same stream travels boundary E onward: a relay has no executor,
            so it collects the application's stream instead of having logs of its own

Only the script text and its result cross boundary D, the caller-facing one; operator calls never leave [2],
where the real methods and the data are. Where the script itself is evaluated (in the application process, in
its own Ason.ExternalExecutor, in a container or on a remote runner) is the application's decision.
```

| Boundary | Protocol | Direction | Owned by |
|---|---|---|---|
| D: caller ↔ application (agent, test harness, CI step, another service) | gRPC, MCP or HTTP/OpenAPI, carrying the manifest, `exec` requests and function calls | both ways (script and arguments down, results up) | the application, which hosts the endpoints; the caller is only a client |
| E: relay ↔ application | one of D's transports, selected with `--transport` | both ways | the application, exactly as for D; the relay borrows it |
| A': application ↔ its own executor (optional) | the identical stdio protocol of boundary A above | both ways | the application (`ScriptRunnerProcessHost`) |

Two hops appear in case 3 only, and only because stdio MCP means "the client spawns the server": a running
application cannot be that child, so the relay owns the pipe and needs one channel back to the application.

For credentials the rule does not change, it moves with the operators: the model key stays where the model is
(the agent) and operator data stays where the operators are (the application). What *does* change is that the
execution surface is now reachable over the network, so bridge endpoints are privileged — that guide's
security section covers what to do about it.

### UI thread affinity

Operator methods are always invoked through the `SynchronizationContext` captured when the `AsonClient`
is constructed. In a WPF application that means every operator call runs on the UI thread, locally and
remotely alike, so operators can touch UI-bound objects without extra marshalling. Construct the
`AsonClient` on the UI thread (as the sample does) for this to hold.

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
