using System.Collections.Concurrent;
using System.Reflection;
using System.Text.Json;
using Ason;
using Ason.CodeGen;
using AsonRunner;

namespace Ason.Bridge;

/// <summary>
/// The ASON runtime as an executor: one <c>RunnerClient</c>, whose existing switches already cover every
/// execution location - in-process evaluation, a local <c>Ason.ExternalExecutor</c> child process, a
/// container, or a remote runner host. Operators are resolved back into this process (or the host that owns
/// them), which is what keeps application data and credentials on the application side.
/// </summary>
public sealed class RunnerClientAsonExecutor : IAsonExecutor {

    readonly AsonBridgeOptions _options;
    readonly RunnerClient _runner;
    readonly JsonSerializerOptions _json = new(JsonSerializerDefaults.Web) { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
    readonly Task _ready;

    public RunnerClientAsonExecutor(AsonBridgeOptions options) {
        _options = options ?? throw new ArgumentNullException(nameof(options));

        var assemblies = options.Assemblies.ToArray();
        if (assemblies.Length == 0) {
            throw new ArgumentException(
                "At least one operator assembly is required: without one the proxy generator would scan every assembly loaded in the process.",
                nameof(options));
        }

        var extraFilter = options.AdditionalMethodFilter;
        var library = new OperatorBuilder()
            .AddAssemblies(assemblies)
            .SetBaseFilter(mi => mi.GetCustomAttribute<AsonMethodAttribute>() != null && (extraFilter is null || extraFilter(mi)))
            .Build();

        var handles = options.OperatorInstances ?? new ConcurrentDictionary<string, OperatorBase>(StringComparer.Ordinal);
        var singletons = options.SingletonOperators ?? new ConcurrentDictionary<string, object>(StringComparer.Ordinal);

        _runner = new RunnerClient(handles, ResolveSynchronizationContext(options), singletons) {
            Mode = ToExecutionMode(options.Execution)
        };
        if (!string.IsNullOrWhiteSpace(options.RunnerExecutablePath)) _runner.RunnerExecutablePath = options.RunnerExecutablePath;
        if (!string.IsNullOrWhiteSpace(options.DockerImage)) _runner.DockerImage = options.DockerImage!;
        if (options.Execution == AsonBridgeExecution.RemoteRunner) {
            if (string.IsNullOrWhiteSpace(options.RemoteRunnerBaseUrl)) {
                throw new ArgumentException("RemoteRunnerBaseUrl is required for the RemoteRunner execution location.", nameof(options));
            }
            _runner.UseRemote = true;
            _runner.RemoteUrl = options.RemoteRunnerBaseUrl!.TrimEnd('/');
        }

        _runner.Log += (_, e) => Log?.Invoke(this, new AsonBridgeLogEventArgs(e.Level.ToString(), e.Message, e.Source, e.Exception));

        // The method cache is what lets the runtime answer "invoke" callbacks coming back from a separate
        // executor process, and what resolves the single-function interface. It is expensive reflection, so it
        // is awaited rather than repeated per call.
        _ready = library.BuildTask.ContinueWith(
            t => { _runner.MethodCache = t.Result.cache; },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    public string Name => _options.Execution switch {
        AsonBridgeExecution.InProcess => "in-process",
        AsonBridgeExecution.ExternalProcess => "external-process",
        AsonBridgeExecution.Docker => "docker",
        AsonBridgeExecution.RemoteRunner => "remote-runner",
        _ => _options.Execution.ToString()
    };

    /// <summary>The underlying client, for hosts that need to register MCP clients or subscribe to calls.</summary>
    public RunnerClient Runner => _runner;

    /// <summary>
    /// The MCP servers the host registered on the runtime. Read live from the runner, so registering a client
    /// after the bridge started is visible without rebuilding anything.
    /// </summary>
    public IReadOnlyCollection<string> McpServers => _runner.McpServerNames;

    public event EventHandler<AsonBridgeLogEventArgs>? Log;

    public async Task StartAsync(CancellationToken cancellationToken = default) {
        await _ready.ConfigureAwait(false);
        // No-op in in-process mode; starts the child process / container / remote session otherwise.
        await _runner.StartProcessAsync().ConfigureAwait(false);
    }

    public async Task<AsonBridgeCallResult> ExecuteScriptAsync(string code, CancellationToken cancellationToken = default) {
        var result = await _runner.ExecuteAsync(code, cancellationToken).ConfigureAwait(false);
        return AsonBridgeCallResult.Ok(result);
    }

    public async Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        var arguments = call.EffectiveArguments.Select(a => (object?)a).ToArray();
        var result = await _runner.InvokeOperatorAsync<object>(call.Operator, call.Method, call.Handle, arguments).ConfigureAwait(false);
        return AsonBridgeCallResult.Ok(ToElement(result));
    }

    public async Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, JsonElement> arguments, CancellationToken cancellationToken = default) {
        var args = arguments.ToDictionary(kv => kv.Key, kv => (object?)kv.Value, StringComparer.Ordinal);
        var result = await _runner.InvokeMcpToolAsync<object>(server, tool, args).ConfigureAwait(false);
        return AsonBridgeCallResult.Ok(ToElement(result));
    }

    public ValueTask DisposeAsync() => new(_runner.StopAsync());

    JsonElement? ToElement(object? result) => result switch {
        null => null,
        JsonElement element => element,
        _ => JsonSerializer.SerializeToElement(result, _json)
    };

    static SynchronizationContext? ResolveSynchronizationContext(AsonBridgeOptions options) =>
        options.SynchronizationContext ?? (options.CaptureSynchronizationContext ? SynchronizationContext.Current : null);

    static ExecutionMode ToExecutionMode(AsonBridgeExecution execution) => execution switch {
        AsonBridgeExecution.InProcess => ExecutionMode.InProcess,
        AsonBridgeExecution.ExternalProcess => ExecutionMode.ExternalProcess,
        AsonBridgeExecution.Docker => ExecutionMode.Docker,
        // A remote runner decides on its own side; by convention it spawns an executor there.
        AsonBridgeExecution.RemoteRunner => ExecutionMode.ExternalProcess,
        _ => ExecutionMode.InProcess
    };
}
