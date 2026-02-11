using System.Text;

namespace Weald.Core;

internal static class RuneOps
{
    [Pure]
    public static bool IsIgnorable(Rune rune) => IsWhitespace(rune) || IsNewline(rune);

    [Pure]
    public static bool IsWhitespace(Rune rune) => rune is Rune(' ' or '\t' or '\u200E' or '\u200F');

    [Pure]
    public static bool IsNewline(Rune rune) => rune is Rune('\n' or '\r');

    [Pure]
    public static bool IsPunctuation(Rune rune) =>
        rune.IsAscii && @"!()[]{}*\&#%`^|~$+-,;:?.@/<=>".Contains((char) rune.Value);

    [Pure]
    public static bool IsNameStart(Rune rune) => UnicodeTables.Predicates.IsNameStart(rune);

    [Pure]
    public static bool IsNameContinue(Rune rune) => UnicodeTables.Predicates.IsNameContinue(rune);

    [Pure]
    public static bool IsNameMedial(Rune rune) => rune is Rune('-');

    [Pure]
    public static bool IsDecDigit(Rune rune) => rune is Rune(>= '0' and <= '9');
}
