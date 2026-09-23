using System.Diagnostics;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>Runs the WPF agent sample's headless self-check and returns what it printed.</summary>
internal static class WpfAgentRunner {

    /// <summary>The agent executable, or <see langword="null"/> when the Windows-only sample has not been built.</summary>
    public static string? LocateExecutable() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            foreach (var framework in new[] { "net9.0-windows", "net10.0-windows" }) {
                var candidate = Path.Combine(directory.FullName, "samples", "WpfAgentDemo", "bin", configuration, framework, "WpfAgentDemo.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>Runs <c>--verify &lt;endpoint&gt;</c> and waits for the process to finish.</summary>
    public static async Task<(int ExitCode, string Output)> VerifyAsync(string executable, string endpoint, bool mcp, TimeSpan? timeout = null) {
        var info = new ProcessStartInfo(executable) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--verify");
        info.ArgumentList.Add(endpoint);
        if (mcp) info.ArgumentList.Add("--mcp");

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        try {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"The agent self-check did not finish in time. Output so far:{Environment.NewLine}{await output}");
        }
        return (process.ExitCode, (await output) + (await error));
    }
}
