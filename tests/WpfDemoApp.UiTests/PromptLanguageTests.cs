using System.Globalization;
using WpfSampleApp.AI;

namespace WpfDemoApp.UiTests;

/// <summary>
/// The demo's language decision is pure string/culture work, so it is verified without starting the UI or
/// calling a model. The directive text itself belongs to the library and is covered by Ason.Tests.
/// </summary>
public class PromptLanguageTests {

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("en")]
    public void English_systems_do_not_opt_in_because_the_presets_are_already_English(string name) {
        var culture = new CultureInfo(name);

        Assert.True(PromptLanguage.IsEnglish(culture));
        Assert.Null(PromptLanguage.AnswerLanguage(culture));
        Assert.StartsWith("Replies in English", PromptLanguage.BuildNotice(culture));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    public void Non_English_systems_opt_in_with_their_culture_name(string name) {
        var culture = new CultureInfo(name);

        Assert.False(PromptLanguage.IsEnglish(culture));
        Assert.Equal(culture.Name, PromptLanguage.AnswerLanguage(culture));
    }

    [Fact]
    public void The_script_preset_keeps_its_format_placeholder_so_ASON_can_fill_the_api() {
        // Documents why neither the demo nor the language option touches ScriptInstructions: the preset is a
        // composite format string whose {0} is filled by AsonClient with the generated API plus the instance
        // declarations it discovers at runtime. The script agent also emits C# only, and its impossibility
        // sentence must keep the literal "Cannot" prefix that the retry logic string-matches.
        Assert.Contains("{0}", Ason.AgentPrompts.ScriptAgentTemplate);
        Assert.DoesNotContain("{0}", Ason.AgentPrompts.BuildScriptInstructions("void Do();"));
        Assert.Contains("void Do();", Ason.AgentPrompts.BuildScriptInstructions("void Do();"));
    }

    [Fact]
    public void The_notice_describes_the_language_of_the_current_ui_culture() {
        var culture = PromptLanguage.SystemUiCulture;
        var notice = PromptLanguage.BuildNotice(culture);

        Assert.False(string.IsNullOrWhiteSpace(notice));

        if (PromptLanguage.IsEnglish(culture)) {
            Assert.StartsWith("Replies in English", notice);
        }
        else {
            Assert.Contains(culture.NativeName, notice);
            Assert.Contains("system language", notice);
        }
    }
}
