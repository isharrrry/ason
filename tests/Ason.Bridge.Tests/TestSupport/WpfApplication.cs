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
    /// Starts the application on a free port and waits until its bridge answers. Waiting on a real gRPC call
    /// rather than on a log line means the application is not considered ready before it can serve requests.
    /// </summary>
    public static async Task<WpfApplication> StartAsync(string executable, TimeSpan? startupTimeout = null) {
        var port = FreePort();
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

    static int FreePort() {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }
}
