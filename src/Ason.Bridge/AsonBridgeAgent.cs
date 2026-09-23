using Ason.CodeGen;
using Ason.Invocation;
using ModelContextProtocol.Client;

namespace Ason.Bridge;

/// <summary>
/// The agent side of a split deployment, reduced to what it actually is: a client whose operator library comes
/// from the application rather than from its own assemblies.
///
/// This is deliberately the only piece of agent-side glue the library provides. Everything else is ordinary
/// ASON usage - an <c>AsonClient</c> with a transport that points at the application.
/// </summary>
public static class AsonBridgeAgent {

    /// <summary>
    /// Builds the client's operator library from a manifest read over a bridge: the proxy layer and the
    /// signature listing the application published, and nothing else.
    ///
    /// No assembly is scanned and no handle directory is supplied, because this side never resolves an operator
    /// method - the application does, in its own process. That is what keeps the two halves from drifting: there
    /// is no second copy of the API to keep in sync.
    /// </summary>
    public static OperatorsLibrary ToOperatorsLibrary(this AsonBridgeManifest manifest) {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        return new OperatorsLibrary(
            Task.FromResult((manifest.Proxies, manifest.Signatures, (IOperatorMethodCache)new RemoteOperatorMethodCache())),
            false,
            Array.Empty<IMcpClient>(),
            Array.Empty<System.Reflection.Assembly>());
    }

    /// <summary>
    /// Answers "not found" for every lookup. The client only needs a cache because the runtime insists on one;
    /// an invoke never arrives here, since the application resolves its own operator calls.
    /// </summary>
    sealed class RemoteOperatorMethodCache : IOperatorMethodCache {
        public bool TryGet(Type declaringType, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
        public bool TryGetStatic(string targetTypeName, string name, int argCount, out OperatorMethodEntry entry) { entry = null!; return false; }
        public OperatorMethodEntry GetOrAddClosedGeneric(OperatorMethodEntry openEntry, Type[] typeArguments) => openEntry;
    }
}
