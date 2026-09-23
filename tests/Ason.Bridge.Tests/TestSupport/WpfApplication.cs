using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Starts the WPF application in its headless bridge mode and stops it again. The application keeps its own
/// dispatcher thread, so this is the real thing: a UI application serving its operators over the network.
/// </summary>
internal sealed class WpfApplication : IDisposable {

    readonly Process _process;
    readonly System.Text.StringBuilder _stderr = new();

    WpfApplication(Process process, string grpcUrl) {
        _process = process;
        GrpcUrl = grpcUrl;
    }

    public string GrpcUrl { get; }

    /// <summary>The executable, or <see langword="null"/> when the Windows-only sample has not been built.</summary>
    public static string? LocateExecutable() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            foreach (var framework in new[] { "net9.0-windows", "net10.0-windows" }) {
                var candidate = Path.Combine(directory.FullName, "samples", "WpfAppOnlyDemo", "bin", configuration, framework, "WpfAppOnlyDemo.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }
        return null;
    }

    /// <summary>
    /// Starts the application on a free port pair and waits until its bridge answers. Waiting on a real gRPC
    /// call rather than on a log line means the application is not considered ready before it can serve
    /// requests; a port race (the pair is claimed between the check and the bind) is retried.
    /// </summary>
    public static async Task<WpfApplication> StartAsync(string executable, TimeSpan? startupTimeout = null) {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++) {
            var (grpcPort, _) = TestPorts.Pair();
            try {
                return await StartOnceAsync(executable, grpcPort, startupTimeout).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (TestPorts.IsPortRace(ex)) {
                lastFailure = ex;
            }
        }
        throw lastFailure ?? new InvalidOperationException("Could not start the WPF application sample.");
    }

    static async Task<WpfApplication> StartOnceAsync(string executable, int port, TimeSpan? startupTimeout) {
        var info = new ProcessStartInfo(executable) {
            RedirectStandardInput = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        info.ArgumentList.Add("--bridge-only");
        info.ArgumentList.Add("--port");
        info.ArgumentList.Add(port.ToString());

        var process = Process.Start(info) ?? throw new InvalidOperationException($"Could not start {executable}.");
        var application = new WpfApplication(process, $"http://localhost:{port}");
        process.OutputDataReceived += (_, _) => { };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) application._stderr.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + (startupTimeout ?? TimeSpan.FromSeconds(60));
        var failure = string.Empty;
        while (DateTime.UtcNow < deadline) {
            if (process.HasExited) {
                failure = $"The application exited with code {process.ExitCode}.{Environment.NewLine}{application._stderr}";
                break;
            }
            try {
                await using var client = Ason.Bridge.Grpc.GrpcAsonBridgeClient.Connect(application.GrpcUrl);
                await client.GetManifestAsync();
                return application;
            }
            catch (Exception) {
                await Task.Delay(250);
            }
        }

        application.Dispose();
        throw new InvalidOperationException(
            string.IsNullOrEmpty(failure)
                ? $"The application's bridge did not answer within the startup timeout.{Environment.NewLine}{application._stderr}"
                : failure);
    }

    public void Dispose() {
        try {
            if (!_process.HasExited) {
                _process.Kill(entireProcessTree: true);
                _process.WaitForExit(10_000);
            }
        }
        catch { }
        finally {
            _process.Dispose();
        }
    }

    /// <summary>
    /// The sample binds two ports (gRPC and MCP, next to each other); see <see cref="TestPorts.Pair"/>.
    /// </summary>
}
