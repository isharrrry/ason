# Documentation index

[English](index.md) | [中文](index.zh-CN.md) | [Español](index.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

Every guide exists in English, Chinese and Spanish. The topic table below links to all three language versions of each document.

| Topic | Language versions | What it covers |
|---|---|---|
| **AI providers** | [English](ai-providers.md) · [中文](ai-providers.zh-CN.md) · [Español](ai-providers.es.md) | Environment variables, DeepSeek and other OpenAI-compatible endpoints, Azure / Ollama / Gemini / Anthropic through Semantic Kernel, troubleshooting |
| **Writing operators** | [English](operators.md) · [中文](operators.zh-CN.md) · [Español](operators.es.md) | `OperatorBase`, root operators, the `GetViewOperator` / `AttachChildOperator` lifecycle, stateless modules, `[AsonModel]`, `Ason.Abstractions` |
| **Client configuration** | [English](configuration.md) · [中文](configuration.zh-CN.md) · [Español](configuration.es.md) | `AsonClient` constructor, the complete `AsonClientOptions` reference, `AddAson` registration, prompt overrides, logging |
| **Execution modes** | [English](execution-modes.md) · [中文](execution-modes.zh-CN.md) · [Español](execution-modes.es.md) | In-process, external process, Docker and remote execution, and the security model behind each |
| **Architecture and agents** | [English](architecture.md) · [中文](architecture.zh-CN.md) · [Español](architecture.es.md) | How ASON works, the internal agents, and the detailed comparison with tool calling / MCP |
| **Contributing** | [English](contributing.md) · [中文](contributing.zh-CN.md) · [Español](contributing.es.md) | Building, the test suites, CI, and what needs Windows or Docker |

## Where to start

| If you want to… | Go to |
|---|---|
| get something running quickly | [README quick start](../README.md#quick-start) |
| point ASON at DeepSeek or another OpenAI-compatible endpoint | [AI providers](ai-providers.md) |
| expose your own methods to the agent | [Writing operators](operators.md) |
| understand what the internal agents do | [Architecture and agents](architecture.md) |
| build and test the repository | [Contributing](contributing.md) |
