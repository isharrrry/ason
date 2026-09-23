using System.Collections.ObjectModel;
using System.Windows;
using Ason;

namespace WpfAppOnlyDemo;

public partial class MainWindow : Window {

    public MainWindow() {
        InitializeComponent();
        Operator = new RootOperator(this);
        // Attaching the operator registers its handle and marks it as attached, exactly like a view operator
        // in a real application.
        Operator.AttachChildOperator<EmployeesOperator>(this);
        EmployeesList.ItemsSource = Employees;
        ActivityList.ItemsSource = Activity;
    }

    /// <summary>The operator root: its instance directory is what the bridge exposes to an agent.</summary>
    public RootOperator Operator { get; }

    public ObservableCollection<Employee> Employees { get; } = new(new[] {
        new Employee { Id = 1, Name = "Ada Lovelace", Department = "Engineering", Hired = "2021-03-01" },
        new Employee { Id = 2, Name = "Grace Hopper", Department = "Engineering", Hired = "2020-07-15" },
        new Employee { Id = 3, Name = "Alan Turing", Department = "Research", Hired = "2019-11-20" }
    });

    public ObservableCollection<string> Activity { get; } = new();

    string _endpoints = string.Empty;

    /// <summary>Shows the addresses an agent should connect to.</summary>
    public void ShowEndpoints(string grpcUrl, string mcpUrl, string openApiUrl) {
        GrpcUrlText.Text = grpcUrl;
        McpUrlText.Text = mcpUrl;
        _endpoints = $"{grpcUrl} (gRPC)  |  {mcpUrl} (MCP)  |  {openApiUrl} (HTTP/OpenAPI)";
        AppendActivity($"bridge started: {_endpoints}");
    }

    void OnCopyEndpoints(object sender, RoutedEventArgs e) {
        try { Clipboard.SetText(_endpoints); } catch { /* the clipboard can be busy; the text is on screen anyway */ }
    }

    /// <summary>
    /// Called from the bridge for every log line. Execution runs on Kestrel threads, so the UI collection is
    /// only ever touched through the dispatcher.
    /// </summary>
    public void AppendActivity(string line) {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.BeginInvoke(() => AppendActivity(line));
            return;
        }
        Activity.Insert(0, $"{DateTime.Now:HH:mm:ss}  {line}");
        while (Activity.Count > 200) Activity.RemoveAt(Activity.Count - 1);
    }
}
