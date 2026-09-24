using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;

namespace AsonRunner;

public sealed class ScriptRunnerProcessHost : IAsyncDisposable {
    private readonly ExecutionMode _mode;
    private readonly string? _dockerImage;
    private readonly ILogger? _logger;
    private readonly string? _runnerPathOverride; // new

    private Process? _process;
    private StreamWriter? _stdin;
    private StreamReader? _stdout;
    private CancellationTokenSource _cts = new();

    public event Func<string, Task>? LineReceived;
    public event Func<string, Task>? ProcessExited;

    public ScriptRunnerProcessHost(ExecutionMode mode, string? dockerImage, ILogger? logger, string? runnerExecutablePath = null) {
        _mode = mode;
        _dockerImage = dockerImage;
        _logger = logger;
        _runnerPathOverride = runnerExecutablePath;
    }

    public bool IsRunning => _process is { HasExited: false };

    public async Task StartAsync() {
        if (_mode == ExecutionMode.InProcess) throw new InvalidOperationException("InProcess mode does not require external host.");
        if (IsRunning) return;

        string runnerBaseName = "Ason.ExternalExecutor";
        string? baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
        if (string.IsNullOrEmpty(baseDir)) throw new InvalidOperationException("Cannot resolve base directory for external executor.");

        string? overridePath = _runnerPathOverride;
        string dllPath;
        string exePath;
        if (!string.IsNullOrWhiteSpace(overridePath)) {
            // The netstandard2.0 BCL carries no nullable annotations, so `IsNullOrWhiteSpace` cannot narrow
            // `overridePath` for the compiler (CS8602 on every use below). The alias states what the guard has
            // already proved; on the net9.0 leg the annotated BCL narrows the same code by itself.
            string overrideTarget = overridePath!;
            // If a directory was supplied, compose expected names inside it
            if (Directory.Exists(overrideTarget)) {
                dllPath = Path.Combine(overrideTarget, runnerBaseName + ".dll");
                exePath = Path.Combine(overrideTarget, runnerBaseName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty));
            } else {
                // A file path was supplied explicitly (.dll or .exe)
                dllPath = overrideTarget.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? overrideTarget : Path.Combine(Path.GetDirectoryName(overrideTarget)!, runnerBaseName + ".dll");
                exePath = overrideTarget.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? overrideTarget : Path.Combine(Path.GetDirectoryName(overrideTarget)!, runnerBaseName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty));
            }
        } else {
            dllPath = Path.Combine(baseDir, runnerBaseName + ".dll");
            exePath = Path.Combine(baseDir, runnerBaseName + (RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : string.Empty));
        }

        string launchFile;
        bool useDotnet;
        if (File.Exists(exePath)) { launchFile = exePath; useDotnet = false; }
        else if (File.Exists(dllPath)) { launchFile = dllPath; useDotnet = true; }
        else throw new FileNotFoundException($"Could not locate external executor. Searched: '{dllPath}'. Make sure the Ason.ExternalExecutor NuGet package is added.");

        var si = new ProcessStartInfo {
            FileName = _mode == ExecutionMode.Docker ? "docker" : (useDotnet ? "dotnet" : launchFile),
            Arguments = _mode == ExecutionMode.Docker ? BuildDockerArgs(_dockerImage) : (useDotnet ? $"\"{launchFile}\"" : string.Empty),
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            StandardOutputEncoding = new UTF8Encoding(false),
            // StandardInputEncoding is netstandard2.1+; stdin is written through an explicit UTF-8 StreamWriter
            // below instead, which is what this property would have configured anyway.
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = si, EnableRaisingEvents = true };
        _process.Exited += async (_, _) => { try { if (ProcessExited != null) await ProcessExited.Invoke("exited"); } catch { } };
        _process.ErrorDataReceived += (_, e) => { if (!string.IsNullOrEmpty(e.Data)) _logger?.LogDebug("[ScriptRunner stderr] {Line}", e.Data); };
        _process.Start();
        _process.BeginErrorReadLine();

        _stdin = new StreamWriter(_process.StandardInput.BaseStream, new UTF8Encoding(false)) { AutoFlush = true };
        _stdout = new StreamReader(_process.StandardOutput.BaseStream, new UTF8Encoding(false), false);

        _ = Task.Run(ReadLoopAsync, _cts.Token);
        await Task.CompletedTask;
    }

    public Task SendLineAsync(string line) {
        if (!IsRunning) throw new InvalidOperationException("Process is not running.");
        if (_stdin == null) throw new InvalidOperationException("stdin not initialized");
        return _stdin.WriteLineAsync(line);
    }

    private async Task ReadLoopAsync() {
        var reader = _stdout;
        if (reader == null) return;
        while (!_cts.IsCancellationRequested) {
            string? line;
            try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
            catch { break; }
            if (line is null) break; // EOF
            if (line.Length == 0) continue;
            var handler = LineReceived;
            if (handler != null) {
                try { await handler.Invoke(line); } catch { }
            }
        }
    }

    public async ValueTask DisposeAsync() {
        try { _cts.Cancel(); } catch { }
        var p = _process;
        if (p != null) {
            try {
                if (!p.HasExited) {
                    if (_mode == ExecutionMode.Docker) {
                        SafeKill(p, tree: false);
                    } else {
                        var attemptedTree = CanAttemptTreeKill() && TryKillTree(p);
                        if (!attemptedTree) {
                            _logger?.LogDebug("Killing only pid={Pid}: tree kill is unavailable or refused on this runtime.", SafeProcessId(p));
                            SafeKill(p, tree: false);
                        }
                    }
                    try { p.WaitForExit(2000); } catch { }
                }
            } catch (Exception ex) {
                _logger?.LogDebug(ex, "Failed to terminate runner process (pid={Pid}).", SafeProcessId(p));
            }
            try { p.Dispose(); } catch { }
        }
        try { _stdin?.Dispose(); } catch { }
        try { _stdout?.Dispose(); } catch { }
        _stdin = null; _stdout = null; _process = null;
        await Task.CompletedTask;
    }

    /// <summary><c>Kill(entireProcessTree:)</c> exists from .NET Core 3.0 / netstandard2.1 on; this library ships
    /// the netstandard2.0 asset, so the method is resolved once at runtime and a plain <c>Kill()</c> is the
    /// fallback for hosts that do not have it.</summary>
    static readonly MethodInfo? KillTreeMethod = typeof(Process).GetMethod("Kill", new[] { typeof(bool) });

    private static bool TryKillTree(Process p) {
        if (KillTreeMethod is null) return false;
        try { KillTreeMethod.Invoke(p, new object[] { true }); return true; } catch { return false; }
    }

    private static void SafeKill(Process p, bool tree) {
        try {
            if (tree && TryKillTree(p)) return;
            p.Kill();
        } catch { }
    }

    /// <summary>
    /// Whether killing the whole process tree can be attempted. On Windows this needs a privileged process
    /// (.NET 8's <c>Environment.IsPrivilegedProcess</c>), and that refinement has to be probed *at runtime*:
    /// this library ships the netstandard2.0 asset only, so a compile-time <c>#if NET8_0_OR_GREATER</c> would be
    /// false for every consumer, including the .NET 8+ ones that could answer the question.
    /// </summary>
    private static bool CanAttemptTreeKill() {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) return true;
        var property = typeof(Environment).GetProperty("IsPrivilegedProcess", BindingFlags.Public | BindingFlags.Static);
        if (property is null || property.PropertyType != typeof(bool)) return true;
        try { return (bool)property.GetValue(null)!; } catch { return true; }
    }

    private static string BuildDockerArgs(string? image) => $"run --rm -i {(string.IsNullOrWhiteSpace(image) ? DockerInfo.DockerImageString : image)}";
    private static int SafeProcessId(Process p) { try { return p.Id; } catch { return -1; } }
}

public static class DockerInfo {
    public static string DockerImageString {
        get {
            var version = Assembly.GetExecutingAssembly().GetName().Version;
            string versionStr = $"{version!.Major}.{version.Minor}.{version.Build}";
            return $"ghcr.io/alexgoon/ason:{versionStr}";
        }
    }
}
