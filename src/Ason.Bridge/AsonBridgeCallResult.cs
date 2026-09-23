using System.Text.Json;

namespace Ason.Bridge;

/// <summary>
/// A request to call exactly one operator method. Arguments are <see cref="JsonElement"/> values, which is how
/// they arrive from any transport; the ASON runtime coerces them onto the method's parameter types.
/// A <paramref name="Handle"/> is only needed for instance operators when more than one live instance of that
/// type exists.
/// </summary>
public sealed record AsonBridgeFunctionCall(string Operator, string Method, string? Handle = null, IReadOnlyList<JsonElement>? Arguments = null) {
    public IReadOnlyList<JsonElement> EffectiveArguments => Arguments ?? Array.Empty<JsonElement>();
}

/// <summary>
/// The outcome of a bridge call. Failures carry one of the <see cref="AsonBridgeErrorCodes"/> so a transport
/// can translate them into its own error shape.
/// </summary>
public sealed record AsonBridgeCallResult(bool Success, JsonElement? Result, string? Error = null, string? ErrorCode = null) {

    public static AsonBridgeCallResult Ok(JsonElement? result) => new(true, result);

    public static AsonBridgeCallResult Fail(string errorCode, string error) => new(false, null, error, errorCode);
}

/// <summary>One log line produced by an executor, relayed to transports that stream logs.</summary>
public sealed record AsonBridgeLogEventArgs(string Level, string Message, string? Source = null, string? Exception = null) {
    public DateTimeOffset TimestampUtc { get; init; } = DateTimeOffset.UtcNow;
}
