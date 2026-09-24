// Records and init-only setters need this marker type. The framework supplies it from .NET 5 on, and this
// library ships the netstandard2.0 asset only (see the csproj), so it has to be declared here.
namespace System.Runtime.CompilerServices {
    internal static class IsExternalInit { }
}
