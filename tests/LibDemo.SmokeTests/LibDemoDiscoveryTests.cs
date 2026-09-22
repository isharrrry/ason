using System.Reflection;
using Ason;
using Ason.CodeGen;
using LibDemo;

namespace LibDemoSmokeTests;

/// <summary>
/// Proves that the ASON markers declared inside the net6.0 / netstandard2.0 LibDemo class
/// library are visible to a .NET 9 host both by reflection and through the OperatorBuilder
/// pipeline that feeds the script agent.
/// </summary>
public class LibDemoDiscoveryTests {

    [Fact]
    public void Markers_are_readable_from_the_net6_library() {
        Assert.NotNull(typeof(LibDemoOperator).GetCustomAttribute<AsonOperatorAttribute>());
        Assert.NotNull(typeof(DemoProduct).GetCustomAttribute<AsonModelAttribute>());

        var asonMethods = typeof(LibDemoOperator)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(m => m.GetCustomAttribute<AsonMethodAttribute>() != null)
            .Select(m => m.Name)
            .ToArray();

        Assert.Contains("Echo", asonMethods);
        Assert.Contains("GetLibraryTimestamp", asonMethods);
        Assert.Contains("GetProducts", asonMethods);
    }

    [Fact]
    public async Task OperatorBuilder_discovers_libdemo_operators() {
        var library = new OperatorBuilder()
            .AddAssemblies(typeof(LibDemoOperator).Assembly)
            .Build();

        var (_, signatures, _) = await library.BuildTask;

        Assert.Contains("LibDemoOperator", signatures);
        Assert.Contains("Echo", signatures);
        Assert.Contains("GetProducts", signatures);
    }

    /// <summary>
    /// The markers moved from Ason.dll to Ason.Abstractions.dll, so already-compiled consumers
    /// resolve them through a type forward. This asserts the forward actually works at runtime
    /// when the type is looked up via the *old* assembly name.
    /// </summary>
    [Fact]
    public void Marker_types_are_forwarded_from_Ason_to_Ason_Abstractions() {
        var forwarded = Type.GetType("Ason.AsonOperatorAttribute, Ason");

        Assert.NotNull(forwarded);
        Assert.Equal("Ason.Abstractions", forwarded!.Assembly.GetName().Name);
        Assert.Same(forwarded, typeof(AsonOperatorAttribute));
    }
}
