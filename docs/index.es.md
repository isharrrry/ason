# Índice de documentación

[English](index.md) | [中文](index.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

Todas las guías existen en inglés, chino y español. La tabla de temas enlaza las tres versiones de cada documento.

| Tema | Versiones de idioma | Qué cubre |
|---|---|---|
| **Proveedores de IA** | [English](ai-providers.md) · [中文](ai-providers.zh-CN.md) · [Español](ai-providers.es.md) | Variables de entorno, DeepSeek y otros endpoints compatibles con OpenAI, Azure / Ollama / Gemini / Anthropic mediante Semantic Kernel, solución de problemas |
| **Cómo escribir operadores** | [English](operators.md) · [中文](operators.zh-CN.md) · [Español](operators.es.md) | `OperatorBase`, operadores raíz, el ciclo de vida de `GetViewOperator` / `AttachChildOperator`, módulos sin estado, `[AsonModel]`, `Ason.Abstractions` |
| **Configuración del cliente** | [English](configuration.md) · [中文](configuration.zh-CN.md) · [Español](configuration.es.md) | El constructor de `AsonClient`, la referencia completa de `AsonClientOptions`, el registro de `AddAson`, anulaciones de prompts, logs |
| **Modos de ejecución** | [English](execution-modes.md) · [中文](execution-modes.zh-CN.md) · [Español](execution-modes.es.md) | En proceso, proceso externo, Docker y ejecución remota, las cinco combinaciones de despliegue y qué configuración encaja con cada forma de aplicación |
| **Separación aplicación / agente** | [English](app-agent-separation.md) · [中文](app-agent-separation.zh-CN.md) · [Español](app-agent-separation.es.md) | Separar la aplicación que solo contiene operadores `[Ason*]` y un puente del agente que la conduce: el manifiesto, las interfaces de script y de función única, los adaptadores gRPC y MCP, la ubicación de ejecución y la seguridad |
| **Arquitectura y agentes** | [English](architecture.md) · [中文](architecture.zh-CN.md) · [Español](architecture.es.md) | Cómo funciona ASON, la topología de despliegue y los límites entre procesos, las credenciales, los agentes internos y la comparación con tool calling / MCP |
| **Contribuir** | [English](contributing.md) · [中文](contributing.zh-CN.md) · [Español](contributing.es.md) | Compilación, las suites de pruebas, CI y qué requiere Windows o Docker |

## Por dónde empezar

| Si quieres… | Ve a |
|---|---|
| poner algo en marcha rápidamente | [inicio rápido del README](../README.es.md#inicio-rápido) |
| conectar ASON a DeepSeek u otro endpoint compatible con OpenAI | [Proveedores de IA](ai-providers.es.md) |
| exponer tus propios métodos al agente | [Cómo escribir operadores](operators.es.md) |
| entender qué hacen realmente los agentes internos | [Arquitectura y agentes](architecture.es.md) |
| compilar y probar el repositorio | [Contribuir](contributing.es.md) |
| elegir un modo y un despliegue para mi aplicación | [Modos de ejecución](execution-modes.es.md) |
