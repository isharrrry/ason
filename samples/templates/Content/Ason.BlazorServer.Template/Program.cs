using Ason.CodeGen;
using Ason.BlazorServer.Template.Operators;
using Ason.BlazorServer.Template.Components;
using Ason.BlazorServer.Template.State;
using AsonRunner;
using Microsoft.SemanticKernel.Connectors.OpenAI;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<SessionState>();
builder.Services.AddAson(
    // Any OpenAI-compatible endpoint works: set MY_OPEN_AI_BASE_URL (e.g. https://api.deepseek.com)
    // and MY_OPEN_AI_MODEL (e.g. deepseek-flash) together with MY_OPEN_AI_KEY.
    defaultChatCompletionFactory: sp => {
        var apiKey = Environment.GetEnvironmentVariable("MY_OPEN_AI_KEY") ?? string.Empty;
        var baseUrl = Environment.GetEnvironmentVariable("MY_OPEN_AI_BASE_URL");
        var modelId = Environment.GetEnvironmentVariable("MY_OPEN_AI_MODEL") ?? "gpt-4.1-mini";
        if (string.IsNullOrWhiteSpace(baseUrl)) {
            return new OpenAIChatCompletionService(modelId, apiKey);
        }
#pragma warning disable SKEXP0010 // custom OpenAI-compatible endpoints are evaluation-only in SK 1.45
        return new OpenAIChatCompletionService(modelId, new Uri(baseUrl), apiKey);
#pragma warning restore SKEXP0010
    },
    rootOperatorFactory: sp => sp.GetRequiredService<SessionState>().MainAppOperator,
    operators: new OperatorBuilder().AddAssemblies(typeof(MainAppOperator).Assembly).Build(),
    configureOptions: opt => {
        opt.ExecutionMode = ExecutionMode.ExternalProcess;
    });

builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error", createScopeForErrors: true);
    app.UseHsts();
}

app.UseHttpsRedirection();


app.UseAntiforgery();

app.MapStaticAssets();
app.MapRazorComponents<App>()
    .AddInteractiveServerRenderMode();

app.Run();
