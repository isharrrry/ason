using Ason;
using Ason.CodeGen;
using Ason.Console.Template;
using Ason.Console.Template.Modules;
using AsonRunner;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;

// Any OpenAI-compatible endpoint works: set MY_OPEN_AI_BASE_URL (e.g. https://api.deepseek.com)
// and MY_OPEN_AI_MODEL (e.g. deepseek-flash) together with MY_OPEN_AI_KEY.
var apiKey = Environment.GetEnvironmentVariable("MY_OPEN_AI_KEY") ?? string.Empty;
var baseUrl = Environment.GetEnvironmentVariable("MY_OPEN_AI_BASE_URL");
var modelId = Environment.GetEnvironmentVariable("MY_OPEN_AI_MODEL") ?? "gpt-4.1-mini";
IChatCompletionService chatService;
if (string.IsNullOrWhiteSpace(baseUrl)) {
    chatService = new OpenAIChatCompletionService(modelId, apiKey);
}
else {
#pragma warning disable SKEXP0010 // custom OpenAI-compatible endpoints are evaluation-only in SK 1.45
    chatService = new OpenAIChatCompletionService(modelId, new Uri(baseUrl), apiKey);
#pragma warning restore SKEXP0010
}


OperatorsLibrary operatorLibrary = new OperatorBuilder()
                                        .AddAssemblies(typeof(MainOperator).Assembly)
                                        .Build();

RootOperator rootOperator = new RootModule().RootOperator;
AsonClientOptions options = new() {
    ExecutionMode = ExecutionMode.InProcess
};
var asonChatClient = new AsonClient(chatService, rootOperator, operatorLibrary, options);

asonChatClient.Log += (o, e) => Debug.WriteLine($"{e.Source}: {e.Message}");


Console.WriteLine("Type a message — you can create, update, delete, or ask about customers and orders:");
while (true) {
    Console.Write("You: ");
    var input = Console.ReadLine();
    if (string.IsNullOrWhiteSpace(input) || string.Equals(input, "exit", StringComparison.OrdinalIgnoreCase))
        break;

    Console.Write("Assistant: ");

    await foreach (var token in asonChatClient.SendStreamingAsync(input)) {
        Console.Write(token);
    }
    Console.WriteLine();
}

Console.WriteLine("Goodbye.");
