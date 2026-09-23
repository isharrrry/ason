using System.Text.Json;

namespace Ason.Bridge.Tests;

/// <summary>
/// The copy-pasteable MCP client configuration (T14). Nothing here needs a running application: the point is
/// that the files a user is told to paste are valid JSON, point at the relay (or at the application's own MCP
/// endpoint), and name the tools the bridge actually publishes - a documentation claim that can silently rot.
/// </summary>
public class McpClientConfigTests {

    static string RepoFile(params string[] parts) {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Ason.sln"))) directory = directory.Parent;
        Assert.NotNull(directory);
        return Path.Combine(new[] { directory!.FullName }.Concat(parts).ToArray());
    }

    static JsonElement Servers(string file) {
        using var document = JsonDocument.Parse(File.ReadAllText(RepoFile("samples", "mcp", file)));
        return document.RootElement.GetProperty("mcpServers").Clone();
    }

    [Fact]
    public void The_stdio_configuration_starts_the_relay_against_the_application() {
        var servers = Servers("claude_desktop_config.json");

        var entry = Assert.Single(servers.EnumerateObject()).Value;
        Assert.Equal("dotnet", entry.GetProperty("command").GetString());

        var args = entry.GetProperty("args").EnumerateArray().Select(a => a.GetString() ?? string.Empty).ToList();
        Assert.Equal("exec", args[0]);
        Assert.EndsWith("Ason.Bridge.McpHost.dll", args[1]);

        // The path is a placeholder on purpose: an absolute path cannot be committed for every checkout, so the
        // file says so and the README repeats the replacement step. It also has to point where the build actually
        // puts the relay - the projects share src/bin, so a path under the project's own bin/ would be wrong.
        Assert.Contains("<repo>", args[1]);
        Assert.Contains("src/bin/", args[1].Replace('\\', '/'));
        Assert.Equal("--url", args[^2]);
        Assert.StartsWith("http://", args[^1]);
    }

    [Fact]
    public void The_http_configuration_points_at_the_applications_own_mcp_endpoint() {
        var servers = Servers("http_mcp_config.json");

        var entry = Assert.Single(servers.EnumerateObject()).Value;
        Assert.Equal("http", entry.GetProperty("type").GetString());
        Assert.EndsWith("/mcp", entry.GetProperty("url").GetString());
    }

    [Fact]
    public void The_configuration_readme_names_the_tools_the_bridge_publishes() {
        var readme = File.ReadAllText(RepoFile("samples", "mcp", "README.md"));

        foreach (var tool in new[] {
            Ason.Bridge.Mcp.AsonBridgeMcpTools.GetManifest,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.GetScriptApi,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.ListInstances,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.ExecuteScript,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.StreamScript,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.InvokeFunction,
            Ason.Bridge.Mcp.AsonBridgeMcpTools.InvokeMcpTool
        }) {
            Assert.Contains(tool, readme);
        }

        // Both authorization spellings T1 delivered, and the relay flags they belong to.
        Assert.Contains("--key", readme);
        Assert.Contains("--header", readme);
    }

    [Fact]
    public void The_callers_and_the_agent_exist_where_the_readme_says_they_do() {
        foreach (var file in new[] {
            RepoFile("samples", "python", "ason_mcp_caller", "main.py"),
            RepoFile("samples", "python", "ason_mcp_agent", "main.py"),
            RepoFile("samples", "python", "requirements-mcp.txt")
        }) {
            Assert.True(File.Exists(file), $"{file} is referenced by the documentation but missing.");
        }

        // The agent has to be honest about having no model to talk to - that is the contract the plan asked for.
        var agent = File.ReadAllText(RepoFile("samples", "python", "ason_mcp_agent", "main.py"));
        Assert.Contains("MY_OPEN_AI_KEY", agent);
        Assert.Contains("--instruction", agent);
        Assert.Contains("return 2", agent);
    }
}
