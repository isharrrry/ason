using Ason;
using WpfSampleApp.Model;
using WpfSampleApp.ViewModels;
using WpfSampleApp.Views;

namespace WpfSampleApp.AI;

[AsonOperator(description: "Manages main navigation")]
public class MainAppOperator : RootOperator<MainViewModel> {
    public MainAppOperator(MainViewModel associatedObject) : base(associatedObject) { }

    [AsonMethod("Navigates to Employees view")]
    public async Task<EmployeesViewOperator> GetEmployeesViewOperatorAsync() {
        return await GetViewOperator<EmployeesViewOperator>(Navigate<EmployeesViewModel>);
    }

    [AsonMethod("Navigates to Calendar view")]
    public async Task<CalendarViewOperator> GetCalendarViewOperatorAsync() {
        return await GetViewOperator<CalendarViewOperator>(Navigate<CalendarViewModel>);
    }

    [AsonMethod("Navigates to Emails view")]
    public async Task<EmailsViewOperator> GetEmailsViewOperatorAsync() {
        return await GetViewOperator<EmailsViewOperator>(Navigate<EmailsViewModel>);
    }

    [AsonMethod("Naivgate to Charts view. CALL THIS METHOD WHEN USER TASK IS TO CREATE/DRAW CHART")]
    public async Task<ChartsViewOperator> GetChartsViewOperatorAsync() {
        return await GetViewOperator<ChartsViewOperator>(Navigate<ChartsViewModel>);
    }

    [AsonMethod("Lists every available operator API as a Markdown table. CALL THIS METHOD WHEN THE USER ASKS WHICH APIs, OPERATIONS OR COMMANDS ARE AVAILABLE.")]
    public string GetApiListing() => ApiListing;

    // Built once: Describe walks the same assemblies the client was configured with, and the walk is pure
    // reflection. Cached because every chat turn that asks for the listing would otherwise redo it.
    static string? _apiListing;
    static string ApiListing => _apiListing ??= OperatorApiCatalog
        .Describe(typeof(MainAppOperator).Assembly, typeof(LibDemo.LibDemoOperator).Assembly)
        .ToMarkdown();

    public void Navigate<TViewModelType>() {
        AttachedObject.CurrentNavigationItem = AttachedObject.NavigatoinItems.FirstOrDefault(i => i.ViewModel.GetType() == typeof(TViewModelType));
    }
}

[AsonOperator(description: "Manages Employees view")]
public class EmployeesViewOperator : OperatorBase<EmployeesViewModel> {

    [AsonMethod]
    public void Test() => Console.WriteLine("CustomersViewOperator.Test called (host)");

    [AsonMethod]
    public void ShowColumnChooser() => Console.WriteLine("CustomersViewOperator.ShowColumnChooser called (host)");

    [AsonMethod]
    public List<Employee>? GetEmployees() {
        return AttachedObject?.Employees.ToList();
    }

    [AsonMethod("Opens Employee editing tab to update employee data")]
    public async Task<EmployeeEditViewOperator> GetEmployeeEditingOperatorAsync(int employeeId) {
        return await GetViewOperator<EmployeeEditViewOperator>(() => AttachedObject?.EditEmployee(employeeId), employeeId.ToString());
    }
}

[AsonOperator("Manages employee update/editing operations")]
public class EmployeeEditViewOperator : OperatorBase<EmployeeEditViewModel> {

    [AsonMethod]
    public void UpdateEmployee(Employee updatedEmployee) {
        if (AttachedObject != null)
            AttachedObject.Editable = updatedEmployee;
    }
}

[AsonOperator("Allows to create/draw charts")]
public class ChartsViewOperator : OperatorBase<ChartsView> {

    [AsonMethod("Initializes bar chart and assigns data displayed in chart")]
    public void CreateBarChart(BarValue[] barValues, string xAxisCaption, string yAxisCaption) {
        AttachedObject?.AddBarChart(barValues, xAxisCaption, yAxisCaption);
    }
}

public class CalendarViewOperator : OperatorBase<CalendarViewModel> {

    [AsonMethod]
    public void AddAppointment(Appointment appt) {
        AttachedObject?.AddAppointmentAsync(appt);
    }
}

public class EmailsViewOperator : OperatorBase<EmailsViewModel> {

    [AsonMethod("Gets a list of emails")]
    public List<MailItem>? GetEmails() {
        return AttachedObject?.Emails.ToList();
    }
}