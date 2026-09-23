using System.Reflection;
using System.Text;

namespace Ason;

/// <summary>One operator (or static operator module) and the methods scripts may call on it.</summary>
public sealed record OperatorApiOperator(string TypeName, string? Description, bool IsStatic, IReadOnlyList<OperatorApiMethod> Methods);

/// <summary>One callable method, named and typed exactly as the Script agent sees it in the prompt.</summary>
public sealed record OperatorApiMethod(string Name, string? Description, string ReturnType, IReadOnlyList<OperatorApiParameter> Parameters);

/// <summary>One parameter of <see cref="OperatorApiMethod"/>.</summary>
public sealed record OperatorApiParameter(string Name, string Type);

/// <summary>One <c>[AsonModel]</c> data-transfer type and its fields.</summary>
public sealed record OperatorApiModel(string Name, string? Description, IReadOnlyList<OperatorApiField> Fields);

/// <summary>One field of <see cref="OperatorApiModel"/>.</summary>
public sealed record OperatorApiField(string Name, string Type);

/// <summary>
/// A structured description of the operator API that scripts can call - the data behind the <c>&lt;api&gt;</c>
/// block of the script prompt - plus renderings of it.
///
/// It exists so an application can answer "which operations are available?" without parsing the prompt text,
/// and so additional renderings (Markdown, JSON, tool/function-calling schemas) all come from one source
/// instead of re-deriving the naming rules. The names, types and descriptions reuse the very helpers that
/// generate the prompt text (<see cref="ProxySerializer"/>), which is what keeps a listing from drifting from
/// what the model is actually told.
///
/// Build one with <see cref="Describe"/>, passing the same assemblies that were registered with the client.
/// </summary>
public sealed class OperatorApiCatalog {

    OperatorApiCatalog(IReadOnlyList<OperatorApiOperator> operators, IReadOnlyList<OperatorApiModel> models) {
        Operators = operators;
        Models = models;
    }

    /// <summary>The operators found in the given assemblies, ordered by type name.</summary>
    public IReadOnlyList<OperatorApiOperator> Operators { get; }

    /// <summary>The <c>[AsonModel]</c> types found in the given assemblies, ordered by type name.</summary>
    public IReadOnlyList<OperatorApiModel> Models { get; }

    /// <summary>Total number of callable methods across <see cref="Operators"/>.</summary>
    public int MethodCount => Operators.Sum(o => o.Methods.Count);

    /// <summary>
    /// Describes the operator API of the given assemblies. Excluded base types and the static-module rule
    /// match the proxy generation exactly, so the catalog lists what a script can really call.
    /// </summary>
    public static OperatorApiCatalog Describe(params Assembly[]? assemblies) {
        // De-duplicated like OperatorBuilder.AddAssemblies, so passing the same assembly twice is harmless.
        var scan = assemblies is { Length: > 0 }
            ? assemblies.Where(a => a is not null).Distinct().ToArray()
            : Array.Empty<Assembly>();

        var operators = new List<OperatorApiOperator>();
        foreach (var type in GetMarkedTypes<AsonOperatorAttribute>(scan).OrderBy(t => t.Name, StringComparer.Ordinal)) {
            if (ProxySerializer.IsExcludedBase(type)) continue;

            // Static classes are operator modules: no instance, addressed by type name over the wire.
            bool isStatic = type.IsAbstract && type.IsSealed;
            var attribute = type.GetCustomAttribute<AsonOperatorAttribute>();

            var methods = type
                .GetMethods((isStatic ? BindingFlags.Static : BindingFlags.Instance) | BindingFlags.Public | BindingFlags.DeclaredOnly)
                .Where(m => m.GetCustomAttribute<AsonMethodAttribute>() != null)
                .OrderBy(m => m.Name, StringComparer.Ordinal)
                .Select(m => new OperatorApiMethod(
                    ProxySerializer.TrimAsyncSuffix(m.Name),
                    m.GetCustomAttribute<AsonMethodAttribute>()?.Description,
                    ProxySerializer.MapReturnSignature(m.ReturnType),
                    m.GetParameters()
                        .Select((p, i) => new OperatorApiParameter(p.Name ?? "arg" + i, ProxySerializer.GetFriendlyTypeName(p.ParameterType)))
                        .ToList()))
                .ToList();

            operators.Add(new OperatorApiOperator(type.Name, attribute?.Description, isStatic, methods));
        }

        var models = GetMarkedTypes<AsonModelAttribute>(scan)
            .Where(t => t.IsClass && !t.IsAbstract)
            .OrderBy(t => t.Name, StringComparer.Ordinal)
            .Select(t => new OperatorApiModel(
                t.Name,
                t.GetCustomAttribute<AsonModelAttribute>()?.Description,
                t.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                    .Where(p => p is { CanRead: true, CanWrite: true })
                    .OrderBy(p => p.Name, StringComparer.Ordinal)
                    .Select(p => new OperatorApiField(p.Name, ProxySerializer.GetFriendlyTypeName(p.PropertyType)))
                    .ToList()))
            .ToList();

        return new OperatorApiCatalog(operators, models);
    }

    /// <summary>
    /// Renders the catalog as Markdown: one table per operator plus a table per model. Intended for user-facing
    /// output such as a chat answer, and deterministic (no timestamps), so it can be asserted on.
    /// </summary>
    public string ToMarkdown() {
        var sb = new StringBuilder();
        sb.AppendLine("# Operator API");
        sb.AppendLine();
        sb.AppendLine($"{Operators.Count} operators, {MethodCount} methods, {Models.Count} models.");
        sb.AppendLine();

        if (Operators.Count == 0) {
            sb.AppendLine("No operators are registered.");
            sb.AppendLine();
        }

        foreach (var op in Operators) {
            sb.Append("## ").Append(op.TypeName);
            if (op.IsStatic) sb.Append(" (static module)");
            sb.AppendLine();
            sb.AppendLine();
            if (!string.IsNullOrWhiteSpace(op.Description)) {
                sb.AppendLine(Escape(op.Description!));
                sb.AppendLine();
            }

            if (op.Methods.Count == 0) {
                sb.AppendLine("No callable methods.");
                sb.AppendLine();
                continue;
            }

            sb.AppendLine("| Method | Returns | Parameters | Description |");
            sb.AppendLine("| --- | --- | --- | --- |");
            foreach (var method in op.Methods) {
                var parameters = method.Parameters.Count == 0
                    ? "-"
                    : string.Join(", ", method.Parameters.Select(p => $"`{p.Type} {p.Name}`"));
                sb.AppendLine($"| `{method.Name}()` | `{method.ReturnType}` | {parameters} | {Escape(method.Description) } |");
            }
            sb.AppendLine();
        }

        if (Models.Count > 0) {
            sb.AppendLine("## Models");
            sb.AppendLine();
            foreach (var model in Models) {
                sb.Append("### ").AppendLine(model.Name);
                sb.AppendLine();
                if (!string.IsNullOrWhiteSpace(model.Description)) {
                    sb.AppendLine(Escape(model.Description!));
                    sb.AppendLine();
                }
                if (model.Fields.Count == 0) {
                    sb.AppendLine("No fields.");
                    sb.AppendLine();
                    continue;
                }
                sb.AppendLine("| Field | Type |");
                sb.AppendLine("| --- | --- |");
                foreach (var field in model.Fields) {
                    sb.AppendLine($"| `{field.Name}` | `{field.Type}` |");
                }
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    // Deliberately not the proxy generator's helper: that one scans every loaded assembly when it is given no
    // assemblies (surprising for a catalog), and it swallows type-load failures. Everything else - which types
    // count and how they are named - is shared with the prompt text on purpose.
    static IEnumerable<Type> GetMarkedTypes<TAttribute>(Assembly[] assemblies) where TAttribute : Attribute {
        foreach (var assembly in assemblies) {
            Type[] types;
            try { types = assembly.GetTypes(); }
            catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t is not null).Cast<Type>().ToArray(); }
            catch { continue; }

            foreach (var type in types) {
                if (type.GetCustomAttribute<TAttribute>() != null) yield return type;
            }
        }
    }

    // Markdown tables break on unescaped pipes and multi-line cells; descriptions come from attributes.
    static string Escape(string? text) => string.IsNullOrWhiteSpace(text)
        ? string.Empty
        : text.Replace("|", "\\|").Replace("\r\n", " ").Replace('\n', ' ').Trim();
}
