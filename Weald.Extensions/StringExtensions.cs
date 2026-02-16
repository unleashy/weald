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

        [Pure]
        public string TakePrefix(Predicate<char> predicate)
        {
            var end = 0;
            for (var i = 0; i < self.Length; ++i) {
                if (predicate(self[i])) {
                    end = i + 1;
                }
                else {
                    break;
                }
            }

            return self[0 .. end];
        }
    }
}
