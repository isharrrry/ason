using System.Globalization;
using WpfSampleApp.AI;

namespace WpfDemoApp.UiTests;

/// <summary>
/// The language directive is pure string work, so it is verified without starting the UI or calling a model.
/// </summary>
public class PromptLanguageTests {

    [Theory]
    [InlineData("en-US")]
    [InlineData("en-GB")]
    [InlineData("en")]
    public void English_systems_get_no_directive_because_the_presets_are_already_English(string name) {
        var culture = new CultureInfo(name);

        Assert.True(PromptLanguage.IsEnglish(culture));
        Assert.Equal(string.Empty, PromptLanguage.BuildDirective(culture));
        Assert.Equal(Ason.AgentPrompts.ExplainerAgentTemplate,
            PromptLanguage.WithSystemLanguage(Ason.AgentPrompts.ExplainerAgentTemplate, culture));
    }

    [Theory]
    [InlineData("zh-CN")]
    [InlineData("es-ES")]
    [InlineData("de-DE")]
    public void Non_English_systems_get_a_directive_naming_their_language(string name) {
        var culture = new CultureInfo(name);

        Assert.False(PromptLanguage.IsEnglish(culture));

        var directive = PromptLanguage.BuildDirective(culture);
        Assert.Contains(culture.EnglishName, directive);
        Assert.Contains(culture.Name, directive);
        Assert.Contains("Language rule", directive);
    }

    [Fact]
    public void The_directive_is_prepended_to_the_preset_without_losing_it() {
        var culture = new CultureInfo("zh-CN");
        var composed = PromptLanguage.WithSystemLanguage(Ason.AgentPrompts.ReceptionAgentTemplate, culture);

        Assert.StartsWith(PromptLanguage.BuildDirective(culture) + "You are an AI assistant.", composed);
        Assert.EndsWith("No code, no markup besides <task> tags", composed.TrimEnd());
    }

    [Fact]
    public void The_script_preset_keeps_its_format_placeholder_so_ASON_can_fill_the_api() {
        // Documents why the sample leaves ScriptInstructions alone: the preset is a composite format
        // string whose {0} is filled by AsonClient with the generated API plus the instance declarations.
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
