using System.Diagnostics;
using System.Net;
using System.Net.Sockets;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// Starts <c>samples/RemoteRunnerService</c> as a real process: the remote script host of a split deployment,
/// where the application's child executor runs (see <c>Ason.RemoteBridge</c>). The sample is started from its
/// built output with <c>dotnet exec</c> - never <c>dotnet run</c>, which would build and add startup jitter to
/// an end-to-end test.
/// </summary>
internal sealed class RemoteRunnerHost : IDisposable {

    readonly Process _process;
    readonly System.Text.StringBuilder _output = new();

    RemoteRunnerHost(Process process, string url) {
        _process = process;
        Url = url;
    }

    public string Url { get; }

    /// <summary>The built assembly, or <see langword="null"/> when the sample has not been built.</summary>
    public static string? LocateAssembly() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        if (directory is null) return null;

        foreach (var configuration in new[] { "Release", "Debug" }) {
            var candidate = Path.Combine(directory.FullName, "samples", "RemoteRunnerService", "bin", configuration, "net9.0", "RunnerServiceSample.dll");
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>
    /// Starts the runner on a free port and waits until it answers. Readiness is an HTTP request to the
    /// OpenAPI document, which the sample publishes in the Development environment - a cheaper and more honest
    /// probe than sleeping, and it does not require the hub's handshake to be spoken here.
    /// </summary>
    public static async Task<RemoteRunnerHost> StartAsync(TimeSpan? startupTimeout = null) {
        var assembly = LocateAssembly() ?? throw new InvalidOperationException("samples/RemoteRunnerService has not been built.");
        var port = FreePort();
        var url = $"http://localhost:{port}";

        var info = new ProcessStartInfo("dotnet") {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in new[] { "exec", assembly }) info.ArgumentList.Add(argument);
        info.Environment["ASPNETCORE_URLS"] = url;
        info.Environment["ASPNETCORE_ENVIRONMENT"] = "Development";
        info.Environment["DOTNET_NOLOGO"] = "1";

        var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start the remote runner sample.");
        var host = new RemoteRunnerHost(process, url);
        process.OutputDataReceived += (_, e) => { if (e.Data is not null) host._output.AppendLine(e.Data); };
        process.ErrorDataReceived += (_, e) => { if (e.Data is not null) host._output.AppendLine(e.Data); };
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        var deadline = DateTime.UtcNow + (startupTimeout ?? TimeSpan.FromSeconds(90));
        while (DateTime.UtcNow < deadline) {
            if (process.HasExited) {
                host.Dispose();
                throw new InvalidOperationException($"The remote runner exited with code {process.ExitCode}.{Environment.NewLine}{host._output}");
            }
            try {
                using var response = await http.GetAsync($"{url}/openapi/v1.json");
                if (response.IsSuccessStatusCode) return host;
            }
            catch (Exception) {
                // not listening yet
            }
            await Task.Delay(250);
        }

        host.Dispose();
        throw new InvalidOperationException($"The remote runner did not answer in time.{Environment.NewLine}{host._output}");
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

/// <summary>Runs only where both halves of the remote-runner deployment have been built.</summary>
public sealed class RequiresRemoteRunnerFactAttribute : FactAttribute {
    public RequiresRemoteRunnerFactAttribute() {
        if (RemoteRunnerHost.LocateAssembly() is null) {
            Skip = "samples/RemoteRunnerService has not been built, so this end-to-end test is skipped.";
            return;
        }
        if (ConsoleBridgeHost.LocateAssembly() is null) {
            Skip = "samples/ConsoleBridgeAppSample has not been built, so this end-to-end test is skipped.";
        }
    }
}
