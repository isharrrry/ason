using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using Ason;
using Ason.Bridge;
using Ason.Client.Execution;
using AsonRunner;
using Microsoft.SemanticKernel.ChatCompletion;
using WpfAgentDemo.Bridge;

namespace WpfAgentDemo;

/// <summary>
/// The agent side of a split deployment. It holds the model client and the orchestration, and it owns no
/// operators: the operator API comes from the application over the bridge, and every script it produces is
/// sent there to run.
/// </summary>
public partial class MainWindow : Window {

    AgentBridge? _bridge;
    AsonClient? _agent;
    readonly StringBuilder _transcript = new();

    public MainWindow() {
        InitializeComponent();
        EndpointBox.Text = "http://localhost:5222";

        // Visible proof of the separation: this assembly declares no [AsonOperator] at all.
        var owned = AsonBridgeOperators.MaterializeMarkerOnly(typeof(MainWindow).Assembly).Count;
        var declared = typeof(MainWindow).Assembly.GetTypes().Count(t => t.GetCustomAttribute<AsonOperatorAttribute>() is not null);
        SeparationText.Text = $"This process declares {declared} [AsonOperator] types and {owned} operator instances. The application owns the operators; this side only owns the model.";
    }

    async void OnConnect(object sender, RoutedEventArgs e) {
        var kind = TransportBox.SelectedIndex == 1 ? AgentTransportKind.Mcp : AgentTransportKind.Grpc;
        ConnectButton.IsEnabled = false;
        StatusText.Text = "connecting…";
        try {
            if (_bridge is not null) await _bridge.DisposeAsync();
            _bridge = await AgentBridge.ConnectAsync(EndpointBox.Text, kind);
            ShowManifest(_bridge.Manifest!);
            _agent = null; // rebuilt on the next message, against this application
            RefreshButton.IsEnabled = true;
            SendButton.IsEnabled = true;
            StatusText.Text = $"connected over {kind} · execution {_bridge.Manifest!.Execution}";
            AppendCall($"connected to '{_bridge.Manifest.AppName}' over {kind} ({_bridge.Endpoint})");
        }
        catch (Exception ex) {
            StatusText.Text = "connection failed";
            AppendCall($"connect failed: {ex.Message}");
        }
        finally {
            ConnectButton.IsEnabled = true;
        }
    }

    async void OnRefresh(object sender, RoutedEventArgs e) {
        if (_bridge is null) return;
        try {
            ShowManifest(await _bridge.RefreshAsync());
            StatusText.Text = "API refreshed";
        }
        catch (Exception ex) {
            AppendCall($"refresh failed: {ex.Message}");
        }
    }

    async void OnSend(object sender, RoutedEventArgs e) => await SendAsync();

    async void OnInputKeyDown(object sender, KeyEventArgs e) {
        if (e.Key == Key.Enter && SendButton.IsEnabled) await SendAsync();
    }

    async Task SendAsync() {
        if (_bridge is null) return;
        var text = InputBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(text)) return;

        SendButton.IsEnabled = false;
        InputBox.Text = string.Empty;
        AppendTranscript($"you: {text}");

        try {
            _agent ??= BuildAgent(_bridge);
            var reply = new StringBuilder();
            await foreach (var chunk in _agent.SendStreamingAsync(text)) reply.Append(chunk);
            AppendTranscript($"agent: {reply}");
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("MY_OPEN_AI_KEY")) {
            AppendTranscript("agent: no model configured. Set MY_OPEN_AI_KEY (and MY_OPEN_AI_BASE_URL / MY_OPEN_AI_MODEL) and send again.");
        }
        catch (Exception ex) {
            AppendTranscript($"agent: {ex.Message}");
        }
        finally {
            SendButton.IsEnabled = true;
        }
    }

    /// <summary>
    /// The whole integration, in three lines: an ASON client whose operator library comes from the
    /// application's manifest, whose transport carries the runner protocol to the application, and whose root
    /// operator is an empty placeholder because this process owns nothing to call.
    /// </summary>
    AsonClient BuildAgent(AgentBridge bridge) {
        var chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
        var options = new AsonClientOptions {
            // The isolation belongs to the application: it decides whether to evaluate in-process, in an
            // external executor, in a container or on a remote runner.
            ExecutionMode = ExecutionMode.ExternalProcess,
            TransportFactory = bridge.CreateTransport,
            ForbiddenScriptKeywords = new[] { "System.IO", "System.Reflection", "Process.Start", "DllImport" },
            MaxFixAttempts = 2
        };
        var agent = new AsonClient(chatService, new RootOperator(new object()), bridge.CreateOperatorsLibrary(), options);
        agent.Log += (_, log) => Dispatcher.BeginInvoke(() => AppendCall($"[{log.Level}] {log.Message}"));
        agent.MethodInvoking += (_, call) => Dispatcher.BeginInvoke(() => AppendCall($"call: {call.Target}.{call.Method}"));
        AppendCall($"agent built from the application manifest: {bridge.Manifest!.Api.Operators.Count} operators, {bridge.Manifest.Api.MethodCount} methods");
        return agent;
    }

    void ShowManifest(AsonBridgeManifest manifest) {
        MarkdownBox.Text = manifest.Markdown;
        ManifestBox.Text = JsonSerializer.Serialize(manifest, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        InstancesBox.Text = manifest.Instances.Count == 0
            ? "(no live operator instances right now)"
            : string.Join(Environment.NewLine, manifest.Instances.Select(i => $"{i.Handle}  ({i.TypeName}, initialized={i.Initialized})"));
    }

    void AppendTranscript(string line) {
        _transcript.AppendLine(line);
        ChatBox.Text = _transcript.ToString();
        ChatBox.ScrollToEnd();
    }

    void AppendCall(string line) {
        CallsList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (CallsList.Items.Count > 200) CallsList.Items.RemoveAt(CallsList.Items.Count - 1);
    }
}
