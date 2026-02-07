using System.Text;

namespace Weald.Core;

internal static class RuneOps
{
    public static bool IsIgnorable(Rune rune) =>
        IsWhitespace(rune) || IsNewline(rune);

    public static bool IsWhitespace(Rune rune) => rune is Rune(' ' or '\t' or '\u200E' or '\u200F');

    public static bool IsNewline(Rune rune) => rune is Rune('\n' or '\r');
}
