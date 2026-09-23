using System.Collections.Concurrent;
using System.Reflection;

namespace Ason.Bridge;

/// <summary>
/// Helpers for the host side of a bridge: turning the <c>[Ason*]</c> markers in an assembly into the live
/// operator directory the bridge addresses.
///
/// Views register themselves (a WPF application attaches its view operators as they load), but a marker-only
/// operator - <c>[AsonOperator]</c> on a plain class with methods marked <c>[AsonMethod]</c> - has no view
/// lifecycle. Those are materialised once and addressed by type name, which is the same rule
/// <c>AsonClient</c> applies for its own scripts.
/// </summary>
public static class AsonBridgeOperators {

    /// <summary>
    /// Creates one instance of every marker-only operator found in <paramref name="assemblies"/>, keyed by type
    /// name. Types that derive from <see cref="OperatorBase"/> are skipped: they need a view to attach to, so
    /// they are registered when the application loads them.
    /// </summary>
    public static ConcurrentDictionary<string, object> MaterializeMarkerOnly(params Assembly[] assemblies) {
        var instances = new ConcurrentDictionary<string, object>(StringComparer.Ordinal);
        if (assemblies is null) return instances;

        foreach (var assembly in assemblies.Where(a => a is not null).Distinct()) {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var type in types) {
                if (type.IsAbstract || type.IsInterface || type.IsGenericTypeDefinition) continue;
                if (typeof(OperatorBase).IsAssignableFrom(type)) continue;
                if (!Attribute.IsDefined(type, typeof(AsonOperatorAttribute))) continue;
                if (type.GetConstructor(Type.EmptyTypes) is null) continue;

                try {
                    if (Activator.CreateInstance(type) is { } instance) instances.TryAdd(type.Name, instance);
                }
                catch {
                    // A type that cannot be constructed is simply not advertised; the manifest is the truth
                    // about what can be called.
                }
            }
        }

        return instances;
    }
}
