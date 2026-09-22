using Ason;
using Ason.CodeGen;
using ConsoleExtractorSample;
using Microsoft.SemanticKernel.ChatCompletion;

string userMessage = "Use the information from the email to get John’s and Bob’s apples, and then calculate the total number of apples";

var operatorLibrary = new OperatorBuilder()
    .AddAssemblies(typeof(MyOperator).Assembly)
    .AddExtractor()
    .Build();

IChatCompletionService chatService = OpenAiCompatibleChatServiceFactory.FromEnvironment();
Console.WriteLine($"Chat service: {OpenAiCompatibleChatServiceFactory.DescribeConfiguration()}");

var myOperator = new MyOperator(new object());
AsonClient client = new AsonClient(chatService, myOperator, operatorLibrary);

Console.WriteLine($"Methods agent can use: MyOperator.Add, MyOperator.GetEmailText, ExtractionOperator.ExtractDataFromText");
Console.WriteLine($"Sample information in your email database: {myOperator.GetEmailText()}");
Console.WriteLine($"User: {userMessage}");

var result = await client.SendAsync(userMessage);

Console.WriteLine($"Agent: {result}");


Console.WriteLine("Press any key to exit...");
Console.ReadKey(intercept: true);

