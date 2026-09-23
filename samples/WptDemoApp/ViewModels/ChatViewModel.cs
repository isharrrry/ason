using Ason;
using Ason.CodeGen;
using AsonRunner;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.SemanticKernel.ChatCompletion;
using System.Diagnostics;
using System.Reflection;
using System.Collections.ObjectModel;
using Microsoft.Extensions.AI;
using WpfSampleApp.AI; 

namespace WpfSampleApp.ViewModels;

public partial class ChatViewModel(MainViewModel mainViewModel) : ObservableObject {
    [ObservableProperty]
    string userInput = "Update fist three employee names to John Doe";

    [ObservableProperty]
    string? chatResponse;

    /// <summary>Which language the assistant will answer in, shown in the chat panel.</summary>
    [ObservableProperty]
    string languageNotice = string.Empty;

    public ObservableCollection<string> PromptSuggestions { get; } = new() {
        "Change position of all employees hired in 2025 to X",
        "Number of employees hired in 2025",
        "Create a chart with sales for the top 3 products in 2025",
        "Add an appointment based on data from the last email",
    };


    MainViewModel _mainViewModel = mainViewModel;
    readonly List<ChatMessage> _messages = new();

    AsonClient Chat;
    static OperatorsLibrary? _sharedSnapshot;

    async Task<OperatorsLibrary> GetOperatorsAsync() => _sharedSnapshot ??= new OperatorBuilder()
        .AddAssemblies(typeof(MainAppOperator).Assembly, typeof(LibDemo.LibDemoOperator).Assembly)
        //.AddExtractor()
        //.AddMcp(await CreateContext7ClientAsync())
        .SetBaseFilter(mi => mi.GetCustomAttribute<AsonMethodAttribute>() != null)
        .Build();

    //async Task<McpClient> CreateContext7ClientAsync() {
    //    var httpClient = new HttpClient();
    //    httpClient.DefaultRequestHeaders.Add("CONTEXT7_API_KEY", Environment.GetEnvironmentVariable("MY_CONTEXT7_API_KEY"));

    //    return await McpClient.CreateAsync(
    //        new HttpClientTransport(new HttpClientTransportOptions {
    //            Endpoint = new Uri("https://mcp.context7.com/mcp")
    //        }, httpClient)).ConfigureAwait(false);
    //}

    [RelayCommand]
    async Task Init() {
        // The demo answers in the Windows display language: it opts into AsonClientOptions.AnswerLanguage, and
        // the library prepends the rule to the Reception and Explainer prompts (the two that write what the
        // user reads). Read AgentPrompts.BuildLanguageDirective for the rule itself; docs/configuration.md
        // covers why the Script agent is left out.
        var uiCulture = PromptLanguage.SystemUiCulture;
        LanguageNotice = PromptLanguage.BuildNotice(uiCulture);

        IChatCompletionService chatService;
        try {
            chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
        }
        catch (InvalidOperationException ex) {
            // A missing key or a malformed base URL must show up in the chat panel instead of
            // tearing the whole window down on startup.
            ChatResponse = ex.Message;
            return;
        }
        var operators = await GetOperatorsAsync();
        var options = new AsonClientOptions {
            MaxFixAttempts = 2,
            SkipReceptionAgent = false,
            ExecutionMode = ExecutionMode.InProcess,
            // Null on an English system, so the presets are used unchanged there.
            AnswerLanguage = PromptLanguage.AnswerLanguage(uiCulture),
            //RunnerExecutablePath = @"..\..\..\..\..\src\bin\Debug\net9.0"
            //UseRemoteRunner = true,
            //RemoteRunnerBaseUrl = "http://localhost:5236"
        };
        Chat = new AsonClient(chatService, _mainViewModel.MainAppOperator, operators, options);

        Chat.Log += (sender, e) => {
            var prefix = $"[{e.Level}] ";
            Debug.WriteLine(prefix + e.Message + (e.Exception is not null ? "\n" + e.Exception : string.Empty));
        };
    }

    [RelayCommand]
    async Task SendMessage() {
        var userText = UserInput?.Trim();
        if (string.IsNullOrWhiteSpace(userText)) return;

        // Add user message to history
        _messages.Add(new ChatMessage(ChatRole.User, userText));
        ChatResponse = string.Empty;
        UserInput = string.Empty;

        await foreach (var chunk in Chat.SendStreamingAsync(_messages)) {
            ChatResponse += chunk;
        }

        if (!string.IsNullOrEmpty(ChatResponse)) {
            _messages.Add(new ChatMessage(ChatRole.Assistant, ChatResponse));
        }
    }

    [RelayCommand]
    void UsePrompt(string prompt) => UserInput = prompt;

    [RelayCommand]
    void ShowSuggestedPrompts(string prompt) => ChatResponse = null;
}