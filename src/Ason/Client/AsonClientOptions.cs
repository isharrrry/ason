using Microsoft.Extensions.Logging;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Reflection;
using AsonRunner;

namespace Ason;

public sealed class AsonClientOptions {
    public ILogger? Logger { get; init; }
    public int MaxFixAttempts { get; init; } = 2;

    /// <summary>
    /// Instructions of the Script Agent. Defaults to <see langword="null"/>, which means the built-in
    /// <see cref="AgentPrompts.ScriptAgentTemplate"/> is used with the generated operator API substituted into
    /// its <c>{0}</c> placeholder. A value assigned here is sent to the model <em>verbatim</em> (it is not
    /// formatted), so it must already contain the API text - build it with
    /// <see cref="AgentPrompts.BuildScriptInstructions"/> - and note that it replaces the operator instance
    /// declarations that <see cref="AsonClient"/> otherwise appends to the preset API block.
    /// </summary>
    public string? ScriptInstructions { get; init; }

    /// <summary>
    /// Instructions of the Reception Agent, which routes a request and can answer it directly.
    /// Defaults to <see langword="null"/>, which means <see cref="AgentPrompts.ReceptionAgentTemplate"/> is
    /// used. Read that constant to keep the preset wording while adding your own rules.
    /// </summary>
    public string? ReceptionInstructions { get; init; }

    /// <summary>
    /// Instructions of the Explainer Agent, which writes the text the user finally reads.
    /// Defaults to <see langword="null"/>, which means <see cref="AgentPrompts.ExplainerAgentTemplate"/> is used.
    /// </summary>
    public string? ExplainerInstructions { get; init; }

    /// <summary>
    /// Pins the language of the text the user reads, as a BCP-47 culture name such as <c>zh-CN</c>.
    ///
    /// <see langword="null"/> (the default) leaves every prompt untouched, so this is an explicit opt-in.
    /// When set, a language rule built by <see cref="AgentPrompts.BuildLanguageDirective"/> is prepended to the
    /// Reception and Explainer instructions - also when <see cref="ReceptionInstructions"/> or
    /// <see cref="ExplainerInstructions"/> are overridden, because the answer language is a cross-cutting
    /// behaviour of the host and not part of a preset.
    ///
    /// The Script agent is deliberately excluded: it only emits C# (the user-facing sentences come from the
    /// other two agents) and its impossibility sentence must keep starting with the literal word "Cannot",
    /// which the retry logic string-matches.
    ///
    /// A malformed name makes the <see cref="AsonClient"/> constructor throw, so a broken value surfaces at
    /// startup. A well-formed but unregistered tag (such as <c>zz</c>) cannot be detected - the runtime accepts
    /// it - and is passed to the model as-is; check the value if answers come back in the wrong language.
    /// </summary>
    public string? AnswerLanguage { get; init; }

    public IChatCompletionService? ScriptChatCompletion { get; init; }
    public IChatCompletionService? ReceptionChatCompletion { get; init; }
    public IChatCompletionService? ExplainerChatCompletion { get; init; }

    public bool SkipReceptionAgent { get; init; } = false;
    public bool SkipExplainerAgent { get; init; } = false;

    // Runner execution mode moved from AsonClient constructor
    public ExecutionMode ExecutionMode { get; init; } = ExecutionMode.InProcess;

    // When true, exposes the OrchestratorChatClient operator methods to scripts via generated proxies
    public bool AllowTextExtractor { get; init; } = true;

    public string[] ForbiddenScriptKeywords { get; init; } = new[]
    {
            "System.Diagnostics", "Process.Start", "System.IO", "DllImport", "System.Runtime.InteropServices",
            "System.Reflection", "Assembly.Load", "Activator.CreateInstance", "Directory.Delete", "Environment.GetEnvironmentVariable", "System.Net.Http"
        };

    // --- Remote runner configuration (new) ---
    public bool UseRemoteRunner { get; init; } = false;
    public string? RemoteRunnerBaseUrl { get; init; }
    public string? RemoteRunnerDockerImage { get; init; } = DockerInfo.DockerImageString;
    public bool StopLocalRunnerWhenEnablingRemote { get; init; } = true;

    // Optional per-client extra method filter (applied on top of snapshot cache)
    public Func<MethodInfo, bool>? AdditionalMethodFilter { get; init; }

    // Optional explicit path to Ason.ExternalExecutor (dll or exe). If null, default discovery logic is used.
    public string? RunnerExecutablePath { get; init; }

    /// <summary>
    /// A transport supplied by the host, replacing the ones the execution mode and the remote runner switches
    /// would build. This is how an application whose operators live somewhere else - a bridge endpoint reached
    /// over gRPC or MCP - keeps the whole client pipeline while the script runs on the far side.
    /// </summary>
    public Func<Ason.Transport.IRunnerTransport>? TransportFactory { get; init; }
}