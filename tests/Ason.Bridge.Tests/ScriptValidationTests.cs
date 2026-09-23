using Ason.Bridge.Tests.TestSupport;

namespace Ason.Bridge.Tests;

/// <summary>
/// The bridge is an execution surface reachable from outside the process, so it applies the same keyword
/// filter the client would apply - and it must reject a script before anything is handed to an executor.
/// </summary>
public class ScriptValidationTests {

    [Fact]
    public async Task ExecuteScript_rejects_a_forbidden_keyword_before_reaching_the_executor() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        options.ForbiddenScriptKeywords = new[] { "System.IO" };
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("return System.IO.Path.GetTempPath();");

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.ScriptRejected, result.ErrorCode);
        Assert.Contains("System.IO", result.Error);
        Assert.Empty(fake.ExecutedScripts);
    }

    [Fact]
    public async Task ExecuteScript_rejects_an_empty_script() {
        var fake = new FakeAsonExecutor();
        var options = BridgeTestApp.Options();
        options.Executor = fake;
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("   ");

        Assert.False(result.Success);
        Assert.Equal(AsonBridgeErrorCodes.ScriptRejected, result.ErrorCode);
        Assert.Empty(fake.ExecutedScripts);
    }

    [Fact]
    public async Task ExecuteScript_allows_a_script_that_trips_no_keyword() {
        var options = BridgeTestApp.Options();
        options.ForbiddenScriptKeywords = new[] { "System.IO" };
        await using var runtime = new AsonBridgeRuntime(options);

        var result = await runtime.ExecuteScriptAsync("return BridgeStaticOperator.Add(1, 2);");

        Assert.True(result.Success, result.Error);
    }
}
