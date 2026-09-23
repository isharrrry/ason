using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using Ason.Bridge.Grpc;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Starts the console application-side sample (<c>samples/ConsoleBridgeAppSample</c>) as a process and stops it
/// again. Unlike the WPF sample this one is cross-platform, so these tests run everywhere - including Linux CI.
/// </summary>
internal sealed class ConsoleBridgeHost : IDisposable {

    readonly Process _process;
    readonly System.Text.StringBuilder _output = new();

    ConsoleBridgeHost(Process process, string grpcUrl, string mcpUrl, int port) {
        _process = process;
        GrpcUrl = grpcUrl;
        McpUrl = mcpUrl;
        Port = port;
    }

    public string GrpcUrl { get; }

    public string McpUrl { get; }

    public int Port { get; }

    public static string? LocateAssembly() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            var candidate = Path.Combine(directory.FullName, "samples", "ConsoleBridgeAppSample", "bin", configuration, "net9.0", "ConsoleBridgeAppSample.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Starts the host and waits until it answers, so a test never races the startup.</summary>
    public static async Task<ConsoleBridgeHost> StartAsync(string execution = "inprocess", TimeSpan? startupTimeout = null) {
        Exception? lastFailure = null;
        for (var attempt = 0; attempt < 3; attempt++) {
            var (port, _) = TestPorts.Pair();
            try {
                return await StartOnceAsync(port, execution, startupTimeout).ConfigureAwait(false);
            }
            catch (InvalidOperationException ex) when (TestPorts.IsPortRace(ex)) {
                lastFailure = ex;
            }
        }
        throw lastFailure ?? new InvalidOperationException("Could not start the console bridge host.");
    }

    static async Task<ConsoleBridgeHost> StartOnceAsync(int port, string execution, TimeSpan? startupTimeout) {
        var assembly = LocateAssembly() ?? throw new InvalidOperationException("samples/ConsoleBridgeAppSample has not been built.");
        var info = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "exec", assembly, "--port", port.ToString(), "--execution", execution }) info.ArgumentList.Add(argument);

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the console bridge host.");
        var host = new ConsoleBridgeHost(process, $"http://localhost:{port}", $"http://localhost:{port + 1}/mcp", port);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) host._output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) host._output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        var deadline = DateTime.UtcNow + (startupTimeout ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline) {
            if (process.HasExited) throw new InvalidOperationException($"The console bridge host exited with code {process.ExitCode}.{Environment.NewLine}{host._output}");
            try {
                await using var client = GrpcAsonBridgeClient.Connect(host.GrpcUrl);
                await client.GetManifestAsync();
                return host;
            }
            catch (Exception) {
                await Task.Delay(250);
            }
        }

        host.Dispose();
        throw new InvalidOperationException($"The console bridge host did not answer in time.{Environment.NewLine}{host._output}");
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