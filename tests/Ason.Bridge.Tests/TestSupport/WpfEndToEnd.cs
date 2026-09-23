namespace Ason.Bridge.Tests;

/// <summary>
/// Tests that start the Windows-only WPF samples. They share one collection so xunit runs them one after
/// another: each starts real processes and binds real ports.
/// </summary>
public static class WpfEndToEnd {
    public const string CollectionName = "wpf-samples";
}

[CollectionDefinition(WpfEndToEnd.CollectionName, DisableParallelization = true)]
public sealed class WpfEndToEndCollection {
}
