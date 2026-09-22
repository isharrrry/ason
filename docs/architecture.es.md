# Arquitectura, agentes y por qué ASON

[English](architecture.md) | [中文](architecture.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

## Cómo funciona ASON

A continuación se muestra una descripción general simplificada de la arquitectura de ASON.

![ASON Flow Overview](../images/flow-overview.jpg)
1. Tu aplicación define **operadores** que ejecutan métodos en tu código.  
2. El **ASON Client** crea API (firmas de operadores) y las pasa al **script agent** junto con la tarea del usuario.  
3. El **script agent** genera un script y lo envía al **entorno de ejecución** (mismo proceso, proceso externo, contenedor Docker o servidor remoto).  
4. El **entorno de ejecución** llama a proxies generados dinámicamente que invocan métodos reales de los operadores.


> La forma en que se declaran y asocian los operadores se describe en [cómo escribir operadores](operators.es.md).

## Agentes de ASON

ASON usa varios agentes de IA internos para coordinar el flujo de trabajo, generar scripts, extraer datos y explicar resultados:

- **Reception Agent** – Acepta el hilo de conversación y prepara la información de entrada para el Script Agent.  
  Puedes omitirlo estableciendo `AsonClientOptions.SkipReceptionAgent` en `true`.

- **Script Agent** – Genera el script según la tarea del usuario y la API disponible.

- **Extractor Agent** – Extrae datos de texto no estructurado.  
  El Script Agent puede usarlo cuando una tarea implica analizar texto, como extraer el nombre de una empresa de un correo electrónico.  
  Habilítalo con `OperatorBuilder.AddExtractor`.

- **Explainer Agent** – Explica los resultados de un script ejecutado según la solicitud original del usuario.  
  Puedes deshabilitarlo estableciendo `AsonClientOptions.SkipExplainerAgent` en `true`.


## Ventajas de ASON frente a tool calling / MCP

### Lógica flexible

ASON puede manejar flujos de trabajo más complejos que los sistemas tradicionales de tool calling o los servidores MCP independientes.  
Proporciona acceso a las entidades de tu modelo y puede encadenar varias llamadas a métodos en un único script generado por IA.

**Escenario de ejemplo:** Un usuario pide a tu sistema que actualice a *Inactivo* el estado de todos los leads creados antes de junio de 2024.

#### Enfoque tradicional (MCP / Tool Calling)

Por lo general, definirías métodos especializados como `GetLeadsBeforeDate(date)` y `UpdateLeadsStatus(leadIds, newStatus)`.  

1. La solicitud del usuario se pasa al LLM junto con todas las herramientas disponibles.  
2. El LLM analiza la solicitud y las herramientas disponibles, y luego decide llamar a `GetLeadsBeforeDate` con una fecha específica como parámetro.  
3. Tu aplicación ejecuta el método `GetLeadsBeforeDate`.  
4. Los resultados se devuelven al LLM (toda la colección devuelta por `GetLeadsBeforeDate`).  
5. Luego el LLM decide llamar a `UpdateLeadsStatus` usando los IDs obtenidos en el paso anterior.  
6. Tu aplicación ejecuta el método `UpdateLeadsStatus`.  
7. Finalmente, el LLM genera la salida de texto.  

Si el usuario modifica posteriormente su solicitud, es posible que estos métodos ya no sirvan, lo que te obliga a agregar métodos nuevos o a hacer más genéricos los existentes, lo que aumenta el riesgo de entradas no válidas generadas por el LLM. Ten en cuenta también que, en el paso 4, todo el conjunto de resultados debe pasarse al LLM para que pueda construir los parámetros del paso 5.



#### Enfoque de ASON

ASON genera y ejecuta dinámicamente un script para completar la tarea. Por ejemplo, podría recuperar los leads creados antes de cierta fecha, actualizar su estado a *Inactivo* y luego guardar los cambios con los métodos disponibles del modelo. 

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
 
ASON entiende tu modelo (por ejemplo, `Lead`) y las operaciones disponibles (como `GetLeads` y `UpdateLead`), lo que permite a la IA componer lógica flexible sobre la marcha.


### Rendimiento

Con los sistemas tradicionales de MCP o tool calling, **cada llamada de herramienta genera una solicitud independiente al LLM**.  
Por ejemplo, si expones un método `DeleteOrder` y un usuario pide eliminar 10 pedidos, tu cliente debe realizar al menos 10 idas y vueltas con el LLM, lo que afecta significativamente el rendimiento.

ASON, en cambio, genera un único script y lo ejecuta **sin más intervención del LLM**. 

```csharp
var lastOrders = ordersOperator.GetOrders()
    .OrderByDescending(o => o.CreatedDate)
    .Take(10);

foreach (var order in lastOrders)
{
    ordersOperator.DeleteOrder(order);
}
```

Por ejemplo, puede obtener una lista de pedidos recientes y eliminarlos todos en un solo script ejecutado localmente.

Esto reduce la latencia de red y el uso de tokens, a la vez que mejora el rendimiento en operaciones a gran escala.

![ASON Performance Benefits](../images/ason-performance.png)

### Uso de tokens y capacidad de la API

Los servicios de IA suelen cobrar según el consumo de tokens.  
Los mecanismos tradicionales de tool calling a menudo desperdician tokens por las siguientes razones:

- **Procesamiento de grandes volúmenes de datos:**  
  Cuando una herramienta de IA (por ejemplo, *GetOrders*) devuelve miles de registros, el LLM debe manejar todos los datos, consumiendo rápidamente los límites de tokens.
- **Descripciones completas de las herramientas en cada solicitud:**  
  Cada llamada de herramienta incluye la definición y la descripción completas de todas las herramientas disponibles, incluso si la mayoría no se usa.

ASON elimina ambos problemas.  
Una vez generado un script, se ejecuta localmente — **sin llamadas adicionales al LLM** — lo que permite procesar datos sin límite y sin costos adicionales de tokens.

ASON también ofrece un **modelo de API compacto** en el que el LLM recibe firmas de métodos concisas representadas como código. 

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

Dado que los LLM se entrenan con sintaxis de programación, entienden y operan fácilmente con este tipo de API, lo que permite una generación de scripts robusta y consciente del contexto incluso para API con cientos de métodos.
