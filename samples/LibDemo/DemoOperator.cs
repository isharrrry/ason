using Ason;

namespace LibDemo;

[AsonOperator(description: "LibDemo operations declared with ASON markers inside a .NET 6 / netstandard2.0 class library")]
public class LibDemoOperator {

    [AsonMethod("Echoes the supplied text back, proving the call reached the LibDemo class library")]
    public string Echo(string text) => $"LibDemo received: {text}";

    [AsonMethod("Returns the current UTC time from the LibDemo class library")]
    public DateTime GetLibraryTimestamp() => DateTime.UtcNow;

    [AsonMethod("Returns a small demo product catalog from the LibDemo class library")]
    public List<DemoProduct> GetProducts() => new List<DemoProduct> {
        new DemoProduct { Id = 1, Name = "ASON Starter Kit", Price = 19.99, InStock = true },
        new DemoProduct { Id = 2, Name = "Operator Base Mug", Price = 9.50, InStock = false }
    };
}

/// <summary>
/// Stateless operations can also be declared as a static module. Static operator modules carry no
/// handle: the generated proxy passes the type name as the target and the runtime dispatches to the
/// static method directly.
/// </summary>
[AsonOperator(description: "Stateless LibDemo helpers exposed as a static operator module")]
public static class LibDemoStaticOperator {

    [AsonMethod("Adds two integers inside the LibDemo class library")]
    public static int Add(int left, int right) => left + right;

    [AsonMethod("Repeats the supplied text the requested number of times")]
    public static string Repeat(string text, int times) => string.Concat(Enumerable.Repeat(text, times));
}
