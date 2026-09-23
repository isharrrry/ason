using System.Reflection;

namespace Ason.Bridge;

/// <summary>Maps execution outcomes and exceptions onto the stable bridge error codes.</summary>
internal static class AsonBridgeErrors {

    internal static AsonBridgeCallResult From(Exception exception) => exception switch {
        // OperatorInvoker raises this when an instance operator is invoked without a handle and no static
        // module matches the target type either.
        ArgumentNullException argument when string.Equals(argument.ParamName, "handleId", StringComparison.Ordinal)
            => AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.HandleRequired, argument.Message),
        // The handle disappeared because the view that owned it was closed.
        ObjectDisposedException disposed => AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.HandleNotFound, disposed.Message),
        MissingMethodException missing => AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.MethodNotFound, missing.Message),
        _ => AsonBridgeCallResult.Fail(AsonBridgeErrorCodes.ExecutionFailed, Describe(exception))
    };

    // Reflection wraps what an operator actually threw; the inner exception is the useful part.
    static string Describe(Exception exception) {
        var cause = exception is TargetInvocationException { InnerException: { } inner } ? inner : exception;
        return $"{cause.GetType().Name}: {cause.Message}";
    }
}
