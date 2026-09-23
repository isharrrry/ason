using System;
using System.Linq;
using System.Reflection;

namespace Ason.Tests.Operators;

/// <summary>
/// Covers <see cref="OperatorApiCatalog"/>: the structured listing of the operator API and its Markdown
/// rendering. The important assertion here is the cross-check against <see cref="ProxySerializer"/> - the
/// catalog is only worth having if the names and types it reports are exactly the ones the Script agent is
/// told about, in any rendering.
/// </summary>
public class OperatorApiCatalogTests {

    static OperatorApiCatalog Catalog => OperatorApiCatalog.Describe(typeof(OperatorApiCatalogTests).Assembly);
    static string Markdown => Catalog.ToMarkdown();

    [Fact]
    public void It_lists_the_operators_of_the_given_assemblies() {
        Assert.Contains("## SimpleOp", Markdown);
        Assert.Contains("## TestRootOp", Markdown);
        Assert.Contains("## SignatureShapeOp", Markdown);
        Assert.Equal("TestRootOp", Assert.Single(Catalog.Operators, o => o.TypeName == "TestRootOp").TypeName);
    }

    [Fact]
    public void Excluded_base_types_are_not_listed() {
        Assert.DoesNotContain("## OperatorBase", Markdown);
        Assert.DoesNotContain("## RootOperator", Markdown);
    }

    [Fact]
    public void Static_modules_are_flagged_as_such() {
        var module = Assert.Single(Catalog.Operators, o => o.TypeName == "SignatureShapeStatic");

        Assert.True(module.IsStatic);
        Assert.Contains("## SignatureShapeStatic (static module)", Markdown);
        Assert.Contains("| `Echo()` | `String` | `String text` | Echoes the text |", Markdown);
    }

    [Fact]
    public void Methods_are_named_and_typed_like_the_script_agent_sees_them() {
        var simpleOp = Assert.Single(Catalog.Operators, o => o.TypeName == "SimpleOp");
        var shapeOp = Assert.Single(Catalog.Operators, o => o.TypeName == "SignatureShapeOp");

        // The Async suffix is gone and the Task wrapper is unwrapped - same rules as the prompt text.
        Assert.Contains(shapeOp.Methods, m => m is { Name: "Sum", ReturnType: "Int32" });
        Assert.Contains(simpleOp.Methods, m => m is { Name: "DoNothing", ReturnType: "void" });
        Assert.DoesNotContain(simpleOp.Methods, m => m.Name.Contains("Async"));
        Assert.DoesNotContain(Markdown, "Task");
    }

    [Fact]
    public void Every_listed_method_matches_a_line_of_the_prompt_text() {
        // The drift guard: whatever the catalog reports must also appear in the generated signature text, so a
        // second rendering can never disagree with what the model was told.
        var signatures = ProxySerializer.SerializeSignatures(typeof(OperatorApiCatalogTests).Assembly);
        var checkedMethods = 0;

        foreach (var op in Catalog.Operators) {
            foreach (var method in op.Methods) {
                var parameterList = string.Join(", ", method.Parameters.Select(p => $"{p.Type} {p.Name}"));
                var expected = $"    public {(op.IsStatic ? "static " : string.Empty)}{method.ReturnType} {method.Name}({parameterList});";
                Assert.Contains(expected, signatures);
                checkedMethods++;
            }
        }

        Assert.True(checkedMethods >= 15, $"expected the test assembly to expose plenty of methods, saw {checkedMethods}");
    }

    [Fact]
    public void Descriptions_from_the_markers_are_carried_over() {
        Assert.Contains("Signature shape operators", Markdown);
        Assert.Contains("Counts the items", Markdown);
        Assert.Contains("Signature shape model", Markdown);
    }

    [Fact]
    public void Models_are_listed_with_their_fields() {
        var model = Assert.Single(Catalog.Models, m => m.Name == "SignatureShapeModel");

        Assert.Contains(model.Fields, f => f is { Name: "Optional", Type: "Int32?" });
        Assert.Contains("### SignatureShapeModel", Markdown);
        Assert.Contains("| `Values` | `List<Int32>` |", Markdown);
    }

    [Fact]
    public void Markdown_escapes_pipes_and_newlines_that_would_break_a_table() {
        Assert.Contains(@"Pipes \| and newlines must not break", Markdown);
        Assert.DoesNotContain("Pipes | and", Markdown);
        Assert.Contains(@"Returns a \| b", Markdown);
    }

    [Fact]
    public void Without_assemblies_it_describes_nothing_instead_of_scanning_the_process() {
        var empty = OperatorApiCatalog.Describe();

        Assert.Empty(empty.Operators);
        Assert.Empty(empty.Models);
        Assert.Equal(0, empty.MethodCount);
        Assert.Contains("No operators are registered", empty.ToMarkdown());
    }

    [Fact]
    public void The_rendering_is_deterministic() {
        // No timestamps or ordering by reflection order: the same input must render identically.
        Assert.Equal(Markdown, OperatorApiCatalog.Describe(typeof(OperatorApiCatalogTests).Assembly).ToMarkdown());
    }

    [Fact]
    public void Passing_the_same_assembly_twice_lists_it_once() {
        // De-duplicated like OperatorBuilder.AddAssemblies, so a caller cannot accidentally double the listing.
        var twice = OperatorApiCatalog.Describe(
            typeof(OperatorApiCatalogTests).Assembly,
            typeof(OperatorApiCatalogTests).Assembly);

        Assert.Equal(Catalog.Operators.Count, twice.Operators.Count);
        Assert.Equal(Catalog.MethodCount, twice.MethodCount);
    }

    [Fact]
    public void The_markdown_summary_counts_agree_with_the_structured_catalog() {
        Assert.Contains($"{Catalog.Operators.Count} operators, {Catalog.MethodCount} methods, {Catalog.Models.Count} models.", Markdown);
    }

    [Fact]
    public void An_operator_without_marked_methods_is_still_listed() {
        // Matches the prompt text, which also emits the empty class: the listing must not hide an operator the
        // model was told about.
        var assembly = typeof(SimpleOp).Assembly;
        var empty = OperatorApiCatalog.Describe(assembly).Operators.FirstOrDefault(o => o.TypeName == nameof(NoMethodsOp));

        Assert.NotNull(empty);
        Assert.Empty(empty!.Methods);
        Assert.Contains("No callable methods.", OperatorApiCatalog.Describe(assembly).ToMarkdown());
    }
}

[AsonOperator(description: "Has no callable methods")]
public class NoMethodsOp : OperatorBase {
    public NoMethodsOp() { }

    public string NotMarkedAsMethod() => "not callable";
}
