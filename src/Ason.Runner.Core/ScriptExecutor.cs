using Microsoft.CodeAnalysis.CSharp.Scripting;
using Microsoft.CodeAnalysis.Scripting;

namespace AsonRunner;

public static class ScriptExecutor
{
    private static ScriptOptions CreateDefaultOptions() => ScriptOptions.Default
        .AddReferences(typeof(ScriptExecutor).Assembly)
        // The script preamble imports System.Text.Json, and on .NET Framework there is no shared framework to
        // resolve it from: without this reference the generated script fails to compile with CS0234. The net472
        // certificate leg in tests/LibDemo.SmokeTests is what surfaced it (net6.0+ resolves it anyway).
        .AddReferences(typeof(System.Text.Json.JsonSerializer).Assembly)
        .AddImports("System", "System.Threading.Tasks", "AsonHostInterop");

    public static Task<object?> EvaluateAsync(string code, AsonHostInterop.IHostBridge hostBridge)
        => EvaluateAsync(code, hostBridge, CreateDefaultOptions(), CancellationToken.None);

    public static Task<object?> EvaluateAsync(string code, AsonHostInterop.IHostBridge hostBridge, CancellationToken ct)
        => EvaluateAsync(code, hostBridge, CreateDefaultOptions(), ct);

    public static Task<object?> EvaluateAsync(string code, AsonHostInterop.IHostBridge hostBridge, ScriptOptions options)
        => EvaluateAsync(code, hostBridge, options, CancellationToken.None);

    public static async Task<object?> EvaluateAsync(string code, AsonHostInterop.IHostBridge hostBridge, ScriptOptions options, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(code)) return null;
        var globals = new ScriptGlobals(hostBridge);
        try
        {
            await hostBridge.LogAsync("Information", $"Evaluating script via Roslyn. Length={code.Length}").ConfigureAwait(false);
            var result = await Task.Run(async () =>
            {
                return await CSharpScript.EvaluateAsync<object?>(code, options, globals, typeof(ScriptGlobals), cancellationToken: ct).ConfigureAwait(false);
            }, ct).ConfigureAwait(false);
            await hostBridge.LogAsync("Information", "Script evaluation succeeded.").ConfigureAwait(false);
            return result;
        }
        catch (CompilationErrorException cex)
        {
            await hostBridge.LogAsync("Error", "Script compilation failed.\n" + cex).ConfigureAwait(false);
            throw;
        }
        catch (OperationCanceledException)
        {
            await hostBridge.LogAsync("Information", "Script evaluation cancelled.").ConfigureAwait(false);
            throw;
        }
        catch (Exception ex)
        {
            await hostBridge.LogAsync("Error", "Script evaluation failed.\n" + ex).ConfigureAwait(false);
            throw;
        }
    }
}

public sealed class ScriptGlobals
{
    public AsonHostInterop.IHostBridge Host { get; }
    public ScriptGlobals(AsonHostInterop.IHostBridge host) => Host = host;
}
