namespace Ason.Bridge;

/// <summary>Resolves the executor for a host's configuration.</summary>
public static class AsonExecutors {

    /// <summary>
    /// Returns the executor the options describe: the one the host injected, or a
    /// <see cref="RunnerClientAsonExecutor"/> bound to the configured execution location.
    /// </summary>
    public static IAsonExecutor Create(AsonBridgeOptions options) {
        if (options is null) throw new ArgumentNullException(nameof(options));
        return options.Executor ?? new RunnerClientAsonExecutor(options);
    }
}
