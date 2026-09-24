using System.Reflection;
using System.Runtime.Versioning;
using Ason;
using AsonRunner.Protocol;
using Xunit;

namespace LibDemoSmokeTests;

/// <summary>
/// The certificate for the runtime's framework promise. <c>Ason</c> ships a single
/// <c>netstandard2.0</c> asset (that is what keeps .NET Framework and every modern .NET host working), so
/// every leg of this project - net6.0 / net9.0 / net10.0 / net472 - must consume exactly that asset:
/// the attribute below is written by the compiler into the assembly it produced, which makes it the only
/// honest way to tell which asset NuGet picked.
///
/// The second test covers what actually breaks below .NET 5: records and <c>init</c> accessors need
/// <c>IsExternalInit</c>, and <c>System.Text.Json</c> is not part of netstandard2.0. Both live on the runner
/// message path, so serializing one message exercises the two fixes that make the ns2.0 leg possible.
/// </summary>
public class FrameworkAssetTests {

    [Fact]
    public void The_runtime_is_consumed_as_a_netstandard2_0_asset() {
        var framework = typeof(AsonClient).Assembly
            .GetCustomAttribute<TargetFrameworkAttribute>()?.FrameworkName;

        Assert.Equal(".NETStandard,Version=v2.0", framework);
    }

    [Fact]
    public void The_runner_message_protocol_serializes_on_this_runtime() {
        var json = RunnerMessageSerializer.Serialize(new ExecRequest("1", "return 1;"));

        Assert.Contains("\"exec\"", json);
        Assert.Contains("return 1;", json);
    }
}
