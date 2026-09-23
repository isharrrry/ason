namespace Ason.Bridge;

/// <summary>One parameter of <see cref="AsonBridgeMethod"/>.</summary>
public sealed record AsonBridgeParameter(string Name, string Type);

/// <summary>One callable method, named and typed exactly as a script sees it.</summary>
public sealed record AsonBridgeMethod(string Name, string? Description, string ReturnType, IReadOnlyList<AsonBridgeParameter> Parameters);

/// <summary>One operator, or one static operator module.</summary>
public sealed record AsonBridgeOperator(string TypeName, string? Description, bool IsStatic, IReadOnlyList<AsonBridgeMethod> Methods);

/// <summary>One field of <see cref="AsonBridgeModel"/>.</summary>
public sealed record AsonBridgeField(string Name, string Type);

/// <summary>One <c>[AsonModel]</c> data transfer type.</summary>
public sealed record AsonBridgeModel(string Name, string? Description, IReadOnlyList<AsonBridgeField> Fields);

/// <summary>A live operator instance, addressable by handle over the single-function interface.</summary>
public sealed record AsonBridgeInstance(string Handle, string TypeName, bool Initialized);

/// <summary>
/// The machine-readable API listing. It is a JSON-friendly projection of <c>OperatorApiCatalog</c>, which is
/// the same reflection walk the script prompt is built from, so a listing cannot drift from what scripts see.
/// </summary>
public sealed record AsonBridgeApi(IReadOnlyList<AsonBridgeOperator> Operators, IReadOnlyList<AsonBridgeModel> Models) {

    public static AsonBridgeApi Empty { get; } = new(Array.Empty<AsonBridgeOperator>(), Array.Empty<AsonBridgeModel>());

    /// <summary>Total number of callable methods across <see cref="Operators"/>.</summary>
    public int MethodCount => Operators.Sum(o => o.Methods.Count);

    public static AsonBridgeApi FromCatalog(OperatorApiCatalog catalog) => new(
        catalog.Operators
            .Select(o => new AsonBridgeOperator(
                o.TypeName,
                o.Description,
                o.IsStatic,
                o.Methods.Select(m => new AsonBridgeMethod(
                    m.Name,
                    m.Description,
                    m.ReturnType,
                    m.Parameters.Select(p => new AsonBridgeParameter(p.Name, p.Type)).ToList())).ToList()))
            .ToList(),
        catalog.Models
            .Select(m => new AsonBridgeModel(
                m.Name,
                m.Description,
                m.Fields.Select(f => new AsonBridgeField(f.Name, f.Type)).ToList()))
            .ToList());
}
