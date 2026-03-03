using Microsoft.CodeAnalysis;

namespace Weald.Generators;

internal readonly record struct GenProblem(GenProblemDesc Desc, Location Location)
{
    public Diagnostic ToDiagnostic() => Diagnostic.Create(Desc.ToDescriptor(), Location);

    public static GenProblem Create(GenProblemDesc desc, SyntaxNode node) =>
        new(desc, node.GetCacheableLocation());
}

internal readonly record struct GenProblemDesc
{
    public required string Id { get; init; }
    public required string Title { get; init; }
    public required string Message { get; init; }

    public DiagnosticDescriptor ToDescriptor() =>
        new(
            id: Id,
            title: Title,
            messageFormat: Message,
            category: "Weald",
            defaultSeverity: DiagnosticSeverity.Error,
            isEnabledByDefault: true,
            customTags: WellKnownDiagnosticTags.NotConfigurable
        );
}
