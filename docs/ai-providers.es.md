# Proveedores de IA

[English](ai-providers.md) | [中文](ai-providers.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

ASON nunca se comunica directamente con un modelo: le pasas a `AsonClient` (o a `AddAson`) cualquier `IChatCompletionService` de Semantic Kernel. Las plantillas y los ejemplos construyen ese servicio a partir de tres variables de entorno, lo cual es suficiente para OpenAI y para cualquier proveedor que exponga una API compatible con OpenAI.

## Variables de entorno

| Variable | Propósito | Valor predeterminado |
|---|---|---|
| `MY_OPEN_AI_KEY` | Clave de API. Si no está, se usa `OPENAI_API_KEY`. | *obligatorio* |
| `MY_OPEN_AI_BASE_URL` | Sustitución del endpoint. Si no está, se usa `OPENAI_BASE_URL`. | valor predeterminado del conector (`api.openai.com`) |
| `MY_OPEN_AI_MODEL` | Id. del modelo. Si no está, se usa `OPENAI_MODEL`. | `gpt-4.1-mini` |

`Ason.OpenAiCompatibleChatServiceFactory` las lee:

```csharp
IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
```

Si no se configura ninguna URL base, usa el constructor simple `OpenAIChatCompletionService(modelId, apiKey)`, de modo que las instalaciones que solo usan OpenAI siguen funcionando sin cambios. El constructor para endpoints personalizados está marcado como experimental por Semantic Kernel (`SKEXP0010`); la fábrica suprime ese diagnóstico de forma interna, así que los consumidores no tienen que hacerlo.

## DeepSeek

DeepSeek expone una API compatible con OpenAI — consulta su [documentación de API](https://api-docs.deepseek.com/).

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

Las solicitudes se envían a `<MY_OPEN_AI_BASE_URL>/chat/completions`, que coincide con la ruta que documenta DeepSeek. Los nombres de los modelos cambian con el tiempo — `deepseek-flash` y `deepseek-v4-pro` eran los vigentes cuando se escribió esta página; consulta su documentación antes de fijar un nombre.

## Otros endpoints compatibles con OpenAI

| Proveedor | URL base | Modelo |
|---|---|---|
| OpenAI | *(dejar sin establecer)* | `gpt-4.1-mini` |
| DeepSeek | `https://api.deepseek.com` | `deepseek-flash` |
| Ollama (local) | `http://localhost:11434/v1` | cualquier modelo que hayas descargado |
| vLLM, LM Studio, otros servidores locales | `http://<host>:<port>/v1` | el nombre que hayas servido |

## Proveedores que no son compatibles con OpenAI

Para Azure OpenAI, Gemini, Anthropic y el resto, construye tú mismo el servicio de Semantic Kernel y pásalo a `AsonClient` o a `AddAson`:

```csharp
IChatCompletionService chatService = new AzureOpenAIChatCompletionService(deploymentName, endpoint, apiKey);
```

Los conectores disponibles se enumeran en [Chat Completion | Overview](https://learn.microsoft.com/en-us/semantic-kernel/concepts/ai-services/chat-completion/?tabs=csharp-Google%2Cpython-AzureOpenAI%2Cjava-AzureOpenAI&pivots=programming-language-csharp).

## Solución de problemas

| Síntoma | Causa probable |
|---|---|
| `ArgumentException: The value cannot be an empty string (Parameter 'apiKey')` al iniciar | No hay ninguna clave configurada. Las aplicaciones de escritorio leen la variable mientras se crea la vista de chat, así que establézcala **antes** de iniciarla. |
| `401` o `invalid_api_key` en la respuesta | La clave no pertenece al proveedor al que apunta `MY_OPEN_AI_BASE_URL`. |
| `404` en `/chat/completions` | La URL base incluye un segmento de ruta que el proveedor no usa. DeepSeek espera el valor simple `https://api.deepseek.com`; los servidores locales al estilo OpenAI normalmente necesitan un `/v1` final. |
| La respuesta ignora el modelo que configuraste | `MY_OPEN_AI_MODEL` no estaba establecida, así que se usó el valor predeterminado `gpt-4.1-mini`. |
