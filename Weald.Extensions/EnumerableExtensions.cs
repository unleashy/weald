using System.Text;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class EnumerableExtensions
{
    [Pure]
    public static string JoinToString<T>(
        this IEnumerable<T> self,
        string separator,
        string prefix,
        string suffix
    )
    {
        var sb = new StringBuilder(prefix.Length + suffix.Length);

        sb.Append(prefix);
        sb.AppendJoin(separator, self);
        sb.Append(suffix);

        return sb.ToString();
    }
}
