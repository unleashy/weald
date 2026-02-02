using System.Globalization;
using System.Text;

namespace Weald.Core;

internal static class StringExtensions
{
    [Pure]
    public static string Escape(this string? self) =>
        (self ?? "")
            .EnumerateRunes()
            .Select(rune => rune == new Rune('"') ? "\\\"" : rune.Escape())
            .JoinToString(separator: "", prefix: "\"", suffix: "\"");
}

internal static class RuneExtensions
{
    extension(Rune self)
    {
        [Pure]
        public static bool TryGetRuneAt(Source source, int index, out Rune rune) =>
            Rune.TryGetRuneAt(source.Body, index, out rune);

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
                _ => true
            };

        [Pure]
        public string Escape() =>
            self.Value switch {
                ' '  => " ",
                '\n' => @"\n",
                '\r' => @"\r",
                '\t' => @"\t",
                '\f' => @"\f",
                '\b' => @"\b",
                '\\' => @"\\",
                '\'' => @"\'",
                '\0' => @"\0",
                _ when Rune.IsGraphical(self) => self.ToString(),
                _ when self.IsBmp => $@"\u{self.Value:X4}",
                _ => $@"\U{self.Value:X8}"
            };
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
