using Ason;

namespace LibDemo;

[AsonModel("Demo product declared in a .NET 6 / netstandard2.0 class library")]
public class DemoProduct {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public double Price { get; set; }
    public bool InStock { get; set; }
}
