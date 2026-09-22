using System.Runtime.CompilerServices;

// The marker attributes now live in Ason.Abstractions (netstandard2.0) so that class
// libraries targeting netstandard2.0 / .NET 6 can reference them without pulling in the
// ASON runtime. Forwarding keeps already-compiled consumers of Ason.dll working.
[assembly: TypeForwardedTo(typeof(Ason.ProxyAttributeBase))]
[assembly: TypeForwardedTo(typeof(Ason.AsonOperatorAttribute))]
[assembly: TypeForwardedTo(typeof(Ason.AsonMethodAttribute))]
[assembly: TypeForwardedTo(typeof(Ason.AsonModelAttribute))]
