# Escribir operators

[English](operators.md) | [中文](operators.zh-CN.md) | **Español**

> Parte de la documentación de **ASON** — volver al [README](../README.es.md).

## Operators

Un **operator** es una clase que contiene métodos expuestos al Script Agent.  
Para definir un operator, crea una clase que herede de `OperatorBase`.  
Aplica el atributo `[AsonOperator]` a la clase y `[AsonMethod]` a cada método que quieras exponer.

Ejemplo:

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

Los operators se asocian a objetos que contienen la lógica de negocio.  Estos objetos se almacenan en la propiedad `AttachedObject`.

Para asociar un operator a un objeto, llama a `AttachChildOperator`:

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

### Root operator

Tu aplicación debe contener un **root operator** que herede de `RootOperator`.  
Normalmente incluye métodos para devolver operators hijos o acceder a APIs disponibles de forma global.

Ejemplo:

```csharp
[AsonOperator]  
public class MainAppOperator : RootOperator<MainViewModel> {  
    public MainAppOperator(MainViewModel attachedObject) : base(attachedObject) { }  

    [AsonMethod]  
    public async Task<OrdersViewOperator> GetOrdersViewOperatorAsync() =>  
        await GetViewOperator<OrdersViewOperator>(AttachedObject.Navigate<OrdersViewModel>);  

    [AsonMethod]  
    public async Task<IEnumerable<Setting>> GetApplicationSettingsAsync() =>  
        await AttachedObject.GetSettings();  
}
```

> [!Note]  
> No necesitas llamar a `AttachChildOperator` para el root operator. Se asocia automáticamente cuando pasas un objeto al constructor.

### Relaciones entre operators

Cada operator contiene APIs relacionadas con una **vista**, un **módulo** o un **servicio**.  
Un operator padre puede crear operators hijos mediante el método `OperatorBase.GetViewOperator`.

![ASON Operators](../images/operators.jpg)

> [!Note]
> Usa siempre `GetViewOperator` para crear operators hijos — nunca los instancies directamente.

Debes pasar una función de navegación a `GetViewOperator` para que el operator sepa cómo navegar o abrir una vista cuando se le invoque.

Esta arquitectura garantiza que los scripts generados llamen a los métodos del operator solo cuando la vista o el modelo correspondiente esté activo.  
Por ejemplo, si un script aplica un filtro a un Data Grid con pedidos, navegará a la vista de pedidos, esperará a que se cargue la cuadrícula y luego llamará al método correspondiente.

El método `AttachChildOperator` notifica al operator padre que el hijo está listo y que sus métodos pueden invocarse.  
Si tu operator depende de elementos de UI o de sus datos, llama a `AttachChildOperator` cuando se renderice la vista o se carguen sus datos.  
Cuando se destruya el objeto asociado, llama a `DetachChildOperator`.


## Operators sin estado

No todos los operators están vinculados a una vista. Dos formas funcionan sin ningún ciclo de vida de vista.

**Módulo estático** — una clase estática se convierte en un operator cuyos métodos se identifican por el nombre del tipo:

```csharp
[AsonOperator(description: "Stateless helpers")]
public static class LibDemoStaticOperator {
    [AsonMethod("Adds two integers")]
    public static int Add(int left, int right) => left + right;

    [AsonMethod("Repeats the supplied text")]
    public static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}
```

El Script Agent llama a estos métodos directamente (`LibDemoStaticOperator.Add(2, 4)`): sin handle, sin navegación y sin paso de asociación.

**Clase solo con marcador** — una clase anotada con `[AsonOperator]` que **no** deriva de `OperatorBase` y tiene un constructor público sin parámetros se materializa una vez y se registra como singleton:

```csharp
[AsonOperator]
public class LibDemoOperator {
    [AsonMethod("Echoes the supplied text")]
    public string Echo(string text) => $"LibDemo received: {text}";
}
```

Los scripts reciben una variable de proxy para ella (`libDemoOperator.Echo("hi")`), exactamente igual que en el caso de los operators de vista asociados.

Ambas formas se pueden invocar en todos los modos de ejecución — en proceso, proceso externo, Docker y ejecutor remoto — porque la invocación siempre ocurre del lado del host. Una clase solo con marcador **sin** un constructor público sin parámetros no se puede invocar, y ASON registra una advertencia en lugar de fallar en silencio.

## Atributos de marcador sin el runtime (`Ason.Abstractions`)

Los atributos de marcador viven en un paquete propio:

| Paquete | Destinos | Contiene |
|---|---|---|
| `Ason` | net6.0, net9.0 | el runtime: `AsonClient`, `OperatorBase`, generación de código y proxies |
| `Ason.Abstractions` | netstandard2.0 | solo `AsonOperatorAttribute`, `AsonMethodAttribute` y `AsonModelAttribute` |

Si una biblioteca de clases solo necesita anotar sus operators y modelos — por ejemplo, un paquete de dominio `netstandard2.0` que no debería depender de Semantic Kernel — haz referencia a **`Ason.Abstractions`** en lugar del runtime completo.

`Ason` declara reenvíos de tipo para estos atributos, de modo que el código compilado contra `Ason.dll` siga resolviéndolos tras la división, y los consumidores existentes no necesitan cambiar su código fuente.

## Modelo de dominio

ASON te permite exponer modelos como parte de la API que usa el Script Agent.  
Para añadir un modelo a tu API, aplica el atributo `[AsonModel]` a una clase.

```csharp
[AsonModel]  
public class Order {  
    public int OrderId { get; set; }  
    public DateTime OrderDate { get; set; }  
}
```

Esto permite que ASON genere scripts que usen el modelo `Order` y gestionen automáticamente la serialización y deserialización entre el cliente y el entorno de ejecución.

