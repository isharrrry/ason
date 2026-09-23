using System.Diagnostics;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Runs the stdio relay (<c>Ason.Bridge.McpHost</c>) the way an MCP-only agent does: as a child process whose
/// stdin/stdout carry the MCP protocol. The relay is cross-platform, so unlike the WPF samples it is also
/// exercised on Linux CI - as long as it has been built.
/// </summary>
internal static class RelayHost {

    /// <summary>The relay assembly, or <see langword="null"/> when it has not been built.</summary>
    public static string? LocateAssembly() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            var candidate = Path.Combine(directory.FullName, "src", "bin", configuration, "net9.0", "Ason.Bridge.McpHost.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Command and arguments that launch the relay for a gRPC or MCP application endpoint.</summary>
    public static (string Command, List<string> Arguments) LaunchCommand(string assembly, string endpoint, bool mcp, IReadOnlyDictionary<string, string>? headers = null) {
        var arguments = new List<string> { "exec", assembly, "--url", endpoint };
        if (mcp) arguments.Add("--transport");
        if (mcp) arguments.Add("mcp");
        if (headers is not null) {
            foreach (var header in headers) {
                arguments.Add("--header");
                arguments.Add($"{header.Key}={header.Value}");
            }
        }
        return ("dotnet", arguments);
    }

    /// <summary>
    /// Starts the relay directly (not through an MCP client) and waits for it to exit, so a test can assert the
    /// fast-fail path: a relay that cannot reach the application must say so and stop.
    /// </summary>
    public static async Task<(int ExitCode, string Output, string Error)> RunToExitAsync(string assembly, string endpoint, TimeSpan timeout, IReadOnlyDictionary<string, string>? headers = null) {
        var (command, arguments) = LaunchCommand(assembly, endpoint, mcp: false, headers);
        var info = new ProcessStartInfo(command) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);

        using var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {command}.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var cancellation = new CancellationTokenSource(timeout);
        try {
            await process.WaitForExitAsync(cancellation.Token);
        }
        catch (OperationCanceledException) {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw new TimeoutException($"The relay did not exit in time.{Environment.NewLine}{(await output)}{(await error)}");
        }
        return (process.ExitCode, await output, await error);
    }
}
