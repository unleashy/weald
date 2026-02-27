using System.Buffers;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class SpanExtensions
{
    [Pure]
    public static ReadOnlySpan<char> TakePrefix(
        this ReadOnlySpan<char> self,
        SearchValues<char> values
    )
    {
        var end = self.IndexOfAnyExcept(values);
        return end >= 0 ? self[0 .. end] : self;
    }
}
