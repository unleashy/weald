using System.Text;

namespace Weald.Tests;

public abstract class BaseTest
{
    public static IEnumerable<TestCaseData<Rune>> UnicodeSetSource(string set)
    {
        var runes = set.EnumerateRunes().ToArray();

        for (var i = 0; i < runes.Length; ++i) {
            var start = runes[i];
            var end = start;

            if (i + 2 < runes.Length && runes[i + 1] == new Rune('-')) {
                end = runes[i + 2];
                i += 2;
            }

            for (var rune = start.Value; rune < end.Value + 1; ++rune) {
                var td = new TestCaseData<Rune>(new Rune(rune));
                td.SetArgDisplayNames($"U+{rune:X4}");
                yield return td;
            }
        }
    }
}
