# Writing operators

[English](operators.md) | [中文](operators.zh-CN.md) | [Español](operators.es.md)

> Part of the **ASON** documentation — back to the [README](../README.md).

## Operators

An **operator** is a class that contains methods exposed to the Script Agent.  
To define an operator, create a class that inherits from `OperatorBase`.  
Apply the `[AsonOperator]` attribute to the class and `[AsonMethod]` to each method you want to expose.

Example:

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

Operators are attached to objects that contain business logic.  These objects are stored in the `AttachedObject` property.

To attach an operator to an object, call `AttachChildOperator`:

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

### Root operator

Your application must contain a **root operator** that inherits from `RootOperator`.  
It typically includes methods for returning child operators or accessing globally available APIs.

Example:

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
> You don’t need to call `AttachChildOperator` for the root operator. It’s automatically attached when you pass an object to the constructor.

### Operator relationships

Each operator contains APIs related to a specific **view**, **module**, or **service**.  
A parent operator can create child operators using the `OperatorBase.GetViewOperator` method.

![ASON Operators](../images/operators.jpg)

> [!Note]
> Always use `GetViewOperator` to create child operators — never instantiate them directly.

You must pass a navigation function to `GetViewOperator` so that the operator knows how to navigate or open a view when called.

This architecture ensures that generated scripts call operator methods only when the corresponding view or model is active.  
For example, if a script applies a filter to a Data Grid with orders, it will navigate to the Orders View, wait until the grid is loaded, and then call the appropriate method.

The `AttachChildOperator` method notifies the parent operator that the child is ready and its methods can be invoked.  
If your operator depends on UI elements or their data, call `AttachChildOperator` when the view is rendered or its data is loaded.  
When the associated object is destroyed, call `DetachChildOperator`.


## Stateless operators

Not every operator is bound to a view. Two shapes work without any view lifecycle.

**Static module** — a static class becomes an operator whose methods are addressed by type name:

```csharp
[AsonOperator(description: "Stateless helpers")]
public static class LibDemoStaticOperator {
    [AsonMethod("Adds two integers")]
    public static int Add(int left, int right) => left + right;

    [AsonMethod("Repeats the supplied text")]
    public static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}
```

The Script Agent calls these methods directly (`LibDemoStaticOperator.Add(2, 4)`): no handle, no navigation and no attachment step.

**Marker-only class** — a class annotated with `[AsonOperator]` that does **not** derive from `OperatorBase` and has a public parameterless constructor is materialised once and registered as a singleton:

```csharp
[AsonOperator]
public class LibDemoOperator {
    [AsonMethod("Echoes the supplied text")]
    public string Echo(string text) => $"LibDemo received: {text}";
}
```

Scripts receive a proxy variable for it (`libDemoOperator.Echo("hi")`), exactly as they do for attached view operators.

Both shapes are callable in every execution mode — in-process, external process, Docker and remote runner — because invocation always happens on the host side. A marker-only class **without** a public parameterless constructor cannot be invoked, and ASON logs a warning for it instead of failing silently.

## Marker attributes without the runtime (`Ason.Abstractions`)

The marker attributes live in a package of their own:

| Package | Targets | Contains |
|---|---|---|
| `Ason` | net6.0, net9.0 | the runtime: `AsonClient`, `OperatorBase`, code generation and proxies |
| `Ason.Abstractions` | netstandard2.0 | only `AsonOperatorAttribute`, `AsonMethodAttribute` and `AsonModelAttribute` |

If a class library only needs to annotate its operators and models — for example a `netstandard2.0` domain package that should not depend on Semantic Kernel — reference **`Ason.Abstractions`** instead of the full runtime.

`Ason` declares type forwards for these attributes, so code compiled against `Ason.dll` keeps resolving them after the split, and existing consumers need no source change.

## Domain model

ASON allows you to expose models as part of the API used by the Script Agent.  
To add a model to your API, apply the `[AsonModel]` attribute to a class.

```csharp
[AsonModel]  
public class Order {  
    public int OrderId { get; set; }  
    public DateTime OrderDate { get; set; }  
}
```

This enables ASON to generate scripts that use the `Order` model and automatically handle serialization and deserialization between the client and the execution environment.

## Listing the API

The API that scripts can call is also available as data. `OperatorApiCatalog.Describe(assemblies)` returns the operators, their methods (named and typed exactly as the script prompt shows them), their parameters and the `[AsonModel]` types; `ToMarkdown()` renders that as tables:

```csharp
OperatorApiCatalog catalog = OperatorApiCatalog.Describe(
    typeof(MainAppOperator).Assembly,
    typeof(LibDemo.LibDemoOperator).Assembly);

catalog.Operators;    // the operators, ordered by type name
catalog.MethodCount;  // total callable methods
catalog.Models;       // the [AsonModel] types
Console.WriteLine(catalog.ToMarkdown());
```

Pass the same assemblies you registered with the client. The catalog reuses the helpers that generate the prompt text, so a listing cannot disagree with what the model is told, and it de-duplicates assemblies just like `OperatorBuilder.AddAssemblies`.

Exposing the listing as an operator is what makes it reachable from the chat, which is what the WPF sample does:

```csharp
[AsonMethod("Lists every available operator API as a Markdown table. CALL THIS METHOD WHEN THE USER ASKS WHICH APIs, OPERATIONS OR COMMANDS ARE AVAILABLE.")]
public string GetApiListing() => OperatorApiCatalog
    .Describe(typeof(MainAppOperator).Assembly, typeof(LibDemo.LibDemoOperator).Assembly)
    .ToMarkdown();   // the sample caches the result in a static field
```

Markdown is the only rendering today; the structured catalog is the single source for any other format (JSON, a tool/function-calling schema) when one is needed.

