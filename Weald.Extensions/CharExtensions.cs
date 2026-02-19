using System.Diagnostics;
using JetBrains.Annotations;

namespace Weald.Extensions;

public static class CharExtensions
{
    extension(char)
    {
        [Pure]
        public static int ToDigit(char c, int radix)
        {
            Debug.Assert(radix is >= 2 and <= 36, "radix must be between 2 and 36");

            uint value;
            if (c > '9' && radix > 10) {
                // the 3rd bit of an ASCII character always determines its case if applicable
                const uint toUppercaseMask = ~0b0010_0000u;

                // maps 'A'..'Z' to 10..35; non-digits become >= 36
                value = ((uint)c - 'A') & toUppercaseMask + 10;
            }
            else {
                // maps '0'..'9' to 0..9; non-digits wrap around to > 36
                value = (uint)c - '0';
            }

            Debug.Assert(value < radix, $"{c} is not a digit in radix {radix}");
            return (int)value;
        }
    }
}
