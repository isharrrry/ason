using System.Windows;
using System.Windows.Threading;
using WpfAppOnlyDemo.Bridge;

namespace WpfAppOnlyDemo;

/// <summary>
/// The application side of a split deployment: a WPF application that contains <c>[Ason*]</c> operators and
/// hosts gRPC + MCP bridge services, and no agent of its own.
///
/// Started with <c>--bridge-only</c> it keeps the dispatcher running without showing a window, which is how a
/// test drives the real application (operators included) over the wire.
/// </summary>
public partial class App : Application {

    BridgeHost? _bridge;

    protected override void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);

        // The bridge captures the synchronization context so operator calls stay on the dispatcher thread.
        // In bridge-only mode nothing else would install it, so make it explicit.
        if (SynchronizationContext.Current is null) {
            SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
        }

        var bridgeOnly = e.Args.Contains("--bridge-only", StringComparer.OrdinalIgnoreCase);
        var grpcPort = Port(e.Args, "--port") ?? 5222;

        // The window owns the operator root; it is created (and the operators attached) either way, because
        // that is the state the bridge exposes.
        var window = new MainWindow();
        _bridge = BridgeHost.Start(window, grpcPort, grpcPort + 1);
        window.ShowEndpoints(_bridge.GrpcUrl, _bridge.McpUrl, _bridge.OpenApiUrl);

        if (!bridgeOnly) {
            window.Show();
            return;
        }

        Console.WriteLine($"ASON_BRIDGE_READY grpc={_bridge.GrpcUrl} mcp={_bridge.McpUrl} app={typeof(App).Assembly.GetName().Name}");

        // Without a window the default shutdown mode ends the process as soon as the dispatcher idles. The
        // bridge is the application's reason to be alive in this mode, so shutdown becomes explicit.
        ShutdownMode = ShutdownMode.OnExplicitShutdown;
        Console.CancelKeyPress += (_, args) => {
            args.Cancel = true;
            Dispatcher.Invoke(Shutdown);
        };
        // Returning hands control back to the application's dispatcher loop, which keeps this thread - and the
        // synchronization context the bridge captured - pumping.
    }

    protected override void OnExit(ExitEventArgs e) {
        _bridge?.Dispose();
        base.OnExit(e);
    }

    static int? Port(string[] args, string name) {
        var index = Array.FindIndex(args, a => string.Equals(a, name, StringComparison.OrdinalIgnoreCase));
        if (index < 0 || index + 1 >= args.Length) return null;
        return int.TryParse(args[index + 1], out var port) ? port : null;
    }
}
