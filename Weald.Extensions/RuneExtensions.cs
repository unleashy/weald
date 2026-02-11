using System.Globalization;
using System.Text;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class RuneExtensions
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
