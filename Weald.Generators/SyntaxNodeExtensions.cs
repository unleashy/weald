using Microsoft.CodeAnalysis;

namespace Weald.Generators;

internal static class SyntaxNodeExtensions
{
    public static Location GetCacheableLocation(this SyntaxNode self)
    {
        var location = self.GetLocation();
        return Location.Create(
            location.SourceTree?.FilePath ?? string.Empty,
            location.SourceSpan,
            location.GetLineSpan().Span
        );
    }
}
