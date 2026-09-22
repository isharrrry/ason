namespace WpfDemoApp.UiTests;

/// <summary>
/// Locates the WPF demo build that the UI tests drive. The target framework is selectable through
/// <c>WPF_DEMO_TFM</c> so the same suite can validate the net6.0 / net9.0 / net10.0 legs.
/// </summary>
internal static class AppUnderTest {

    public const string TfmVariable = "WPF_DEMO_TFM";
    public const string ConfigurationVariable = "WPF_DEMO_CONFIG";
    public const string DefaultTfm = "net9.0-windows";
    public const string ApiKeyVariable = "MY_OPEN_AI_KEY";

    public static string TargetFramework =>
        Environment.GetEnvironmentVariable(TfmVariable) is { Length: > 0 } tfm ? tfm : DefaultTfm;

    public static string Configuration =>
        Environment.GetEnvironmentVariable(ConfigurationVariable) is { Length: > 0 } config ? config : "Release";

    public static bool HasApiKey => !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ApiKeyVariable));

    public static string ExePath {
        get {
            var exe = Path.Combine(RepoRoot(), "samples", "WptDemoApp", "bin", Configuration, TargetFramework, "WpfSampleApp.exe");
            if (!File.Exists(exe)) {
                throw new FileNotFoundException(
                    $"WPF demo build not found. Build it first: dotnet build samples/WptDemoApp/WpfSampleApp.csproj -c {Configuration} (missing: {exe})", exe);
            }
            return exe;
        }
    }

    public static string RepoRoot() {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "Ason.sln"))) {
            dir = dir.Parent;
        }
        return dir?.FullName
            ?? throw new InvalidOperationException($"Could not locate the repository root (Ason.sln) above '{AppContext.BaseDirectory}'.");
    }
}
