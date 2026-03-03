using System.CodeDom.Compiler;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Weald.Generators;

[Generator(LanguageNames.CSharp)]
public sealed class AstGenerator : IIncrementalGenerator
{
    private const string CoreNs = "Weald.Core";
    private const string AstInterfaceName = $"{CoreNs}.IAst";
    private const string AstAttributeName = $"{CoreNs}.GenAstAttribute";

    private readonly record struct TypeDesc(string Name, string Prefix);

    private readonly record struct AstModel(
        TypeDesc Desc,
        EqArray<TypeDesc> ContainingTypes,
        string Namespace,
        EqArray<string> Props,
        EqArray<string> Children
    );

    public void Initialize(IncrementalGeneratorInitializationContext ctx)
    {
        var pipeline = ctx.SyntaxProvider
            .ForAttributeWithMetadataName(
                AstAttributeName,
                predicate: static (node, _) =>
                    node is
                        ClassDeclarationSyntax or
                        RecordDeclarationSyntax or
                        StructDeclarationSyntax,
                transform: ParseIntoModel
            )
            .Where(static it => it is not null)
            .Collect()
            .WithComparer(new EqImmutableArrayComparer<object?>());

        ctx.RegisterSourceOutput(pipeline, static (spCtx, results) => {
            var asts = new List<AstModel>();
            foreach (var result in results) {
                switch (result) {
                    case GenProblem problem: {
                        spCtx.ReportDiagnostic(problem.ToDiagnostic());
                        break;
                    }

                    case AstModel ast: {
                        asts.Add(ast);
                        break;
                    }

                    default: {
                        throw new InvalidOperationException(
                            $"Unexpected result type: {result?.GetType().FullName}"
                        );
                    }
                }
            }

            if (asts.Count == 0) return;

            var s = EmitAll(asts);
            spCtx.AddSource("AstGenerator.g.cs", s);
        });
    }

    private static object? ParseIntoModel(
        GeneratorAttributeSyntaxContext ctx,
        CancellationToken ct
    )
    {
        ct.ThrowIfCancellationRequested();

        if (
            ctx.TargetNode is not TypeDeclarationSyntax decl
            || decl.Modifiers.All(it => !it.IsKind(SyntaxKind.PartialKeyword))
        ) {
            return GenProblem.Create(Descs.InvalidSignature, ctx.TargetNode);
        }

        if (ctx.TargetSymbol is not INamedTypeSymbol symbol) {
            return null;
        }

        var compilation = ctx.SemanticModel.Compilation;

        var iast = compilation.GetTypeByMetadataName(AstInterfaceName);
        if (iast is null) {
            return GenProblem.Create(Descs.AstInterfaceNotFound, ctx.TargetNode);
        }

        var ienumerable =
            compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1");
        if (ienumerable is null) {
            return null;
        }

        var props = ImmutableArray.CreateBuilder<string>();
        var children = ImmutableArray.CreateBuilder<string>();

        foreach (var prop in symbol.GetMembers().OfType<IPropertySymbol>()) {
            ct.ThrowIfCancellationRequested();

            if (prop.GetMethod is null || prop.IsImplicitlyDeclared || prop.IsIndexer) continue;

            props.Add($"(\"{prop.Name}\", {prop.Name})");

            if (IsAst(prop.Type, iast)) {
                children.Add(prop.Name);
            }
            else if (IsAstEnumerable(prop.Type, iast)) {
                children.Add($"..{prop.Name}");
            }
        }

        props.Add($"(\"Loc\", Loc)");

        return new AstModel(
            Desc: new TypeDesc(symbol.Name, PrefixOf(symbol)),
            ContainingTypes: ContainingTypesOf(symbol),
            Namespace: NamespaceOf(symbol),
            Props: props.DrainToEquatable(),
            Children: children.DrainToEquatable()
        );
    }

    private static bool IsAst(ITypeSymbol type, INamedTypeSymbol iast) =>
        SymbolEqualityComparer.Default.Equals(type, iast) ||
        type.AllInterfaces.Any(it => SymbolEqualityComparer.Default.Equals(it, iast));

    private static bool IsAstEnumerable(ITypeSymbol type, INamedTypeSymbol iast) =>
        type is INamedTypeSymbol nts &&
        type.AllInterfaces.Any(iface =>
            iface.OriginalDefinition.SpecialType ==
                SpecialType.System_Collections_Generic_IEnumerable_T
        ) &&
        (nts.TypeArguments.FirstOrDefault()?.AllInterfaces ?? [])
            .Any(it => SymbolEqualityComparer.Default.Equals(it, iast));

    private static string PrefixOf(INamedTypeSymbol symbol)
    {
        var parts = new List<string>();

        if (AccessibilityOf(symbol) is {} accessibility) {
            parts.Add(accessibility);
        }

        if (ModifiersOf(symbol) is {} modifiers) {
            parts.Add(modifiers);
        }

        if (symbol.IsRecord) parts.Add("record");

        switch (symbol.TypeKind) {
            case TypeKind.Class: parts.Add("class"); break;
            case TypeKind.Struct: parts.Add("struct"); break;
            default: break;
        }

        return string.Join(" ", parts);
    }

    private static string? AccessibilityOf(ISymbol symbol)
    {
        return symbol.DeclaredAccessibility switch {
            Accessibility.Public => "public",
            Accessibility.Private => "private",
            Accessibility.Protected => "protected",
            Accessibility.Internal => "internal",
            Accessibility.ProtectedOrInternal => "protected internal",
            Accessibility.ProtectedAndInternal => "private protected",
            _ => null,
        };
    }

    private static string? ModifiersOf(INamedTypeSymbol symbol)
    {
        var modifiers = new List<string>();

        if (symbol.IsStatic) {
            modifiers.Add("static");
        }

        if (symbol.IsSealed) {
            modifiers.Add("sealed");
        }

        if (symbol.IsAbstract) {
            modifiers.Add("abstract");
        }

        if (symbol.IsReadOnly) {
            modifiers.Add("readonly");
        }

        modifiers.Add("partial");

        return modifiers.Count > 0 ? string.Join(" ", modifiers) : null;
    }

    private static EqArray<TypeDesc> ContainingTypesOf(INamedTypeSymbol symbol)
    {
        var types = ImmutableArray.CreateBuilder<TypeDesc>();
        var current = symbol.ContainingType;

        while (current is not null) {
            types.Add(new TypeDesc(current.Name, PrefixOf(current)));
            current = current.ContainingType;
        }

        types.Reverse();
        return types.DrainToEquatable();
    }

    private static string NamespaceOf(ISymbol symbol)
    {
        var ns = symbol.ContainingNamespace;
        return ns is null || ns.IsGlobalNamespace ? string.Empty : ns.ToDisplayString();
    }

    private static string EmitAll(List<AstModel> asts)
    {
        var sw = new StringWriter();
        var w = new IndentedTextWriter(sw, "    ");

        w.WriteLine(
            """
            // <auto-generated/>
            #pragma warning disable
            #nullable enable
            """
        );

        foreach (var group in asts.GroupBy(ast => ast.Namespace)) {
            var ns = group.Key;
            if (ns != string.Empty) {
                w.WriteLineNoTabs("");
                w.WriteLine($"namespace {ns}");
                w.WriteLine("{");
                ++w.Indent;
            }

            foreach (var ast in group) {
                w.WriteLineNoTabs("");

                foreach (var cty in ast.ContainingTypes) {
                    w.WriteLine($"{cty.Prefix} {cty.Name}");
                    w.WriteLine("{");
                    ++w.Indent;
                }

                w.WriteLine($"{ast.Desc.Prefix} {ast.Desc.Name} : {AstInterfaceName}");
                w.WriteLine("{");
                ++w.Indent;

                EmitGenLoc(w, ast);
                EmitGenName(w, ast);
                EmitGenProps(w, ast);
                EmitGenChildren(w, ast);

                --w.Indent;
                w.WriteLine("}");

                foreach (var _ in ast.ContainingTypes) {
                    --w.Indent;
                    w.WriteLine("}");
                }
            }

            if (ns != string.Empty) {
                --w.Indent;
                w.WriteLine("}");
            }
        }

        return sw.ToString();
    }

    private static void EmitGenName(IndentedTextWriter w, AstModel ast)
    {
        var name = ast.Desc.Name.StartsWith("Ast", StringComparison.Ordinal)
            ? ast.Desc.Name[3 ..]
            : ast.Desc.Name;

        w.WriteLine($"public string DisplayName() => \"{name}\";");
    }

    private static void EmitGenProps(IndentedTextWriter w, AstModel ast)
    {
        w.WriteLine("public IEnumerable<(string Name, object? Value)> Props() => [");
        ++w.Indent;

        foreach (var prop in ast.Props) {
            w.WriteLine($"{prop},");
        }

        --w.Indent;
        w.WriteLine("];");
    }

    private static void EmitGenChildren(IndentedTextWriter w, AstModel ast)
    {
        w.WriteLine("public IEnumerable<IAst?> Children() => [");
        ++w.Indent;

        foreach (var child in ast.Children) {
            w.WriteLine($"{child},");
        }

        --w.Indent;
        w.WriteLine("];");
    }

    private static void EmitGenLoc(IndentedTextWriter w, AstModel _)
    {
        w.WriteLine("public required Loc Loc { get; init; }");
    }

    private static class Descs
    {
        public static readonly GenProblemDesc InvalidSignature = new() {
            Id = "WEALD1000",
            Title = "Invalid AST node signature",
            Message = "AST node must be a partial class or struct",
        };

        public static readonly GenProblemDesc AstInterfaceNotFound = new() {
            Id = "WEALD1001",
            Title = "AST interface not found",
            Message = $"Could not find the IAst type ({AstInterfaceName})",
        };
    }
}
