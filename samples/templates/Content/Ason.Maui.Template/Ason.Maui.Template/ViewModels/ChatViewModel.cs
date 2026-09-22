using Ason;
using Ason.CodeGen;
using Ason.Maui.Template.Operators;
using AsonRunner;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using Microsoft.SemanticKernel.Connectors.OpenAI;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace Ason.Maui.Template.ViewModels;

public partial class ChatViewModel : ObservableObject {
    [ObservableProperty]
    string userInput = @"Update the contact details for the following customers:
- Alice Johnson:  
  Phone: +1 (212) 555-0147  
  Address: 123 Main St, New York, NY  
- Bob Smith:  
  Phone: +1 (310) 555-0923  
  Address: 456 Oak Ave, Los Angeles, CA  
- Carol Davis:  
  Phone: +1 (617) 555-3789  
  Address: 789 Pine Rd, Boston, MA""";
    AsonClient asonChatClient;
    RootOperator mainAppOperator;

    [ObservableProperty]
    string chatResponse = string.Empty;
    public ChatViewModel(RootOperator rootOperator)
    {
        mainAppOperator = rootOperator;
        InitAsonClient();
    }

    [MemberNotNull(nameof(asonChatClient))]
    void InitAsonClient() {
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
                                                .AddAssemblies(typeof(MainAppOperator).Assembly)
                                                .Build();

        AsonClientOptions options = new() {
            ExecutionMode = ExecutionMode.ExternalProcess,
            RemoteRunnerBaseUrl = DeviceInfo.Platform == DevicePlatform.Android? "http://10.0.2.2:5222" : "http://localhost:5222",
            UseRemoteRunner = true,
        };
        asonChatClient = new AsonClient(chatService, mainAppOperator, operatorLibrary, options);

        asonChatClient.Log += (o, e) => Debug.WriteLine($"{e.Source}: {e.Message}");
    }

    [RelayCommand]
    async Task SendMessage() {
        var userText = UserInput?.Trim();
        if (string.IsNullOrWhiteSpace(userText)) return;
        UserInput = string.Empty;
        ChatResponse = string.Empty;

        ChatResponse = await asonChatClient.SendAsync(userText); 
        //await foreach (var token in asonChatClient.SendStreamingAsync(userText)) {
        //    ChatResponse += token;
        //}
    }
}
