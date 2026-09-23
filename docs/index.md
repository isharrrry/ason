# Documentation index

[English](index.md) | [中文](index.zh-CN.md) | [Español](index.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

Every guide exists in English, Chinese and Spanish. The topic table below links to all three language versions of each document.

| Topic | Language versions | What it covers |
|---|---|---|
| **AI providers** | [English](ai-providers.md) · [中文](ai-providers.zh-CN.md) · [Español](ai-providers.es.md) | Environment variables, DeepSeek and other OpenAI-compatible endpoints, Azure / Ollama / Gemini / Anthropic through Semantic Kernel, troubleshooting |
| **Writing operators** | [English](operators.md) · [中文](operators.zh-CN.md) · [Español](operators.es.md) | `OperatorBase`, root operators, the `GetViewOperator` / `AttachChildOperator` lifecycle, stateless modules, `[AsonModel]`, `Ason.Abstractions` |
| **Client configuration** | [English](configuration.md) · [中文](configuration.zh-CN.md) · [Español](configuration.es.md) | `AsonClient` constructor, the complete `AsonClientOptions` reference, `AddAson` registration, prompt overrides, logging |
| **Execution modes** | [English](execution-modes.md) · [中文](execution-modes.zh-CN.md) · [Español](execution-modes.es.md) | In-process, external process, Docker and remote execution, the five deployment combinations, and which configuration fits which application shape |
| **Application / agent separation** | [English](app-agent-separation.md) · [中文](app-agent-separation.zh-CN.md) · [Español](app-agent-separation.es.md) | Splitting an application that only holds `[Ason*]` operators and a bridge from the agent that drives it: the manifest, the script and single-function interfaces, the gRPC, MCP and HTTP/OpenAPI adapters, execution location, security, and driving the same endpoints from an ordinary program with no model |
| **Architecture and agents** | [English](architecture.md) · [中文](architecture.zh-CN.md) · [Español](architecture.es.md) | How ASON works, the deployment topology and process boundaries, credentials, the internal agents, and the comparison with tool calling / MCP |
| **Contributing** | [English](contributing.md) · [中文](contributing.zh-CN.md) · [Español](contributing.es.md) | Building, the test suites, CI, and what needs Windows or Docker |

## Where to start

| If you want to… | Go to |
|---|---|
| get something running quickly | [README quick start](../README.md#quick-start) |
| point ASON at DeepSeek or another OpenAI-compatible endpoint | [AI providers](ai-providers.md) |
| expose your own methods to the agent | [Writing operators](operators.md) |
| understand what the internal agents do | [Architecture and agents](architecture.md) |
| build and test the repository | [Contributing](contributing.md) |
| pick a mode and a deployment for my application | [Execution modes](execution-modes.md) |
| drive my running application from a test, a CI job, another program — no model involved | [Application / agent separation](app-agent-separation.md#using-the-bridge-without-an-agent) |
| run a working example of every shape, single-process or split | [Samples and how to run them](app-agent-separation.md#samples-and-how-to-run-them) |
