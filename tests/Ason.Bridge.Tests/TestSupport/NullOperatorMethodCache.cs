using Ason.Invocation;

namespace Ason.Bridge.Tests.TestSupport;

/// <summary>
/// The agent side owns no operators, so it never has to resolve an operator method locally: an invoke never
/// travels over a bridge transport. This cache answers "not found" and keeps that expectation explicit.
/// </summary>
internal sealed class NullOperatorMethodCache : IOperatorMethodCache {
    public bool TryGet(Type declaringType, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
    public bool TryGetStatic(string targetTypeName, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
    public OperatorMethodEntry GetOrAddClosedGeneric(OperatorMethodEntry openEntry, Type[] typeArguments) => openEntry;
}
