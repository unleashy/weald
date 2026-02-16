using System.Text;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class EnumerableExtensions
{
    extension<T>(IEnumerable<T> self)
    {
        [Pure]
        public string JoinToString(char separator) => string.Join(separator, self);

        [Pure]
        public string JoinToString(string separator) => string.Join(separator, self);

        [Pure]
        public string JoinToString(string separator, string prefix, string suffix)
        {
            var sb = new StringBuilder(prefix.Length + suffix.Length);

            sb.Append(prefix);
            sb.AppendJoin(separator, self);
            sb.Append(suffix);

            return sb.ToString();
        }
    }
}
