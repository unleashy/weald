using System.Text;

namespace Weald.UnicodeTables;

public static class Predicates
{
    public static bool IsNameStart(Rune rune) =>
        Check(rune, Tables.AsciiStart, Tables.TrieStart);

    public static bool IsNameContinue(Rune rune) =>
        Check(rune, Tables.AsciiContinue, Tables.TrieContinue);

    private static bool Check(Rune rune, UInt128 ascii, byte[] table)
    {
        if (rune.IsAscii) {
            return (ascii & ((UInt128) 1 << rune.Value)) != 0;
        }

        var index = rune.Value / 8 / Tables.ChunkLength;
        var chunk = index < table.Length ? table[index] : 0;
        var offset = chunk * Tables.ChunkLength / 2 + rune.Value / 8 % Tables.ChunkLength;
        return (Tables.Leaf[offset] >>> (rune.Value % 8) & 1) != 0;
    }
}
