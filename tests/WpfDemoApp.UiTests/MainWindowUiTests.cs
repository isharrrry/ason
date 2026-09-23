using FlaUI.Core.AutomationElements;
using FlaUI.Core.Tools;
using Xunit.Abstractions;

namespace WpfDemoApp.UiTests;

[Collection(WpfAppCollection.Name)]
public class MainWindowUiTests {

    const string NavigationListId = "NavigationList";
    const string ChatInputId = "ChatInput";
    const string ChatResponseId = "ChatResponseBox";
    const string SendButtonId = "SendButton";
    const string LanguageNoticeId = "LanguageNotice";
    const string ThemeNoticeId = "ThemeNotice";

    readonly WpfAppFixture _fixture;
    readonly ITestOutputHelper _output;

    public MainWindowUiTests(WpfAppFixture fixture, ITestOutputHelper output) {
        _fixture = fixture;
        _output = output;
    }

    Window Window => _fixture.MainWindow;

    [Fact]
    public void Main_window_is_shown_with_the_expected_title() {
        _output.WriteLine($"driving {AppUnderTest.ExePath}");
        Assert.Equal("MainWindow", Window.Title);
        Assert.NotNull(Find(NavigationListId).AsListBox());
    }

    [Fact]
    public void Navigation_switches_between_all_four_views() {
        NavigateTo("Employees");
        Assert.NotNull(Find("EmployeesGrid"));

        NavigateTo("Calendar");
        Assert.NotNull(Find("CalendarAppointments"));

        NavigateTo("Emails");
        Assert.NotNull(Find("EmailsList"));

        NavigateTo("Analytics");
        Assert.NotNull(Find("ChartsViewHint"));

        NavigateTo("Employees");
        Assert.NotNull(Find("EmployeesGrid"));
    }

    [Fact]
    public void Chat_panel_announces_the_reply_language_taken_from_the_system() {
        // Proves the running app read the Windows display language (not the test process only): the panel
        // shows the note built from CultureInfo.CurrentUICulture of the app, formatted with its native name.
        var expected = WpfSampleApp.AI.PromptLanguage.BuildNotice();

        var noticeText = Retry.WhileEmpty(
            () => Find(LanguageNoticeId).Name,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(250)).Result;

        _output.WriteLine($"language notice: {noticeText}");
        Assert.Equal(expected, noticeText);
    }

    [Fact]
    public void Chat_panel_reports_the_theme_the_build_renders_with() {
        // net9/net10 use the platform Fluent theme (ThemeMode), net6 brings its own (WPF-UI). The label is
        // set by the same code that applies the theme, so this fails if the net6 fallback is dropped and the
        // app silently goes back to the classic Aero2 look.
        var expected = AppUnderTest.TargetFramework.StartsWith("net6", StringComparison.Ordinal)
            ? "WPF-UI"
            : "platform";

        var themeText = Retry.WhileEmpty(
            () => Find(ThemeNoticeId).Name,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(250)).Result;

        _output.WriteLine($"theme notice: {themeText}");
        Assert.Contains(expected, themeText);
    }

    [MissingApiKeyFact]
    public void Chat_panel_explains_that_the_api_key_is_missing() {
        var response = Find(ChatResponseId).AsTextBox();

        var text = Retry.WhileEmpty(
            () => response.Text,
            TimeSpan.FromSeconds(20),
            TimeSpan.FromMilliseconds(250)).Result;

        _output.WriteLine($"chat panel says: {text}");
        Assert.Contains(AppUnderTest.ApiKeyVariable, text);
    }

    [LiveEndpointFact]
    public void Chat_answers_a_task_through_the_configured_endpoint() {
        // The sample shows prompt suggestions until the first reply arrives.
        var input = Find(ChatInputId).AsTextBox();
        input.Text = "How many employees are in the app? Use the Employees view.";

        Find(SendButtonId).AsButton().Invoke();

        var response = Find(ChatResponseId).AsTextBox();
        var reply = WaitForStableReply(response);

        _output.WriteLine("assistant reply: " + reply);

        Assert.False(string.IsNullOrWhiteSpace(reply), "the assistant produced no reply");

        // A non-empty reply is not enough: an auth/endpoint failure also paints text into the panel.
        foreach (var failureMarker in new[] { "invalid_api_key", "Unauthorized", "401", "Exception", "No API key configured" }) {
            Assert.False(
                reply!.Contains(failureMarker, StringComparison.OrdinalIgnoreCase),
                $"the reply looks like a configuration/endpoint failure (matched '{failureMarker}'): {reply}");
        }

        // The task asks for a count, so a digit proves the agent actually ran a script against the app.
        Assert.Matches(@"\d", reply!);
    }

    [LiveEndpointFact]
    public void Chat_answers_in_the_system_language_when_it_is_not_English() {
        // The demo prefixes the preset prompts with a language rule, so on a non-English Windows the sentence
        // the user reads must be written in that language. The question asks for prose on purpose: a bare
        // number (what the test above gets) would carry no language signal at all.
        Find(ChatInputId).AsTextBox().Text =
            "Introduce yourself in one friendly sentence and then tell me how many employees are in the app.";

        Find(SendButtonId).AsButton().Invoke();

        var response = Find(ChatResponseId).AsTextBox();
        var reply = WaitForStableReply(response);

        _output.WriteLine("assistant reply: " + reply);
        Assert.False(string.IsNullOrWhiteSpace(reply), "the assistant produced no reply");

        foreach (var failureMarker in new[] { "invalid_api_key", "Unauthorized", "401", "Exception", "No API key configured" }) {
            Assert.False(
                reply!.Contains(failureMarker, StringComparison.OrdinalIgnoreCase),
                $"the reply looks like a configuration/endpoint failure (matched '{failureMarker}'): {reply}");
        }

        var uiCulture = System.Globalization.CultureInfo.CurrentUICulture;
        if (uiCulture.TwoLetterISOLanguageName == "zh") {
            Assert.True(
                reply!.Any(c => c >= '\u4E00' && c <= '\u9FFF'),
                $"the system UI language is {uiCulture.Name}, so the reply should contain Chinese text: {reply}");
        }
    }

    [LiveEndpointFact]
    public void Chat_answers_an_api_query_with_the_markdown_listing() {
        // The demo exposes OperatorApiCatalog through an operator, so this proves the whole chain: the model
        // routes the question to GetApiListing, the script returns the Markdown, and the answer carries it.
        Find(ChatInputId).AsTextBox().Text = "List all available APIs and operations as a Markdown table.";

        Find(SendButtonId).AsButton().Invoke();

        var response = Find(ChatResponseId).AsTextBox();
        var reply = WaitForStableReply(response);

        _output.WriteLine("assistant reply: " + reply);

        Assert.False(string.IsNullOrWhiteSpace(reply), "the assistant produced no reply");
        foreach (var failureMarker in new[] { "invalid_api_key", "Unauthorized", "401", "Exception", "No API key configured" }) {
            Assert.False(
                reply!.Contains(failureMarker, StringComparison.OrdinalIgnoreCase),
                $"the reply looks like a configuration/endpoint failure (matched '{failureMarker}'): {reply}");
        }

        // A Markdown table plus a real operator name: prose alone, an endpoint failure or a truncated answer
        // would fail this. How much of the listing the model reproduces varies between runs (it may add a lead
        // sentence), so the exact rows are covered by the deterministic OperatorApiCatalogTests instead.
        Assert.Contains("|", reply!);
        Assert.Contains("---", reply!);
        Assert.True(
            new[] { "EmployeesViewOperator", "ChartsViewOperator", "LibDemoOperator" }.Any(name => reply!.Contains(name)),
            $"the reply should name at least one real operator: {reply}");
    }

    /// <summary>
    /// Reads a streamed reply once it stopped growing. Retry.WhileEmpty returns on the first chunk, which makes
    /// assertions race the stream: a long answer gets asserted while only its opening lines have arrived.
    /// </summary>
    string WaitForStableReply(TextBox response, TimeSpan? quietPeriod = null) {
        var quiet = quietPeriod ?? TimeSpan.FromSeconds(3);
        var deadline = DateTime.UtcNow.AddSeconds(180);
        var last = string.Empty;
        var lastChange = DateTime.UtcNow;

        while (DateTime.UtcNow < deadline) {
            var current = response.Text;
            if (!string.Equals(current, last, StringComparison.Ordinal)) {
                last = current;
                lastChange = DateTime.UtcNow;
            }
            else if (!string.IsNullOrEmpty(current) && DateTime.UtcNow - lastChange > quiet) {
                return current;
            }
            FlaUI.Core.Input.Wait.UntilInputIsProcessed();
            Thread.Sleep(250);
        }

        _output.WriteLine($"reply did not stabilize within the timeout; returning {last.Length} characters");
        return last;
    }

    void NavigateTo(string itemName) {
        var list = Find(NavigationListId).AsListBox();
        var item = list.Items.FirstOrDefault(i => string.Equals(i.Name, itemName, StringComparison.Ordinal))
            ?? throw new InvalidOperationException(
                $"Navigation item '{itemName}' not found. Available: {string.Join(", ", list.Items.Select(i => i.Name))}");

        var selectionItem = item.Patterns.SelectionItem.PatternOrDefault;
        if (selectionItem is not null) {
            selectionItem.Select();
        }
        else {
            item.Click();
        }

        FlaUI.Core.Input.Wait.UntilInputIsProcessed();
    }

    AutomationElement Find(string automationId) {
        var element = Window.FindFirstDescendant(cf => cf.ByAutomationId(automationId));
        if (element is not null) return element;

        var knownIds = Window.FindAllDescendants()
            .Select(e => e.Properties.AutomationId.ValueOrDefault)
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Distinct()
            .OrderBy(id => id, StringComparer.Ordinal);

        throw new InvalidOperationException(
            $"No element with AutomationId '{automationId}' under the main window. Known AutomationIds: {string.Join(", ", knownIds)}");
    }
}
