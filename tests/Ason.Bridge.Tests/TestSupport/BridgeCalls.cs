using System.Text.Json;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>Builds <see cref="AsonBridgeFunctionCall"/>s with JSON arguments, like a gRPC/MCP client would.</summary>
internal static class BridgeCalls {

    public static AsonBridgeFunctionCall Call(string @operator, string method, params object?[] args)
        => new(@operator, method, null, ToJson(args));

    public static AsonBridgeFunctionCall CallHandled(string @operator, string method, string? handle, params object?[] args)
        => new(@operator, method, handle, ToJson(args));

    static IReadOnlyList<JsonElement> ToJson(object?[] args)
        => args.Select(a => JsonSerializer.SerializeToElement(a)).ToArray();
}
