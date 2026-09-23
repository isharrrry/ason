using Ason;

namespace WpfAppOnlyDemo;

/// <summary>An employee row, declared as a model so the API listing describes its fields.</summary>
[AsonModel("One employee row shown in the window")]
public sealed class Employee {
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Department { get; set; } = string.Empty;
    public string Hired { get; set; } = string.Empty;
}

/// <summary>What the application can report about itself to an agent.</summary>
[AsonModel("Application diagnostics observed during an operator call")]
public sealed class AppDiagnostics {
    public bool OnUiThread { get; set; }
    public int ThreadId { get; set; }
    public int EmployeeCount { get; set; }
}

/// <summary>
/// A view-style operator: it is attached to the window, so every call mutates the collection the ListView is
/// bound to. Because it is registered as an operator instance, its handle is what the manifest reports and
/// what the single-function interface addresses.
/// </summary>
[AsonOperator("Manages the employee list shown in the application window")]
public sealed class EmployeesOperator : OperatorBase<MainWindow> {

    [AsonMethod("Returns every employee currently in the list")]
    public List<Employee> GetEmployees() =>
        AttachedObject?.Employees.Select(Clone).ToList() ?? new List<Employee>();

    [AsonMethod("Renames one employee by id and returns the updated row")]
    public Employee? Rename(int id, string name) {
        var employee = AttachedObject?.Employees.FirstOrDefault(e => e.Id == id);
        if (employee is null) return null;
        employee.Name = name;
        return Clone(employee);
    }

    [AsonMethod("Adds an employee and returns the new id")]
    public int AddEmployee(string name, string department, string hired) {
        var employees = AttachedObject?.Employees;
        if (employees is null) return -1;
        var id = employees.Count == 0 ? 1 : employees.Max(e => e.Id) + 1;
        employees.Add(new Employee { Id = id, Name = name, Department = department, Hired = hired });
        return id;
    }

    [AsonMethod("Reports whether this call ran on the UI thread, and how many employees there are")]
    public AppDiagnostics GetDiagnostics() => new() {
        OnUiThread = AttachedObject?.Dispatcher.CheckAccess() ?? false,
        ThreadId = Environment.CurrentManagedThreadId,
        EmployeeCount = AttachedObject?.Employees.Count ?? 0
    };

    static Employee Clone(Employee employee) => new() {
        Id = employee.Id,
        Name = employee.Name,
        Department = employee.Department,
        Hired = employee.Hired
    };
}

/// <summary>
/// A static operator module: no instance, no handle - an agent addresses it by type name.
/// </summary>
[AsonOperator("Stateless application information")]
public static class AppInfoOperator {

    [AsonMethod("Returns the application name and the runtime it runs on")]
    public static string GetAppInfo() =>
        $"WpfAppOnlyDemo on {System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription}";

    [AsonMethod("Adds two integers")]
    public static int Add(int left, int right) => left + right;
}

/// <summary>
/// A marker-only operator: marked, but not an <see cref="OperatorBase"/>, so the host materialises one
/// instance and the bridge addresses it by type name.
/// </summary>
[AsonOperator("Reports the application composes without touching the UI")]
public sealed class ReportOperator {

    [AsonMethod("Builds a short status report")]
    public string BuildSummary() =>
        $"machine {Environment.MachineName} · utc {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss} · operators are resolved inside the application process";
}
