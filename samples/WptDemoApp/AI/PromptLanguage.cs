using System.Globalization;

namespace WpfSampleApp.AI;

/// <summary>
/// Reads the Windows display language for the demo and decides whether to opt into localized answers.
///
/// The prompt work itself lives in the library: <see cref="Ason.AsonClientOptions.AnswerLanguage"/> makes
/// <see cref="Ason.AsonClient"/> prepend the rule built by <see cref="Ason.AgentPrompts.BuildLanguageDirective"/>
/// to the Reception and Explainer prompts. Keeping the directive in one place is the point - a copy here would
/// drift from the one the library applies. What stays in the demo is the UI-side part: whether to opt in at all
/// and what to show in the chat panel.
/// </summary>
public static class PromptLanguage {

    /// <summary>The language the demo localizes for: the UI language of the current user.</summary>
    public static CultureInfo SystemUiCulture => CultureInfo.CurrentUICulture;

    /// <summary>An English (or unknown) UI language does not opt in: the presets are already written in English.</summary>
    public static bool IsEnglish(CultureInfo? culture = null) {
        var code = (culture ?? CultureInfo.CurrentUICulture).TwoLetterISOLanguageName;
        return string.IsNullOrEmpty(code) || code.Equals("en", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// The culture name to assign to <see cref="Ason.AsonClientOptions.AnswerLanguage"/>, or
    /// <see langword="null"/> on an English system, which leaves every preset untouched.
    /// </summary>
    public static string? AnswerLanguage(CultureInfo? culture = null) {
        culture ??= CultureInfo.CurrentUICulture;
        return IsEnglish(culture) ? null : culture.Name;
    }

    /// <summary>A short note for the chat panel describing which language replies will use.</summary>
    public static string BuildNotice(CultureInfo? culture = null) {
        culture ??= CultureInfo.CurrentUICulture;
        var code = string.IsNullOrEmpty(culture.Name) ? "unknown locale" : culture.Name;
        return IsEnglish(culture)
            ? $"Replies in English ({code})"
            : $"Replies in {culture.NativeName} ({code}) - your system language";
    }
}
