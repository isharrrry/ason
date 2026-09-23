using System.Globalization;

namespace Ason;

/// <summary>
/// The built-in prompts used by the internal ASON agents. They are public so an application can read
/// (and start from) the presets that ASON applies when the matching <see cref="AsonClientOptions"/>
/// property is left <see langword="null"/>, for example:
/// <code>
/// AsonClientOptions options = new() {
///     // keep everything from the preset, but append a house rule
///     ReceptionInstructions = AgentPrompts.ReceptionAgentTemplate + "\nAlways answer in English.",
///     // or replace the API block inside the script prompt
///     ScriptInstructions = AgentPrompts.BuildScriptInstructions(myApiText),
/// };
/// </code>
/// </summary>
public static class AgentPrompts
{
    /// <summary>
    /// Prompt for the Script Agent. This is a composite format string: <c>{0}</c> is replaced with the
    /// generated operator API (see <see cref="BuildScriptInstructions"/>).
    /// </summary>
    public const string ScriptAgentTemplate =
"""
        You are a C# Roslyn script generator.
        Output only valid top-level C# statements (no classes, no methods, no explanations, no comments) unless the task cannot be executed.

        Strict rules:
        1. Use only the API inside <api> … </api>.
        2. If asked to return data, the script must end with a single `return` statement of a simple value from model objects.
        3. Methods must only be called on the class or object where they are defined:
            - Do not call a method from another class.
            - Never attach a method call to a different operator class, even if names look similar.
            - Example: If `SomeOperator.SampleMethod` exists, you may only call `SampleMethod` only from `SomeOperator` instances.
        4. Absolutely forbidden: creating operator instances with `new`:
           - Never use constructors like `new SomeViewOperator()`.
           - You must call existing API methods instead.
           - Treat any attempt to use `new` on an operator class as a compilation error.
        5. If the task cannot be completed based on the available API and standard C# functions, output a single plain English sentence starting with the word "Cannot" explaining briefly (no code, no XML tags). Do NOT output any code in that case.
        6. It's prohibited to create infinite loops.

        <api>
        {0}
        </api>
        """;

    /// <summary>Prompt for the Reception Agent. Used when <see cref="AsonClientOptions.ReceptionInstructions"/> is null.</summary>
    public const string ReceptionAgentTemplate =
        """
        You are an AI assistant. You can see the full prior conversation. The user may refine the request over multiple messages.

        ## Output format

        Respond only in this EXACT format:
             script
             <task>
             <single concise description of the consolidated user task capturing ALL relevant details from the conversation>
             </task>

        ## Rules

        Output the word **script** exactly as shown (not <script> and not “Task completed”).
        Produce nothing else before or after the required format.

        Inside <task>:
        - Restate the user’s request fully, capturing all parameters (names, numbers, strings, time ranges, filters, raw fragments—even malformed input such as line breaks or extra spaces)
        - No invented data
        - Do not alter or normalize data beyond minimal clarity; preserve original fragments verbatim while optionally clarifying (e.g., include the original malformed domain string)
        - Single concise actionable description; no extra commentary
        - No code, no markup besides <task> tags
        """;

    /// <summary>Prompt for the Explainer Agent. Used when <see cref="AsonClientOptions.ExplainerInstructions"/> is null.</summary>
    public const string ExplainerAgentTemplate =
        """
        You explain results of executed tasks back to the user.
        Input will include:
        <original>the original user task</original>
        <executed>the actual executed (possibly modified) task</executed>
        <result>the raw JSON/text result returned by the task (may be empty)</result>

        Guidelines:
        - If <original> and <executed> differ, first clearly state that only a partial version was executed and why (briefly, without mentioning missing APIs explicitly; you may say only available internal data was used, etc.).
        - If <result> is empty or null, say the task executed but returned no data.
        - Otherwise summarize the outcome in 1–3 short sentences focusing on the most important parts.
        - If the result is a plain user-facing string and tasks are identical, return it verbatim without quotes.
        - Do NOT mention scripts, code internals, or agents.
        """;


    /// <summary>Prompt for the Extractor Agent, which turns unstructured text into structured JSON.</summary>
    public const string TextToDataAgentTemplate =
        """
        You convert plain text into structured JSON that strictly matches a provided JSON format.

        Input:
        - <format> contains either:
          1) A JSON Schema (Draft 2020-12 style), or
          2) A JSON EXAMPLE that defines the exact shape and keys (arrays may provide one item as the item template).
        - <text> contains the source text to extract from.

        Strict output requirements:
        - Return ONLY a single JSON value (no prose, no code fences, no extra text).
        - If <format> is a JSON EXAMPLE:
          - Return exactly the same shape and keys as the example (no extra properties).
          - For arrays, use the single provided example element as the item template and repeat it per extracted items.
          - If a value cannot be found, use null (or an empty array for arrays).
          - Preserve types: numbers as numbers, booleans as booleans, strings as strings, nulls as nulls.
        - If <format> is a JSON SCHEMA:
          - Produce an instance that satisfies the schema (respect required, types, enums if present).
          - Do not include additionalProperties unless the schema allows it.
        - The JSON must be syntactically valid.

        Formatting:
        - Output must be minified (no pretty printing, no trailing commas).
        """;


    /// <summary>
    /// Builds the ready-to-use Script Agent prompt by substituting the operator API into
    /// <see cref="ScriptAgentTemplate"/>. Use this when you want to override
    /// <see cref="AsonClientOptions.ScriptInstructions"/> without losing the preset wording.
    /// </summary>
    /// <param name="apiSignatures">The generated operator API text, normally produced by <c>OperatorBuilder</c>.</param>
    public static string BuildScriptInstructions(string? apiSignatures)
        => string.Format(ScriptAgentTemplate, apiSignatures ?? string.Empty);

    /// <summary>
    /// Builds the rule that pins the language of the text the user reads, for a BCP-47 culture name such as
    /// <c>zh-CN</c>, <c>es</c> or <c>de-DE</c>. Returns an empty string when <paramref name="answerLanguage"/>
    /// is null or blank, so leaving <see cref="AsonClientOptions.AnswerLanguage"/> unset changes nothing.
    ///
    /// The rule also tells the model not to copy the language of earlier turns: conversation history pulls a
    /// model back into the previous language, which is the difference between a single isolated turn and a
    /// real multi-turn chat.
    /// </summary>
    /// <param name="answerLanguage">BCP-47 culture name, or null for "no rule".</param>
    /// <remarks>
    /// A malformed name (one the runtime rejects, such as <c>en/US</c>) throws. A well-formed but unregistered
    /// tag (such as <c>zz</c>) is accepted by the runtime and passed through, so the directive then names that
    /// tag - the visible symptom of a typo. It is deliberately not second-guessed with a hand-rolled registry
    /// check: legitimate tags such as <c>en-001</c> resolve like unregistered ones, and the display name of an
    /// unregistered tag differs between runtimes.
    /// </remarks>
    /// <exception cref="ArgumentException">The name is not a culture name the current runtime accepts.</exception>
    public static string BuildLanguageDirective(string? answerLanguage) {
        if (string.IsNullOrWhiteSpace(answerLanguage)) return string.Empty;

        CultureInfo culture;
        try {
            culture = CultureInfo.GetCultureInfo(answerLanguage.Trim());
        }
        catch (CultureNotFoundException ex) {
            throw new ArgumentException(
                $"Answer language '{answerLanguage}' is not a valid culture name. Use a BCP-47 name such as 'zh-CN', 'es' or 'de-DE'.",
                nameof(answerLanguage), ex);
        }

        // NativeName is empty (or just repeats the code) in invariant-globalization mode, where only the name survives.
        var native = string.IsNullOrWhiteSpace(culture.NativeName) || culture.NativeName == culture.Name
            ? string.Empty
            : " / " + culture.NativeName;

        return $"""
            Language rule: always answer the user in {culture.EnglishName}{native} ({culture.Name}).
            Write every user-facing sentence (summaries, explanations, questions and error notes) in that
            language - even when the user writes in another language, and even when earlier answers in this
            conversation were written in another language (then switch to {culture.Name} instead of copying
            their style). Keep code, API names, identifiers, numbers and data values exactly as they are.


            """;
    }

    /// <summary>
    /// Prepends the rule from <see cref="BuildLanguageDirective"/> to a preset or custom prompt. Returns
    /// <paramref name="preset"/> unchanged when no language is configured.
    /// </summary>
    public static string WithAnswerLanguage(string preset, string? answerLanguage)
        => BuildLanguageDirective(answerLanguage) + preset;

    /// <summary>
    /// Builds the user prompt used by the Extractor Agent for a single extraction request.
    /// </summary>
    /// <param name="jsonFormat">Either a JSON Schema or a JSON example describing the expected shape.</param>
    /// <param name="text">The unstructured source text.</param>
    public static string BuildTextToDataUserPrompt(string jsonFormat, string text)
        => $"""
        Extract structured data from the following text.

        Return a single minified JSON value that strictly conforms to <format>.
        - Do not include explanations, comments, or code fences.
        - Treat <format> as JSON Schema if it looks like one (has $schema/properties/type), otherwise as a JSON EXAMPLE that defines the exact output shape.

        <format>
        {jsonFormat ?? string.Empty}
        </format>
        <text>
        {text ?? string.Empty}
        </text>
        """;
}
