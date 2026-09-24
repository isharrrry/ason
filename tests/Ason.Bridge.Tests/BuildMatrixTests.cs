using System.Xml.Linq;

namespace Ason.Bridge.Tests;

/// <summary>
/// Wave 3 turned "which frameworks are supported, and where is that exercised" into a contract. This is that
/// contract as a single inventory: <b>every</b> project in the repository appears exactly once with the
/// frameworks it must declare, how CI reaches it, and - where something is deliberately narrower - why.
///
/// It exists because the first pass of Wave 3 pinned only what it happened to think about (<c>src/**</c> plus the
/// two WPF samples) and silently missed the console sample inventory: <c>ConsoleBridgeCallerSample</c> and
/// <c>ConsoleAgentSample</c> stayed single-target while their siblings went multi-target, and nothing failed.
/// A guard that covers the axis you remembered is not a guard; this one covers the inventory.
///
/// Three of the tests are about the repository rather than a single project, and each closes a hole that was
/// found the hard way: every csproj must be declared here (no silent additions), every
/// <c>ProjectReference</c> must resolve to a file that exists (<c>Ason.RemoteRunner.Tests</c> pointed at
/// <c>src/Ason.Runner</c>, deleted in an earlier wave, and MSBuild only *warns* MSB9008 - CI stayed green), and
/// no project may target net8.
/// </summary>
public class BuildMatrixTests {

    /// <summary>How CI exercises a project.</summary>
    enum Ci {
        /// <summary>A build command names it (all of its frameworks are built).</summary>
        Built,
        /// <summary>A test command names it, one leg per runtime where it ships several.</summary>
        Tested,
        /// <summary>Deliberately absent from CI - <see cref="Expectation.Reason"/> must say why.</summary>
        ByDesign
    }

    sealed record Expectation(string Project, string[] Frameworks, Ci Ci, string Reason);

    // The inventory. Order follows the repository: libraries, adapters, samples, test projects.
    // `Reason` is the *why* for anything that is not the obvious "three runtime legs"; it is what a reviewer
    // reads instead of re-deriving the decision from package metadata.
    static readonly Expectation[] Matrix = {
        // ---- libraries: one netstandard2.0 asset (what a legacy third-party host embeds) ----------------
        new("src/Ason.Abstractions/Ason.Abstractions.csproj", new[] { "netstandard2.0" }, Ci.Built,
            "One asset for .NET Framework 4.6.2+ and every modern .NET; the marker attributes a legacy host compiles against."),
        new("src/Ason.Runner.Core/Ason.Runner.Core.csproj", new[] { "netstandard2.0" }, Ci.Built,
            "The scripting engine; also the netstandard2.0 leg that the net472 smoke tests actually run."),
        new("src/Ason/Ason.csproj", new[] { "netstandard2.0" }, Ci.Built,
            "Model/orchestration side, operators and proxy generation; consumed by both legacy and modern hosts."),

        // ---- adapters: three runtime legs, net6.0 being the legacy-host floor --------------------------
        new("src/Ason.Bridge/Ason.Bridge.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "Not netstandard2.0: its abstractions use default interface members (CS8701) and every adapter needs the ASP.NET Core shared framework."),
        new("src/Ason.Bridge.Grpc/Ason.Bridge.Grpc.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "Grpc.AspNetCore 2.71.0 covers net6.0 and up."),
        new("src/Ason.Bridge.OpenApi/Ason.Bridge.OpenApi.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "The net6.0 leg is why Results.Empty became a local no-op EmptyResult (Results.Empty is net7+)."),
        new("src/Ason.RemoteBridge/Ason.RemoteBridge.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "The remote runner is an ASP.NET Core host; same floor as the other adapters."),
        new("src/Ason.ExternalExecutor/Ason.ExternalExecutor.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "Ships one host-manifest pair per framework (buildTransitive/host/<tfm>/), so the legs are also a packaging contract."),
        new("src/Ason.Bridge.Mcp/Ason.Bridge.Mcp.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "The official ModelContextProtocol.AspNetCore has no net6.0 asset and this repository ships no net8 tier; a net6.0 host is driven through the stdio relay instead."),
        new("src/Ason.Bridge.McpHost/Ason.Bridge.McpHost.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "Same SDK floor as Ason.Bridge.Mcp; it is the relay that gives a net6.0 application an MCP path."),

        // ---- samples -----------------------------------------------------------------------------------
        new("samples/LibDemo/LibDemo.csproj", new[] { "net6.0", "netstandard2.0" }, Ci.Built,
            "netstandard2.0 is the asset the net472 smoke leg loads; net6.0 is the modern consumer."),
        new("samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "The application side. MCP is gated per framework (AsonMcpSupported + ASON_MCP): the net6.0 leg publishes gRPC/OpenAPI and reports mcp=none."),
        new("samples/ConsoleBridgeCallerSample/ConsoleBridgeCallerSample.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "The caller side. It references only Ason.Bridge.Grpc (which covers all three legs) and reads capabilities.invokeMcpTool as a field, so it needs no conditional compilation at all."),
        new("samples/ConsoleAgentSample/ConsoleAgentSample.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "The cross-platform twin of WpfAgentDemo, so it carries the same MCP gate: net6.0 selects gRPC only and --transport mcp reports why."),
        new("samples/ConsoleMcpSample/ConsoleMcpSample.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "References only Ason (netstandard2.0), so the net6.0 floor costs nothing; it was simply never asked for before."),
        new("samples/ConsoleExtractorSample/ConsoleExtractorSample.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Built,
            "References only Ason (netstandard2.0); a legacy host can run the extractor."),
        new("samples/BlazorAdvancedApp/BlazorAdvancedApp.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "MudBlazor 8.x ships net8/net9 assets only, and there is no net8 tier here - so net9.0 is the *floor*, not the ceiling: the net10.0 leg consumes the net9.0 asset through NuGet's nearest-compatible rule. The project was absent from every CI build list, so it is now built."),
        new("samples/WpfAppOnlyDemo/WpfAppOnlyDemo.csproj", new[] { "net6.0-windows", "net9.0-windows", "net10.0-windows" }, Ci.Built,
            "Application side on the desktop; MCP gated per framework like the console sample."),
        new("samples/WpfAgentDemo/WpfAgentDemo.csproj", new[] { "net6.0-windows", "net9.0-windows", "net10.0-windows" }, Ci.Built,
            "Agent side on the desktop; the net6.0-windows leg has no MCP client and says so instead of pretending."),
        new("samples/WptDemoApp/WpfSampleApp.csproj", new[] { "net6.0-windows", "net9.0-windows", "net10.0-windows" }, Ci.Built,
            "The FlaUI demo application; every leg is built so the UI automation can target any of them (WPF_DEMO_TFM)."),
        new("samples/RemoteRunnerService/RunnerServiceSample.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "Both tiers the adapters ship. It exposes /openapi/v1.json as the readiness probe, so the Microsoft.AspNetCore.OpenApi package follows the leg (9.0.x for net9.0, 10.0.x for net10.0). The remote-runner end-to-end test drives the net9.0 leg, which it locates by path."),
        new("samples/RemoteRunnerService/RemoteRunnerService.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "The published-package consumer, on both tiers (the published Ason.RemoteBridge ships a net9.0 asset, which net10.0 consumes through the nearest-compatible rule). It was not built anywhere before, and it did not compile: it still referenced the retired Ason.RemoteRunner package."),
        new("samples/templates/Ason.ProjectTemplates.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "The template package itself; CI packs it so the templates cannot rot. Packing is identical per tier (the content is what ships), so this stays on both for consistency with what the templates scaffold."),
        new("samples/templates/Content/Ason.BlazorServer.Template/Ason.BlazorServer.Template.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "Scaffolds both tiers this repository ships. The floor stays a property of the shipped libraries; a scaffold is retargetable by the consumer."),
        new("samples/templates/Content/Ason.Console.Template/Ason.Console.Template.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "Scaffolds both tiers; consumed as Content Ason Version=\"*\" from NuGet, whose net9.0 asset serves the net10.0 leg."),
        new("samples/templates/Content/Ason.Maui.Template/Ason.Maui.Template.Server/Ason.Maui.Template.Server.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "The MAUI template's server half is platform-neutral, so CI builds both tiers of it, with the OpenAPI package following the leg."),
        new("samples/templates/Content/Ason.Maui.Template/Ason.Maui.Template/Ason.Maui.Template.csproj",
            new[] { "net9.0-android", "net9.0-ios", "net9.0-maccatalyst", "net10.0-android", "net10.0-ios", "net10.0-maccatalyst" }, Ci.ByDesign,
            "Declares both tiers' platform legs, but none of them can be built here: that needs the android/ios/maccatalyst workloads, which the hosted runners and the Linux test machine do not install (the file documents the same limitation for its commented-out tizen leg). The template's platform-neutral server half is built on both tiers instead."),
        new("samples/templates/Content/Ason.WinForms.Template/Ason.WinForms.Template.csproj", new[] { "net9.0-windows", "net10.0-windows" }, Ci.Built,
            "Windows-only scaffold, both Windows tiers; built by the Windows job."),
        new("samples/templates/Content/Ason.Wpf.Template/Ason.Wpf.Template.csproj", new[] { "net9.0-windows", "net10.0-windows" }, Ci.Built,
            "Windows-only scaffold, both Windows tiers; built by the Windows job."),

        // ---- test projects -----------------------------------------------------------------------------
        new("tests/Ason.Bridge.Tests/Ason.Bridge.Tests.csproj", new[] { "net9.0", "net10.0" }, Ci.Tested,
            "References Ason.Bridge.Mcp, so net9.0 is its floor; one --framework leg per runtime, because a multi-target dotnet test writes a single TRX for all of its frameworks."),
        new("tests/Ason.Tests/Ason.Tests.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Tested,
            "Three legs: Microsoft.AspNetCore.Mvc.Testing 8.0.1 (net8.0 asset only) used to pin this suite to net9.0, but nothing in the repository referenced it - removing the unused reference and setting LangVersion latest (the net6.0 leg defaults to C# 10 and the sources use C# 12) is all it took. The MCP client tests stay out through the hermetic filter."),
        new("tests/Ason.Runner.Tests/Ason.Runner.Tests.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Tested,
            "Runs the scripting engine's own tests on the oldest and newest supported host."),
        new("tests/Ason.RemoteRunner.Tests/Ason.RemoteRunner.Tests.csproj", new[] { "net6.0", "net9.0", "net10.0" }, Ci.Tested,
            "Also pins the fix for the stale ProjectReference to src/Ason.Runner (deleted in an earlier wave; MSBuild only warned MSB9008)."),
        new("tests/LibDemo.SmokeTests/LibDemo.SmokeTests.csproj", new[] { "net6.0", "net9.0", "net10.0", "net472" }, Ci.Tested,
            "The net472 leg is the only place where what a .NET Framework host receives is actually run."),
        new("tests/TestMcpServer/TestMcpServer.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "The protocol fixture the MCP client tests launch. Both tiers the MCP family ships, so a net10.0-only break cannot hide; built so it cannot rot."),
        new("tests/TestRemoteExecutorServer/TestRemoteExecutorServer.csproj", new[] { "net9.0", "net10.0" }, Ci.Built,
            "Manual fixture for the remote-executor scenarios, on both tiers, with the OpenAPI package following the leg (that document is its readiness probe). Built so it cannot rot."),
        new("tests/WpfDemoApp.UiTests/WpfDemoApp.UiTests.csproj", new[] { "net6.0-windows", "net9.0-windows", "net10.0-windows" }, Ci.Tested,
            "FlaUI.UIA3 5.0.0 covers net6.0-windows7.0 and up; the step is continue-on-error because UI Automation needs an interactive desktop, so this is reachability rather than a gate."),
    };

    static Expectation Entry(string project) =>
        Matrix.FirstOrDefault(e => e.Project == project)
        ?? throw new InvalidOperationException($"{project} is not declared in the build matrix");

    static string RepositoryRoot() {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException($"Ason.sln was not found above {AppContext.BaseDirectory}");
    }

    static string FullPath(string relativePath) =>
        Path.Combine(RepositoryRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar));

    static string [] Frameworks(string project) => FrameworksOf(FullPath(project));

    static string[] FrameworksOf(string absolutePath) {
        var document = XDocument.Load(absolutePath);
        var element = document.Descendants().FirstOrDefault(e => e.Name.LocalName is "TargetFrameworks" or "TargetFramework");
        Assert.True(element is not null, $"{absolutePath} declares no target framework");
        return element!.Value.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    }
    static string Text(string relativePath) => File.ReadAllText(FullPath(relativePath));

    static IEnumerable<string> AllProjects() {
        var root = RepositoryRoot();
        return Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}")
                        && !path.Contains($"{Path.DirectorySeparatorChar}artifacts{Path.DirectorySeparatorChar}"))
            .Select(path => Path.GetRelativePath(root, path).Replace(Path.DirectorySeparatorChar, '/'))
            .OrderBy(path => path, StringComparer.Ordinal);
    }

    static string CiText() {
        var root = RepositoryRoot();
        var workflow = File.ReadAllText(Path.Combine(root, ".github", "workflows", "ci.yml"));
        var local = File.ReadAllText(Path.Combine(root, "scripts", "ci-linux.sh"));
        return (workflow + "\n" + local).Replace('\\', '/');
    }

    public static IEnumerable<object[]> DeclaredProjects() => Matrix.Select(e => new object[] { e.Project });

    // ---- one case per project: the frameworks it declares --------------------------------------------

    [Theory]
    [MemberData(nameof(DeclaredProjects))]
    public void Project_declares_the_frameworks_the_matrix_requires(string project) {
        var expected = Entry(project);
        // The set is the contract, not the order: WpfSampleApp lists its legs newest-first.
        Assert.Equal(
            expected.Frameworks.OrderBy(f => f, StringComparer.Ordinal),
            Frameworks(project).OrderBy(f => f, StringComparer.Ordinal));
    }

    [Theory]
    [MemberData(nameof(DeclaredProjects))]
    public void Project_is_reached_by_ci(string project) {
        var expected = Entry(project);
        if (expected.Ci == Ci.ByDesign) {
            // An exception without a reason is a hole with better manners.
            Assert.False(string.IsNullOrWhiteSpace(expected.Reason), $"{project} is absent from CI without a recorded reason");
            Assert.True(expected.Reason.Length > 40, $"{project}: the recorded reason must explain the alternative, not name it");
            return;
        }
        Assert.Contains(expected.Project, CiText(), StringComparison.Ordinal);
    }

    // ---- the three repository-wide holes -------------------------------------------------------------

    [Fact]
    public void Every_project_in_the_repository_is_declared_in_the_matrix() {
        var declared = Matrix.Select(e => e.Project).ToHashSet(StringComparer.Ordinal);
        var present = AllProjects().ToHashSet(StringComparer.Ordinal);

        var undeclared = present.Except(declared).OrderBy(p => p, StringComparer.Ordinal).ToList();
        var missing = declared.Except(present).OrderBy(p => p, StringComparer.Ordinal).ToList();

        Assert.True(undeclared.Count == 0,
            "These projects exist but are not in the build matrix - add each one with its expected frameworks, how CI reaches it and why:\n  " + string.Join("\n  ", undeclared));
        Assert.True(missing.Count == 0,
            "The build matrix names projects that do not exist:\n  " + string.Join("\n  ", missing));
    }

    [Fact]
    public void Every_project_reference_resolves_to_a_file_that_exists() {
        // Ason.RemoteRunner.Tests referenced src/Ason.Runner/Ason.Runner.csproj, which an earlier wave deleted
        // (its namespace AsonRunner now lives in Ason.Runner.Core). MSBuild reports MSB9008 as a *warning*, so the
        // suite kept building and staying green against a reference that resolved to nothing.
        var dangling = new List<string>();
        foreach (var project in AllProjects()) {
            var document = XDocument.Load(FullPath(project));
            foreach (var reference in document.Descendants().Where(e => e.Name.LocalName == "ProjectReference")) {
                var include = reference.Attribute("Include")?.Value;
                if (string.IsNullOrWhiteSpace(include)) continue;
                // MSBuild accepts either separator, and this repository uses both (`..\..\src\...` in the samples,
                // `../../src/...` in the test projects). Normalising only '/' made every backslash-style reference
                // look like a literal file name on Linux, which is exactly where this test first ran.
                var relative = include.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
                var target = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(FullPath(project))!, relative));
                if (!File.Exists(target)) dangling.Add($"{project} -> {include}");
            }
        }
        Assert.True(dangling.Count == 0,
            "These ProjectReference entries point at files that do not exist (MSBuild only warns MSB9008):\n  " + string.Join("\n  ", dangling));
    }

    [Fact]
    public void No_project_targets_net8() {
        var offenders = AllProjects()
            .Where(path => FrameworksOf(FullPath(path)).Any(f => f.StartsWith("net8.", StringComparison.OrdinalIgnoreCase)))
            .ToList();
        Assert.Empty(offenders);
    }

    // ---- the per-framework MCP gate, and the two mistakes that are easy to repeat --------------------

    [Theory]
    [InlineData("samples/WpfAppOnlyDemo/WpfAppOnlyDemo.csproj")]
    [InlineData("samples/WpfAgentDemo/WpfAgentDemo.csproj")]
    [InlineData("samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj")]
    [InlineData("samples/ConsoleAgentSample/ConsoleAgentSample.csproj")]
    public void The_mcp_gate_is_one_property_and_a_compile_symbol(string project) {
        // One property decides whether a leg carries MCP, and both the reference and ASON_MCP hang off it, so a
        // new leg is added by editing TargetFrameworks alone. The gate must be a negative condition on the
        // framework name: comparing $(TargetFrameworkVersion) fails *silently* because it carries a 'v' prefix.
        var text = Text(project);
        Assert.Contains("AsonMcpSupported", text);
        Assert.Contains("Condition=\"'$(AsonMcpSupported)' == 'true'\"", text);
        Assert.Contains("ASON_MCP", text);
        Assert.DoesNotContain("VersionGreaterThanOrEquals('$(TargetFrameworkVersion)'", text);
        Assert.DoesNotContain("VersionGreaterThanOrEquals($(TargetFrameworkVersion)", text);
    }

    [Fact]
    public void No_sample_hand_writes_a_net9_0_output_path() {
        foreach (var project in new[] {
            "samples/ConsoleBridgeAppSample/ConsoleBridgeAppSample.csproj",
            "samples/RemoteRunnerService/RunnerServiceSample.csproj",
            "samples/WpfAppOnlyDemo/WpfAppOnlyDemo.csproj",
        }) {
            var text = Text(project);
            if (!text.Contains("AsonExecutorHostFiles")) continue;
            Assert.DoesNotContain(@"\net9.0\", text);
        }
    }
}
