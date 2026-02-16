using System.Globalization;
using System.Text;

namespace Weald.Core;

internal static class RuneOps
{
    [Pure]
    public static bool IsForbidden(Rune rune) =>
        (Rune.IsControl(rune) && !IsIgnorable(rune)) ||
        (rune is Rune('\u2028' or '\u2029')) ||
        (Rune.GetUnicodeCategory(rune) == UnicodeCategory.Surrogate);

    [Pure]
    public static bool IsIgnorable(Rune rune) => IsWhitespace(rune) || IsNewline(rune);

    [Pure]
    public static bool IsWhitespace(Rune rune) => rune is Rune(' ' or '\t' or '\u200E' or '\u200F');

    [Pure]
    public static bool IsNewline(Rune rune) => rune is Rune('\n' or '\r');

    [Pure]
    public static bool IsPunctuation(Rune rune) =>
        rune.IsAscii &&
        @"!()[]{}*\&#%`^|~$+-,;:?.@/<=>".Contains((char) rune.Value, StringComparison.Ordinal);

    [Pure]
    public static bool IsNameStart(Rune rune) => UnicodeTables.Predicates.IsNameStart(rune);

    [Pure]
    public static bool IsNameContinue(Rune rune) => UnicodeTables.Predicates.IsNameContinue(rune);

    [Pure]
    public static bool IsNameMedial(Rune rune) => rune is Rune('-');

    [Pure]
    public static bool IsNameFinal(Rune rune) => rune is Rune('?' or '!');

    [Pure]
    public static bool IsNameChar(Rune rune) =>
        IsNameContinue(rune) || IsNameMedial(rune) || IsNameFinal(rune);

    [Pure]
    public static bool IsDecDigit(Rune rune) => rune is Rune(>= '0' and <= '9');

    [Pure]
    public static bool IsHexDigit(Rune rune) =>
        rune is Rune((>= '0' and <= '9') or (>= 'a' and <= 'f') or (>= 'A' and <= 'F'));

    [Pure]
    public static bool IsBinDigit(Rune rune) => rune is Rune('0' or '1');

    [Pure]
    public static bool IsSign(Rune rune) => rune is Rune('-' or '+');

    [Pure]
    public static bool IsNumberStart(Rune rune) => IsSign(rune) || IsDecDigit(rune);
}
