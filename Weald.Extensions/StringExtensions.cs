using System.Globalization;
using System.Text;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class StringExtensions
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
