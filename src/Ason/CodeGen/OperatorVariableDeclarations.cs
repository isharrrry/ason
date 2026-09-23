using System.Text;

namespace Ason.CodeGen;

/// <summary>
/// Declares a script variable for every live operator instance, so a generated script can call
/// <c>employeesViewOperator.GetEmployees()</c> without being told the handle. The declarations are appended to
/// the generated proxy layer.
///
/// This is shared rather than re-implemented: <see cref="AsonClient"/> uses it for the script prompt, and the
/// bridge uses the very same text for the scripts it accepts over a transport. Two copies of these naming
/// rules would drift, and a drift shows up as a script that compiles locally and fails remotely.
/// </summary>
public static class OperatorVariableDeclarations {

    /// <summary>
    /// Builds the declaration block. <paramref name="instances"/> is normally
    /// <see cref="RootOperator.OperatorInstances"/>; <paramref name="singletons"/> holds marker-only operators
    /// ([AsonOperator] without <see cref="OperatorBase"/>), which are addressed by their type name.
    /// </summary>
    public static string Build(IReadOnlyDictionary<string, OperatorBase>? instances, IReadOnlyDictionary<string, object>? singletons = null) {
        var sb = new StringBuilder();
        sb.AppendLine();
        var typeInstanceCount = new Dictionary<string, int>(StringComparer.Ordinal);

        var declared = new List<(Type Type, string Handle)>();
        if (instances is not null) {
            foreach (var instance in instances.Values) {
                var type = instance.GetType();
                // The root operator is declared by the proxy layer itself.
                if (type == typeof(RootOperator)) continue;
                declared.Add((type, instance.Handle));
            }
        }
        if (singletons is not null) {
            foreach (var singleton in singletons) declared.Add((singleton.Value.GetType(), singleton.Key));
        }

        foreach (var (type, handle) in declared) {
            var typeName = type.Name;
            if (!typeInstanceCount.TryGetValue(typeName, out var count)) count = 0;
            string baseVar = char.ToLowerInvariant(typeName[0]) + typeName.Substring(1);
            string varName = count == 0 ? baseVar : baseVar + count.ToString();
            typeInstanceCount[typeName] = count + 1;
            string proxyName = typeName;
            bool isRootDerived = typeof(RootOperator).IsAssignableFrom(type) && type != typeof(RootOperator);
            string ctor = isRootDerived ? $"new {proxyName}()" : $"new {proxyName}(\"{handle}\")";
            sb.AppendLine($"{proxyName} {varName} = {ctor};");
        }

        sb.AppendLine();
        return sb.ToString();
    }
}
