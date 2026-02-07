using System.Globalization;
using System.Text;

namespace Weald.Core;

internal static class StringExtensions
{
    extension(string self)
    {
        [Pure]
        public string Escape() =>
            self
                .EnumerateRunes()
                .Select(rune => rune == new Rune('"') ? "\\\"" : Rune.Escape(rune))
                .JoinToString(separator: "", prefix: "\"", suffix: "\"");

        [Pure]
        public int LengthInGraphemeClusters()
        {
            var si = new StringInfo(self);
            return si.LengthInTextElements;
        }
    }
}

internal static class RuneExtensions
{
    extension(Rune self)
    {
        [Pure]
        public static bool IsGraphical(Rune rune) =>
            Rune.GetUnicodeCategory(rune) switch {
                UnicodeCategory.Control or
                    UnicodeCategory.Format or
                    UnicodeCategory.PrivateUse or
                    UnicodeCategory.Surrogate or
                    UnicodeCategory.OtherNotAssigned or
                    UnicodeCategory.LineSeparator or
                    UnicodeCategory.ParagraphSeparator => false,
                _ => true,
            };

        [Pure]
        public static string Escape(Rune rune) =>
            rune.Value switch {
                ' '                           => " ",
                '\n'                          => @"\n",
                '\r'                          => @"\r",
                '\t'                          => @"\t",
                '\f'                          => @"\f",
                '\b'                          => @"\b",
                '\\'                          => @"\\",
                '\''                          => @"\'",
                '\0'                          => @"\0",
                _ when Rune.IsGraphical(rune) => rune.ToString(),
                _ when rune.IsBmp             => $@"\u{rune.Value:X4}",
                _                             => $@"\U{rune.Value:X8}",
            };

        [Pure]
        public void Deconstruct(out int value)
        {
            value = self.Value;
        }
    }
}

internal static class EnumerableExtensions
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

internal static class DictionaryExtensions
{
    public static TValue GetValueElse<TKey, TValue>(
        this IDictionary<TKey, TValue> self,
        TKey key,
        [RequireStaticDelegate] Func<TKey, TValue> func
    ) =>
        self.TryGetValue(key, out var value) ? value : self[key] = func(key);
}
