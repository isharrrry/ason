using System;
using System.Threading.Tasks;
using Ason.Tests.Infrastructure;

namespace Ason.Tests.Client;

/// <summary>
/// Covers <see cref="AsonClientOptions.AnswerLanguage"/>, the opt-in that pins the language of the text the
/// user reads. Two levels are asserted on purpose: the directive text itself, and the instructions the agents
/// really send (captured by the stub), because a rule that is built correctly but never reaches the model
/// would still leave answers in the wrong language.
/// </summary>
public class AnswerLanguageTests {

    const string ReceptionPresetMarker = "You are an AI assistant";
    const string ExplainerPresetMarker = "You explain results";
    const string RuleMarker = "Language rule";
    const string ScriptReply = "return 5;";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void No_language_configured_builds_no_directive(string? language) {
        Assert.Equal(string.Empty, AgentPrompts.BuildLanguageDirective(language));
    }

    [Theory]
    [InlineData("zh-CN", "Chinese", "zh-CN")]
    [InlineData("es", "Spanish", "es")]
    [InlineData("de-DE", "German", "de-DE")]
    [InlineData("en-US", "English", "en-US")]
    public void A_configured_language_names_itself_and_the_rule(string language, string englishName, string code) {
        var directive = AgentPrompts.BuildLanguageDirective(language);

        Assert.Contains(RuleMarker, directive);
        Assert.Contains(englishName, directive);
        Assert.Contains($"({code})", directive);
        // The rule has to survive a multi-turn conversation, not only an isolated request. Assert fragments
        // that fit inside one line: the prompt is wrapped, so a longer phrase would span a newline.
        Assert.Contains("even when the user writes in another language", directive);
        Assert.Contains("earlier answers in this", directive);
        Assert.Contains("instead of copying", directive);
    }

    [Fact]
    public void A_well_formed_but_unregistered_language_is_passed_through() {
        // The runtime accepts any well-formed BCP-47 tag, so the library cannot tell "zz" from a real locale
        // (legitimate tags such as en-001 resolve the same way). What it must do is stay honest: the tag
        // reaches the prompt as-is, so the failure mode of a typo is visible in the directive instead of a
        // silently wrong language.
        //
        // The *display name* of an unregistered tag is produced by the runtime's globalization and differs by
        // implementation: .NET on Windows reports "zz", NLS reports "Unknown Locale (zz)" and ICU (Linux)
        // reports "Unknown Language (zz)". Asserting on it therefore makes the suite pass or fail by platform,
        // which is why only the tag and the rule body are asserted here - the sentence around them is not a
        // contract the library can keep.
        var directive = AgentPrompts.BuildLanguageDirective("zz");

        Assert.StartsWith("Language rule", directive);
        Assert.Contains("(zz)", directive);                 // the tag reaches the prompt as written
        Assert.Contains("instead of copying", directive);   // the rule body is intact, not just the first line
        Assert.DoesNotContain("zz / zz", directive);        // no duplicated native name when the runtime has none
    }

    [Fact]
    public void English_is_not_special_cased_so_a_host_can_pin_it_explicitly() {
        // Setting the option means "answer in this language", English included: a host that wants English
        // answers even when the user writes in another language can opt in that way. Leaving the option null
        // is what keeps the presets untouched.
        var english = AgentPrompts.BuildLanguageDirective("en-US");

        Assert.NotEqual(string.Empty, english);
        Assert.Contains("English", english);
    }

    [Fact]
    public void A_malformed_language_is_rejected_with_a_readable_message() {
        var ex = Assert.Throws<ArgumentException>(() => AgentPrompts.BuildLanguageDirective("en/US"));

        Assert.Contains("en/US", ex.Message);
        Assert.Contains("BCP-47", ex.Message);
    }

    [Fact]
    public void A_malformed_language_fails_at_client_construction_not_later() {
        var chat = TestChatServices.CreateReceptionService("script");
        var options = new AsonClientOptions { AnswerLanguage = "12345" };

        var ex = Assert.Throws<ArgumentException>(() => TestHarness.CreateBasicClient(chat, options));

        Assert.Contains("12345", ex.Message);
    }

    [Fact]
    public void Applying_a_language_keeps_the_preset_text_intact() {
        var directive = AgentPrompts.BuildLanguageDirective("zh-CN");
        var composed = AgentPrompts.WithAnswerLanguage(AgentPrompts.ExplainerAgentTemplate, "zh-CN");

        Assert.StartsWith(RuleMarker, composed);
        Assert.Contains(ExplainerPresetMarker, composed);
        Assert.Equal(AgentPrompts.ExplainerAgentTemplate, composed.Substring(directive.Length));
    }

    [Fact]
    public async Task Without_the_option_the_reception_agent_receives_the_preset_unchanged() {
        var chat = TestChatServices.CreateReceptionService("script");
        chat.Enqueue(ScriptReply);   // the script agent dequeues this after reception routed to a script

        await TestHarness.CreateBasicClient(chat).SendAsync("count the employees");

        Assert.Contains(ReceptionPresetMarker, chat.ReceivedText);
        Assert.DoesNotContain(RuleMarker, chat.ReceivedText);
    }

    [Fact]
    public async Task With_the_option_both_user_facing_agents_receive_the_language_rule() {
        var reception = TestChatServices.CreateReceptionService("script");
        var script = TestChatServices.CreateScriptService(ScriptReply);
        var explainer = TestChatServices.CreateExplainerService(echoUserInput: true);
        var options = new AsonClientOptions {
            AnswerLanguage = "zh-CN"
        };

        await TestHarness.CreateBasicClient(script, options, reception, explainer).SendAsync("how many employees are there?");

        Assert.Contains(RuleMarker, reception.ReceivedText);
        Assert.Contains(ReceptionPresetMarker, reception.ReceivedText);

        // Reception routed to a script (see the stub reply) and the script succeeded, so the explainer ran too.
        Assert.Contains(RuleMarker, explainer.ReceivedText);
        Assert.Contains(ExplainerPresetMarker, explainer.ReceivedText);

        // The script agent is deliberately excluded: it emits C# and must keep the literal "Cannot" sentinel.
        Assert.DoesNotContain(RuleMarker, script.ReceivedText);
    }

    [Fact]
    public async Task A_host_instruction_override_still_gets_the_language_rule() {
        var reception = TestChatServices.CreateReceptionService("script");
        var script = TestChatServices.CreateScriptService(ScriptReply);
        var options = new AsonClientOptions {
            AnswerLanguage = "zh-CN",
            ReceptionInstructions = "Custom reception prompt. Always route to a script."
        };

        await TestHarness.CreateBasicClient(script, options, reception).SendAsync("count the employees");

        Assert.Contains(RuleMarker, reception.ReceivedText);
        Assert.Contains("Custom reception prompt", reception.ReceivedText);
        Assert.DoesNotContain(ReceptionPresetMarker, reception.ReceivedText);
    }
}
