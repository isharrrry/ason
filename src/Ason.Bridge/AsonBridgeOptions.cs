using System.Collections.Concurrent;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Ason.Bridge;

/// <summary>Where a script is evaluated. This is a deployment decision, orthogonal to how it is transported.</summary>
public enum AsonBridgeExecution {
    /// <summary>In the application process itself. Fastest; the only barrier is the keyword filter.</summary>
    InProcess,
    /// <summary>In a local <c>Ason.ExternalExecutor</c> child process.</summary>
    ExternalProcess,
    /// <summary>In a local container (<c>docker run --rm -i &lt;image&gt;</c>).</summary>
    Docker,
    /// <summary>On a remote runner host (Ason.RemoteBridge), which spawns the executor on its side.</summary>
    RemoteRunner
}

/// <summary>
/// Everything the bridge needs from its host application. Mirrors the shape of
/// <see cref="Microsoft.Extensions.DependencyInjection.AsonRegistrationOptions"/>: plain settable properties,
/// so a host configures it wherever it wires up the rest of its services.
/// </summary>
public sealed class AsonBridgeOptions {

    /// <summary>Reported in the manifest so a client can tell which application it is talking to.</summary>
    public string AppName { get; set; } = "ASON application";

    /// <summary>
    /// The assemblies whose <c>[Ason*]</c> marked types form the API. At least one is required: with none,
    /// the proxy generator would scan every assembly loaded in the process.
    /// </summary>
    public IReadOnlyList<Assembly> Assemblies { get; set; } = Array.Empty<Assembly>();

    public AsonBridgeExecution Execution { get; set; } = AsonBridgeExecution.InProcess;

    /// <summary>Container image for <see cref="AsonBridgeExecution.Docker"/>.</summary>
    public string? DockerImage { get; set; }

    /// <summary>Explicit path of <c>Ason.ExternalExecutor</c> when the default discovery does not apply.</summary>
    public string? RunnerExecutablePath { get; set; }

    /// <summary>Base URL of a remote runner host, required for <see cref="AsonBridgeExecution.RemoteRunner"/>.</summary>
    public string? RemoteRunnerBaseUrl { get; set; }

    public AsonBridgeCapabilities Capabilities { get; set; } = new();

    /// <summary>Extra per-host filter applied on top of the <c>[AsonMethod]</c> marker.</summary>
    public Func<MethodInfo, bool>? AdditionalMethodFilter { get; set; }

    /// <summary>
    /// Keywords that make the bridge reject a script before it reaches the executor. <see langword="null"/>
    /// keeps only the empty-script check; set it to the default list to apply the same rule set a local
    /// <c>AsonClient</c> would.
    /// </summary>
    public IReadOnlyList<string>? ForbiddenScriptKeywords { get; set; }

    /// <summary>
    /// When no explicit <see cref="SynchronizationContext"/> is supplied, capture the one that is current when
    /// the runtime is built. In a WPF application that is the dispatcher, and it is what keeps operator calls
    /// on the UI thread. Turn it off only for hosts that have no UI affinity (tests, service hosts).
    /// </summary>
    public bool CaptureSynchronizationContext { get; set; } = true;

    /// <summary>Explicit synchronization context to marshal operator calls to; wins over capture.</summary>
    public SynchronizationContext? SynchronizationContext { get; set; }

    /// <summary>The live operator directory - normally <see cref="RootOperator.OperatorInstances"/>.</summary>
    public ConcurrentDictionary<string, OperatorBase>? OperatorInstances { get; set; }

    /// <summary>
    /// Marker-only operators ([AsonOperator] without <see cref="OperatorBase"/>), which a host materialises
    /// once and addresses by type name. The bridge reports them like any other live instance.
    /// </summary>
    public ConcurrentDictionary<string, object>? SingletonOperators { get; set; }

    /// <summary>An executor supplied by the host, replacing the one the execution location would resolve to.</summary>
    public IAsonExecutor? Executor { get; set; }

    public ILogger? Logger { get; set; }
}
