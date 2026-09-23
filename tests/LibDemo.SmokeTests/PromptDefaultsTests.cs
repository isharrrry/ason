using Ason;

namespace LibDemoSmokeTests;

/// <summary>
/// The compiler is part of the assertion here: this project has no <c>InternalsVisibleTo</c> access to
/// Ason, so every reference below only compiles while the built-in prompts remain public.
/// </summary>
public class PromptDefaultsTests {

    [Fact]
    public void Built_in_prompts_are_publicly_readable() {
        Assert.False(string.IsNullOrWhiteSpace(AgentPrompts.ScriptAgentTemplate));
        Assert.False(string.IsNullOrWhiteSpace(AgentPrompts.ReceptionAgentTemplate));
        Assert.False(string.IsNullOrWhiteSpace(AgentPrompts.ExplainerAgentTemplate));
        Assert.False(string.IsNullOrWhiteSpace(AgentPrompts.TextToDataAgentTemplate));
    }

    [Fact]
    public void Options_default_to_null_so_the_presets_apply() {
        var options = new AsonClientOptions();

        Assert.Null(options.ScriptInstructions);
        Assert.Null(options.ReceptionInstructions);
        Assert.Null(options.ExplainerInstructions);
    }

    [Fact]
    public void BuildScriptInstructions_fills_the_api_placeholder() {
        var text = AgentPrompts.BuildScriptInstructions("MyOperator.DoSomething();");

        Assert.Contains("MyOperator.DoSomething();", text);
        Assert.DoesNotContain("{0}", text);
    }

    [Fact]
    public void Presets_can_be_extended_without_losing_the_original_wording() {
        var custom = AgentPrompts.ReceptionAgentTemplate + "\nAlways answer in English.";

        Assert.StartsWith(AgentPrompts.ReceptionAgentTemplate, custom);
        Assert.EndsWith("Always answer in English.", custom);
    }
}
