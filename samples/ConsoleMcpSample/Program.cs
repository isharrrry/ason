using Ason;
using Ason.CodeGen;
using Microsoft.SemanticKernel.ChatCompletion;
using ModelContextProtocol.Client;

var context7McpClient = await CreateContext7ClientAsync();
var operatorLibrary = new OperatorBuilder()
    .AddAssemblies(typeof(Program).Assembly)
    .AddMcp(context7McpClient)
    .Build();

IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
Console.WriteLine($"Chat service: {OpenAiCompatibleChatServiceFactory.DescribeConfiguration()}");

AsonClient client = new AsonClient(chatService, new RootOperator(new object()), operatorLibrary);

client.Log += (o, e) => Console.WriteLine($"{e.Source}: {e.Message}");
var result = await client.SendAsync("Find repos related to OpenAI and output a summary in a console-friently format");

Console.WriteLine($"AGENT REPLY: \n {result}");
Console.WriteLine("Press any key to exit...");
Console.ReadKey(intercept: true);

async Task<McpClient> CreateContext7ClientAsync() {
    var httpClient = new HttpClient();
    httpClient.DefaultRequestHeaders.Add("CONTEXT7_API_KEY", Environment.GetEnvironmentVariable("MY_CONTEXT7_API_KEY"));

    return await McpClient.CreateAsync(
        new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint = new Uri("https://mcp.context7.com/mcp")
        }, httpClient));
}