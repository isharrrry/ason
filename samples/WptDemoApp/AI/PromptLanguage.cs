using System.Globalization;

namespace WpfSampleApp.AI;

/// <summary>
/// Builds the language part of the agent instructions used by the demo.
///
/// ASON ships its preset prompts as public constants (<see cref="Ason.AgentPrompts"/>) and
/// <see cref="Ason.AsonClientOptions.ReceptionInstructions"/> / <c>ExplainerInstructions</c> are plain text
/// that ASON forwards to the model unchanged. The demo therefore reads a preset <em>before</em> it creates
/// the client, prepends a language rule when the Windows display language is not English, and the assistant
/// answers in the language the user actually reads.
/// </summary>
public static class PromptLanguage {

    /// <summary>The language the demo localizes for: the UI language of the current user.</summary>
    public static CultureInfo SystemUiCulture => CultureInfo.CurrentUICulture;

    /// <summary>An English (or unknown) UI language needs no directive: the presets are written in English.</summary>
    public static bool IsEnglish(CultureInfo? culture = null) {
        var code = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        return string.IsNullOrEmpty(code) || code.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The text prepended to a preset prompt, or an empty string when the UI language is English.
    /// It only constrains the natural-language output; code, identifiers and data stay untouched.
    /// </summary>
    public static string BuildDirective(CultureInfo? culture = null) {
        culture ??= CultureInfo.CurrentUICulture;
        if (IsEnglish(culture)) return string.Empty;

        var language = string.IsNullOrWhiteSpace(culture.NativeName) ? culture.EnglishName : culture.NativeName;
        return $"""
            Language rule: always answer the user in {culture.EnglishName} / {language} ({culture.Name}).
            Write every user-facing sentence (summaries, explanations, questions and error notes) in that
            language - even when the user writes in another language, and even when earlier answers in this
            conversation were written in another language (then switch to {culture.Name} instead of copying
            their style). Keep code, API names, identifiers, numbers and data values exactly as they are.


            """;
    }

    /// <summary>Prepends the language directive to a preset prompt. No-op on an English system.</summary>
    public static string WithSystemLanguage(string preset, CultureInfo? culture = null)
        => BuildDirective(culture) + preset;

    /// <summary>A short note for the chat panel describing which language replies will use.</summary>
    public static string BuildNotice(CultureInfo? culture = null) {
        culture ??= CultureInfo.CurrentUICulture;
        var code = string.IsNullOrEmpty(culture.Name) ? "unknown locale" : culture.Name;
        return IsEnglish(culture)
            ? $"Replies in English ({code})"
            : $"Replies in {culture.NativeName} ({code}) - your system language";
    }
}
