# 编写 operator

[English](operators.md) | **中文** | [Español](operators.es.md)

> 本文档是 **ASON** 文档的一部分 —— 返回 [README](../README.zh-CN.md)。

## Operator

**operator** 是一个类，其中包含向 Script Agent 暴露的方法。  
要定义一个 operator，请创建一个继承自 `OperatorBase` 的类。  
对类应用 `[AsonOperator]` 特性，并对每个你想暴露的方法应用 `[AsonMethod]` 特性。

示例：

```csharp
[AsonOperator]  
public class OrdersViewOperator : OperatorBase<OrdersViewModel> {  
    [AsonMethod]  
    public void DeleteOrder(int orderId) => AttachedObject?.DeleteOrder(orderId);  
}
```

Operator 会被附加到包含业务逻辑的对象上。这些对象存储在 `AttachedObject` 属性中。

要将 operator 附加到某个对象上，请调用 `AttachChildOperator`：

```csharp
public partial class OrdersViewModel {  
    public void DeleteOrder(int orderId) => Debug.WriteLine($"Deleted order {orderId}");  
    public OrdersViewModel(RootOperator rootOperator) {  
        rootOperator.AttachChildOperator<OrdersViewOperator>(this);  
    }  
}
```

### Root operator

你的应用程序必须包含一个继承自 `RootOperator` 的 **root operator**。  
它通常包含用于返回子 operator 或访问全局可用 API 的方法。

示例：

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
> 你不需要为 root operator 调用 `AttachChildOperator`。当你把对象传给构造函数时，它会自动被附加。

### Operator 之间的关系

每个 operator 都包含与特定**视图**、**模块**或**服务**相关的 API。  
父 operator 可以使用 `OperatorBase.GetViewOperator` 方法来创建子 operator。

![ASON Operators](../images/operators.jpg)

> [!Note]
> 请始终使用 `GetViewOperator` 来创建子 operator —— 绝不要直接实例化它们。

你必须向 `GetViewOperator` 传入一个导航函数，这样 operator 在被调用时才知道如何导航或打开视图。

这种架构确保了生成的脚本只在相应的视图或模型处于活动状态时才调用 operator 方法。  
例如，如果某个脚本要对包含订单的 Data Grid 应用筛选，它会先导航到 Orders View，等待网格加载完成，然后调用相应的方法。

`AttachChildOperator` 方法会通知父 operator：子级已就绪，其方法可以被调用。  
如果你的 operator 依赖于 UI 元素或它们的数据，请在视图渲染完成或其数据加载完成时调用 `AttachChildOperator`。  
当关联的对象被销毁时，请调用 `DetachChildOperator`。


## 无状态 operator

并非每个 operator 都绑定到视图。有两种形态无需任何视图生命周期即可工作。

**静态模块** —— 静态类会成为一个 operator，其方法通过类型名称来寻址：

```csharp
[AsonOperator(description: "Stateless helpers")]
public static class LibDemoStaticOperator {
    [AsonMethod("Adds two integers")]
    public static int Add(int left, int right) => left + right;

    [AsonMethod("Repeats the supplied text")]
    public static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}
```

Script Agent 会直接调用这些方法（`LibDemoStaticOperator.Add(2, 4)`）：没有句柄、没有导航，也没有附加步骤。

**仅标记类** —— 一个带有 `[AsonOperator]` 注解的类，只要它**不**派生自 `OperatorBase` 且具有公共无参构造函数，就会被实例化一次并注册为单例：

```csharp
[AsonOperator]
public class LibDemoOperator {
    [AsonMethod("Echoes the supplied text")]
    public string Echo(string text) => $"LibDemo received: {text}";
}
```

脚本会为它获得一个代理变量（`libDemoOperator.Echo("hi")`），这与它们为已附加的视图 operator 获得代理变量的方式完全一致。

这两种形态在每种执行模式下都可调用 —— 同一进程、外部进程、Docker 和远程运行器 —— 因为调用始终发生在宿主侧。**没有**公共无参构造函数的仅标记类无法被调用，ASON 会为它记录一条警告，而不是静默失败。

## 没有运行时的标记特性（`Ason.Abstractions`）

这些标记特性位于它们自己的包中：

| 包 | 目标框架 | 包含内容 |
|---|---|---|
| `Ason` | net6.0, net9.0 | 运行时：`AsonClient`、`OperatorBase`、代码生成和代理 |
| `Ason.Abstractions` | netstandard2.0 | 仅包含 `AsonOperatorAttribute`、`AsonMethodAttribute` 和 `AsonModelAttribute` |

如果某个类库只需要为它的 operator 和模型添加注解 —— 例如一个不应依赖 Semantic Kernel 的 `netstandard2.0` 领域包 —— 请引用 **`Ason.Abstractions`** 而不是完整的运行时。

`Ason` 为这些特性声明了类型转发，因此针对 `Ason.dll` 编译的代码在该拆分之后仍能解析它们，现有使用方无需修改源代码。

## 领域模型

ASON 允许你把模型作为 Script Agent 所使用的 API 的一部分暴露出来。  
要把某个模型加入你的 API，请对该类应用 `[AsonModel]` 特性。

```csharp
[AsonModel]  
public class Order {  
    public int OrderId { get; set; }  
    public DateTime OrderDate { get; set; }  
}
```

这样一来，ASON 就能生成使用 `Order` 模型的脚本，并自动处理客户端与执行环境之间的序列化和反序列化。

