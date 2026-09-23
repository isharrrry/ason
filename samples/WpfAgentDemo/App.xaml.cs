using System.Reflection;
using System.Windows;
using Ason;
using WpfAgentDemo.Bridge;

namespace WpfAgentDemo;

public partial class App : Application {

    public App() {
#pragma warning disable WPF0001
        ThemeMode = ThemeMode.Light;
#pragma warning restore WPF0001
    }

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // Headless self-check: connect to an application, print what it published, and prove that this side
        // owns no operator. This is how the agent half of the split can be verified without a model and
        // without a desktop session.
        var verify = Array.FindIndex(e.Args, a => string.Equals(a, "--verify", StringComparison.OrdinalIgnoreCase));
        if (verify >= 0) {
            var endpoint = verify + 1 < e.Args.Length ? e.Args[verify + 1] : "http://localhost:5222";
            var transport = e.Args.Contains("--mcp", StringComparer.OrdinalIgnoreCase) ? AgentTransportKind.Mcp : AgentTransportKind.Grpc;
            var exitCode = Task.Run(() => VerifyAsync(endpoint, transport)).GetAwaiter().GetResult();
            Shutdown(exitCode);
            return;
        }

        new MainWindow().Show();
    }

    static async Task<int> VerifyAsync(string endpoint, AgentTransportKind transport) {
        var declared = typeof(App).Assembly.GetTypes().Count(t => t.GetCustomAttribute<AsonOperatorAttribute>() is not null);
        Console.WriteLine($"agent-operators={declared}");

        try {
            await using var bridge = await AgentBridge.ConnectAsync(endpoint, transport);
            var manifest = bridge.Manifest!;
            Console.WriteLine($"app={manifest.AppName} transport={transport} execution={manifest.Execution} capabilities=script:{manifest.Capabilities.ExecuteScript},function:{manifest.Capabilities.InvokeFunction}");
            Console.WriteLine($"operators={string.Join(",", manifest.Api.Operators.Select(o => o.TypeName))}");
            Console.WriteLine($"methods={manifest.Api.MethodCount} instances={string.Join(",", manifest.Instances.Select(i => i.Handle))}");
            Console.WriteLine($"library-proxies={(bridge.CreateOperatorsLibrary().BuildTask.GetAwaiter().GetResult().proxies.Length > 0 ? "ready" : "empty")}");

            var call = await bridge.InvokeFunctionAsync("AppInfoOperator", "Add", "[40,2]");
            Console.WriteLine($"remote-call={(call.Success ? call.Result?.GetRawText() : call.ErrorCode)}");
            return call.Success && call.Result?.GetInt32() == 42 ? 0 : 1;
        }
        catch (Exception ex) {
            Console.WriteLine($"verify-failed={ex.Message}");
            return 1;
        }
    }
}
