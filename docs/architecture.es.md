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

## Topología de despliegue

`ExecutionMode` y `UseRemoteRunner` son ajustes independientes: el primero indica *cómo* se aísla el
código generado y el segundo indica *dónde* vive el host del script. En conjunto producen cinco rutas de
enlace reales a través de tres fronteras de proceso.

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

El diagrama anterior, leído de arriba hacia abajo, muestra que el host del cliente —el nodo `[1]`, es
decir, tu aplicación con `AsonClient`, el root operator, los operadores, los LLM agents y los clientes
MCP— siempre es el origen de la conexión. Desde ahí salen dos ramas. La primera lleva al nodo `[2]`, un
ejecutor externo del lado del cliente: un proceso hijo de `Ason.ExternalExecutor` que corre en la
misma máquina, o bien, en modo Docker, ese mismo proceso hijo cuando es `docker run --rm -i <image>`. La
segunda rama lleva al nodo `[3]`, el servicio de ejecución remota, que es una aplicación ASP.NET Core con
el punto de conexión `/scriptRunnerHub` proporcionado por `Ason.RemoteBridge`. Ese servicio remoto se
ramifica a su vez: hacia el nodo `[4]`, un ejecutor externo del lado del servidor (un proceso hijo de
`Ason.ExternalExecutor` en la máquina del servidor, o un contenedor Docker), y hacia el nodo `[4']`, la
evaluación en proceso del propio servidor, donde `ScriptExecutor` se ejecuta dentro del proceso del
servidor web. Los comentarios de la derecha indican la ubicación de cada nodo (*local* o *remote*) y el
modo que corresponde a cada uno. Las tres fronteras se rotulan sobre los conectores: la frontera A es
stdio, con una línea JSON por mensaje; la frontera B es SignalR, que transporta esas mismas líneas JSON;
y la frontera C es el protocolo stdio idéntico de la frontera A, iniciado por el servidor. Las flechas
horizontales y verticales del diagrama solo dibujan esa topología; no representan un orden temporal de
ejecución.

| Frontera | Protocolo | Dirección | A cargo de |
|---|---|---|---|
| A: cliente ↔ ejecutor del lado del cliente | mensajes JSON de ASON por stdin/stdout del proceso hijo | en ambos sentidos (el script baja, las llamadas a operadores suben) | el cliente (`ScriptRunnerProcessHost`; el árbol de procesos se termina al liberar) |
| B: cliente ↔ servicio de ejecución remota | los mismos mensajes JSON dentro de SignalR, más un handshake `StartRunner` | en ambos sentidos, incluidas las devoluciones de llamada iniciadas por el servidor (`OnRunnerMessage`, `OnRunnerClosed`) | el servidor (una sesión por conexión, las sesiones inactivas se reclaman); el cliente se reconecta automáticamente |
| C: servicio de ejecución remota ↔ ejecutor del lado del servidor | el protocolo stdio idéntico al de la frontera A | en ambos sentidos | el servidor (reutiliza el mismo `ScriptRunnerProcessHost`) |

Como el host del script solo recibe *texto* —el script generado— y siempre vuelve a llamar para obtener
operadores, ambos transportes comparten un único conjunto de mensajes (`exec`, `execResult`, `invoke`,
`invokeResult`, `log`, `mcpInvoke`, …). Mover el host del script, por lo tanto, **no** mueve tus
operadores ni tus datos; [modos de ejecución](execution-modes.es.md#modos-de-ejecución--entornos) cubre
cómo elegir una configuración.

### Credenciales y fronteras de datos

| Elemento | Dónde vive | ¿Cruza una frontera? |
|---|---|---|
| Clave de API del LLM y configuración del modelo | solo en el host del cliente | no |
| Credenciales del cliente MCP | solo en el host del cliente | no |
| Datos de negocio accesibles a través de los operadores | solo en el host del cliente | no, salvo los argumentos y los resultados de las propias llamadas a operadores |
| Texto del script generado (`exec`) | se produce en el cliente | sí — se envía al host del script |
| Mensajes de log, argumentos y resultados de operadores | en ambos lados | sí |

Los componentes que no son del cliente no tienen dependencia de LLM: `Ason.RemoteBridge`,
`Ason.ExternalExecutor` y `Ason.Runner.Core` no contienen código de finalización de chat, y el ejecutor
solo necesita Roslyn. El extractor agent es en sí mismo un operador, así que incluso su llamada al
modelo ocurre en el cliente.

Consecuencias prácticas:

- un host de ejecución remota **no** necesita acceso saliente a tu proveedor de modelos, y un cliente de
  escritorio no necesita un runtime local ni Docker para el host del script;
- las credenciales permanecen donde viven tus operadores, así que una llamada a un LLM o una llamada
  HTTP colocada dentro de un operador mantiene sus secretos en el cliente;
- todo lo que llega al contexto del modelo puede terminar dentro del texto del script, y ese texto sí
  cruza la frontera: elige en consecuencia qué valores de retorno de los operadores expones a los
  agentes.

> El análisis estático (`AsonClientOptions.ForbiddenScriptKeywords`, que deniega `System.IO`,
> `Process.Start`, `System.Reflection`, `Environment.GetEnvironmentVariable`, …) es un filtro de
> palabras clave, no una sandbox. En modo **In-process** el script se ejecuta dentro de tu propio
> proceso, así que ese filtro es la única barrera — por eso no se recomienda In-process para entradas no
> confiables.

### Afinidad al hilo de la UI

Los métodos de los operadores siempre se invocan a través del `SynchronizationContext` capturado cuando
se construye el `AsonClient`. En una aplicación WPF eso significa que cada llamada a un operador se
ejecuta en el hilo de la UI, tanto de forma local como remota, de modo que los operadores pueden tocar
objetos vinculados a la UI sin marshalling adicional. Construye el `AsonClient` en el hilo de la UI
(como hace el ejemplo) para que esto se cumpla.

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
