using Ason;
using Ason.Client.Execution;
using Ason.CodeGen;

namespace Ason.Bridge;

/// <summary>
/// The bridge: it exposes an application's operator API to an agent and forwards execution to an executor.
/// It owns the parts that must not be duplicated per transport - the manifest, capability checks, script
/// validation, operator-to-handle resolution and error codes - while each adapter (gRPC, MCP, OpenAPI, ...)
/// only has to map these calls onto its own protocol.
///
/// Build it on the thread that owns the operators (in WPF: the dispatcher thread) so operator calls are
/// marshalled to the same place the application itself runs them.
/// </summary>
public sealed class AsonBridgeRuntime : IAsyncDisposable {

    readonly SemaphoreSlim _startGate = new(1, 1);
    readonly IScriptValidator? _validator;
    OperatorApiCatalog? _catalog;
    bool _started;

    public AsonBridgeRuntime(AsonBridgeOptions options) {
        Options = options ?? throw new ArgumentNullException(nameof(options));
        if (options.Assemblies.Count == 0) {
            throw new ArgumentException(
                "At least one operator assembly is required: without one the proxy generator would scan every assembly loaded in the process.",
                nameof(options));
        }

        Executor = AsonExecutors.Create(options);
        Executor.Log += (_, e) => Log?.Invoke(this, e);
        _validator = options.ForbiddenScriptKeywords is null ? null : new KeywordScriptValidator(options.ForbiddenScriptKeywords);
    }

    public AsonBridgeOptions Options { get; }

    /// <summary>The executor every call is forwarded to.</summary>
    public IAsonExecutor Executor { get; }

    /// <summary>The operator API, once the runtime has started.</summary>
    public OperatorApiCatalog? Catalog => _catalog;

    /// <summary>Execution logs, for transports that stream them (mirrors <see cref="IAsonExecutor.Log"/>).</summary>
    public event EventHandler<AsonBridgeLogEventArgs>? Log;

    /// <summary>Reads the operator API and prepares the executor. Idempotent and safe to call concurrently.</summary>
    public async Task StartAsync(CancellationToken cancellationToken = default) {
        if (_started) return;
        await _startGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try {
            if (_started) return;
            var assemblies = Options.Assemblies.ToArray();
            _catalog = await Task.Run(() => OperatorApiCatalog.Describe(assemblies), cancellationToken).ConfigureAwait(false);
            await Executor.StartAsync(cancellationToken).ConfigureAwait(false);
            _started = true;
        }
        finally {
            _startGate.Release();
        }
    }

    /// <summary>Builds the payload every adapter publishes to its clients.</summary>
    public async Task<AsonBridgeManifest> GetManifestAsync(CancellationToken cancellationToken = default) {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var catalog = _catalog!;
        var instances = await ListInstancesAsync(cancellationToken).ConfigureAwait(false);
        var declarations = BuildInstanceDeclarations();
        var assemblies = Options.Assemblies.ToArray();

        return new AsonBridgeManifest(
            AsonBridgeProtocol.Version,
            Options.AppName,
            Executor.Name,
            Options.Capabilities,
            AsonBridgeApi.FromCatalog(catalog),
            catalog.ToMarkdown(),
            ProxySerializer.SerializeAll(assemblies) + declarations,
            ProxySerializer.SerializeSignatures(assemblies) + declarations,
            instances);
    }

    /// <summary>
    /// The operator instances that are addressable right now. Only types the manifest lists are reported: a
    /// root operator or an unmarked helper is not a call target, so advertising it would only invite
    /// failures.
    /// </summary>
    public async Task<IReadOnlyList<AsonBridgeInstance>> ListInstancesAsync(CancellationToken cancellationToken = default) {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        var addressable = new HashSet<string>(_catalog!.Operators.Select(o => o.TypeName), StringComparer.Ordinal);
        var instances = new List<AsonBridgeInstance>();

        if (Options.OperatorInstances is { } handles) {
            foreach (var entry in handles) {
                var typeName = entry.Value.GetType().Name;
                if (!addressable.Contains(typeName)) continue;
                instances.Add(new AsonBridgeInstance(entry.Key, typeName, entry.Value.IsAttached));
            }
        }

        if (Options.SingletonOperators is { } singletons) {
            foreach (var entry in singletons) {
                var typeName = entry.Value.GetType().Name;
                if (!addressable.Contains(typeName)) continue;
                if (instances.Any(i => i.Handle == entry.Key)) continue;
                instances.Add(new AsonBridgeInstance(entry.Key, typeName, true));
            }
        }

        return instances.OrderBy(i => i.Handle, StringComparer.Ordinal).ToList();
    }

    /// <summary>
    /// The whole-script interface. The script is validated, the generated proxy layer is prepended (including
    /// declarations for the live instances) and the executor evaluates it.
    /// </summary>
    public async Task<AsonBridgeCallResult> ExecuteScriptAsync(string script, bool includeProxyPreamble = true, CancellationToken cancellationToken = default) {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (!Options.Capabilities.ExecuteScript) return Disabled("executeScript");
        if (string.IsNullOrWhiteSpace(script)) return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ScriptRejected, "Empty script");
        if (_validator?.Validate(script) is { } rejection) return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ScriptRejected, rejection);

        var code = includeProxyPreamble ? BuildProxyPreamble() + "\n" + script : script;
        try {
            return await Executor.ExecuteScriptAsync(code, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            return AsonBridgeErrors.From(ex);
        }
    }

    /// <summary>
    /// The single-function interface: call one operator method precisely, without generating script text.
    /// A static module needs no handle; an instance operator is resolved through the live instance directory
    /// when exactly one instance of its type exists.
    /// </summary>
    public async Task<AsonBridgeCallResult> InvokeFunctionAsync(AsonBridgeFunctionCall call, CancellationToken cancellationToken = default) {
        if (call is null) throw new ArgumentNullException(nameof(call));
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (!Options.Capabilities.InvokeFunction) return Disabled("invokeFunction");
        if (string.IsNullOrWhiteSpace(call.Operator) || string.IsNullOrWhiteSpace(call.Method)) {
            return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.InvalidArguments, "Both 'operator' and 'method' are required.");
        }

        var known = _catalog!.Operators.FirstOrDefault(o => string.Equals(o.TypeName, call.Operator, StringComparison.Ordinal));
        if (known is null) {
            return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.OperatorNotFound, $"Unknown operator '{call.Operator}'. Read the manifest for the operators this application exposes.");
        }

        var handle = call.Handle;
        if (string.IsNullOrEmpty(handle)) {
            var live = LiveHandlesOf(known.TypeName);
            if (live.Count > 1) {
                return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.HandleAmbiguous,
                    $"Operator '{known.TypeName}' has {live.Count} live instances ({string.Join(", ", live)}); pass one of these handles.");
            }
            if (live.Count == 1) {
                handle = live[0];
            }
            else if (!known.IsStatic) {
                return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.HandleRequired,
                    $"Operator '{known.TypeName}' is an instance operator with no live instance. Call a static module, or pass the handle of a live instance.");
            }
        }

        try {
            var resolved = new AsonBridgeFunctionCall(known.TypeName, call.Method, handle, call.Arguments);
            return await Executor.InvokeFunctionAsync(resolved, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            return AsonBridgeErrors.From(ex);
        }
    }

    /// <summary>Pass-through to a tool on an MCP server the application itself consumes.</summary>
    public async Task<AsonBridgeCallResult> InvokeMcpToolAsync(string server, string tool, IReadOnlyDictionary<string, System.Text.Json.JsonElement> arguments, CancellationToken cancellationToken = default) {
        await StartAsync(cancellationToken).ConfigureAwait(false);
        if (!Options.Capabilities.InvokeMcpTool) return Disabled("invokeMcpTool");
        if (string.IsNullOrWhiteSpace(server) || string.IsNullOrWhiteSpace(tool)) {
            return AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.InvalidArguments, "Both 'server' and 'tool' are required.");
        }

        try {
            return await Executor.InvokeMcpToolAsync(server, tool, arguments ?? new Dictionary<string, System.Text.Json.JsonElement>(), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) {
            return AsonBridgeErrors.From(ex);
        }
    }

    public async ValueTask DisposeAsync() {
        await Executor.DisposeAsync().ConfigureAwait(false);
        _startGate.Dispose();
    }

    AsonBridgeCallResult Disabled(string capability) =>
        AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.NotSupported, $"The '{capability}' capability is disabled on this bridge.");

    /// <summary>
    /// Rebuilt per request on purpose: the instance declarations change as views open and close, and that is
    /// exactly how a script learns which variables it may use.
    /// </summary>
    string BuildProxyPreamble() =>
        ProxySerializer.SerializeAll(Options.Assemblies.ToArray()) + BuildInstanceDeclarations();

    string BuildInstanceDeclarations() =>
        OperatorVariableDeclarations.Build(Options.OperatorInstances, Options.SingletonOperators);

    List<string> LiveHandlesOf(string typeName) {
        var handles = new List<string>();
        if (Options.OperatorInstances is { } map) {
            foreach (var entry in map) {
                if (string.Equals(entry.Value.GetType().Name, typeName, StringComparison.Ordinal)) handles.Add(entry.Key);
            }
        }
        if (Options.SingletonOperators is { } singletons) {
            foreach (var entry in singletons) {
                if (string.Equals(entry.Value.GetType().Name, typeName, StringComparison.Ordinal)) handles.Add(entry.Key);
            }
        }
        return handles.OrderBy(h => h, StringComparer.Ordinal).ToList();
    }
}
