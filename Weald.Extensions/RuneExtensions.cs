using System.Globalization;
using System.Text;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class RuneExtensions
{
    extension(Rune self)
    {
        [Pure]
        public static Rune? ParseHex(ReadOnlySpan<char> hex) =>
            int.TryParse(
                hex,
                NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture,
                out var value
            ) && Rune.TryCreate(value, out var rune)
                ? rune
                : null;

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
                '\\'                          => @"\\",
                '\0'                          => @"\0",
                '\b'                          => @"\b",
                '\e'                          => @"\e",
                '\f'                          => @"\f",
                '\n'                          => @"\n",
                '\r'                          => @"\r",
                '\t'                          => @"\t",
                _ when Rune.IsGraphical(rune) => rune.ToString(),
                _ when rune.IsLatin1          => $@"\x{rune.Value:X2}",
                _ when rune.IsBmp             => $@"\u{rune.Value:X4}",
                _                             => $@"\U{rune.Value:X8}",
            };

        public bool IsLatin1 {
            [Pure]
            get => self.Value <= 0xFF;
        }

        [Pure]
        public void Deconstruct(out int value)
        {
            value = self.Value;
        }
    }
}
