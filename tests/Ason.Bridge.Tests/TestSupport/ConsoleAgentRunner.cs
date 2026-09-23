using System.Diagnostics;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>Runs the console agent-side sample (<c>samples/ConsoleAgentSample</c>) and returns what it printed.</summary>
internal static class ConsoleAgentRunner {

    public static string? LocateAssembly() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            var candidate = Path.Combine(directory.FullName, "samples", "ConsoleAgentSample", "bin", configuration, "net9.0", "ConsoleAgentSample.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Runs the sample with the given arguments and waits for it to finish.</summary>
    public static async Task<(int ExitCode, string Output)> RunAsync(string endpoint, bool mcp, TimeSpan? timeout = null, params string[] extraArguments) {
        var assembly = LocateAssembly() ?? throw new InvalidOperationException("samples/ConsoleAgentSample has not been built.");
        var info = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "exec", assembly, "--url", endpoint }) info.ArgumentList.Add(argument);
        if (mcp) {
            info.ArgumentList.Add("--transport");
            info.ArgumentList.Add("mcp");
        }
        foreach (var argument in extraArguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the console agent sample.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        try {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"The console agent sample did not finish in time.{Environment.NewLine}{await output}");
        }
        return (process.ExitCode, (await output) + (await error));
    }
}
