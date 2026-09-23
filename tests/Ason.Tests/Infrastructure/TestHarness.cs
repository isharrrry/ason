using System;
using System.Reflection;
using System.Threading.Tasks;
using Ason;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel;
using Microsoft.Extensions.DependencyInjection;
using Ason.Client.Execution;
using AsonRunner;
using Ason.CodeGen;

namespace Ason.Tests.Infrastructure;

internal static class TestHarness {
    static OperatorsLibrary Snapshot = new OperatorBuilder()
        .AddAssemblies(typeof(RootOperator).Assembly)
        .SetBaseFilter(mi => mi.GetCustomAttribute<AsonMethodAttribute>() != null)
        .Build();

    internal static AsonClient CreateBasicClient(
        IChatCompletionService chat,
        AsonClientOptions? opts = null,
        IChatCompletionService? receptionChat = null,
        IChatCompletionService? explainerChat = null) {
        var root = new RootOperator(new object());
        var options = opts ?? new AsonClientOptions();
        // A new options object is built here, so this list is the complete set of forwarded fields: anything a
        // test sets on AsonClientOptions but is missing below is silently dropped.
        options = new AsonClientOptions {
            SkipReceptionAgent = options.SkipReceptionAgent,
            SkipExplainerAgent = options.SkipExplainerAgent,
            MaxFixAttempts = options.MaxFixAttempts,
            AnswerLanguage = options.AnswerLanguage,
            ScriptInstructions = options.ScriptInstructions,
            ReceptionInstructions = options.ReceptionInstructions,
            ExplainerInstructions = options.ExplainerInstructions,
            ScriptChatCompletion = options.ScriptChatCompletion ?? chat,
            ReceptionChatCompletion = receptionChat ?? options.ReceptionChatCompletion ?? chat,
            ExplainerChatCompletion = explainerChat ?? options.ExplainerChatCompletion ?? chat,
            ExecutionMode = ExecutionMode.InProcess
        };
        return new AsonClient(chat, root, Snapshot, options);
    }
}
