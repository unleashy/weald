using System.Diagnostics.CodeAnalysis;
using System.Text;
using Weald.UnicodeTables;

namespace Weald.Tests;

[SuppressMessage("Assertion", "NUnit2045:Use Assert.EnterMultipleScope or Assert.Multiple")]
[SuppressMessage("Style", "IDE0300:Simplify collection initialization")]
public class UnicodeTablesTests
{
    [TestCaseSource(nameof(UnicodeSetSource), new object[] { "A-Za-z_" })]
    public void AsciiStart(Rune rune)
    {
        Assert.That(Predicates.IsNameStart(rune), Is.True);
        Assert.That(Predicates.IsNameContinue(rune), Is.True);
    }

    [TestCaseSource(nameof(UnicodeSetSource), new object[] { "A-Za-z0-9_" })]
    public void AsciiContinue(Rune rune)
    {
        Assert.That(Predicates.IsNameContinue(rune), Is.True);
    }

    // not parameterised otherwise it would be like 140k tests lol so just testing some random
    // characters
    [Test]
    public void Unicode()
    {
        Assert.That(Predicates.IsNameStart(new Rune('ↈ')), Is.True);
        Assert.That(Predicates.IsNameStart(new Rune('꩓')), Is.False);

        Assert.That(Predicates.IsNameContinue(new Rune('ↈ')), Is.True);
        Assert.That(Predicates.IsNameContinue(new Rune('꩓')), Is.True);
    }

    [TestCaseSource(
        nameof(UnicodeSetSource),
        new object[] {
            "\u2202\u2207\u221E\U0001D6C1\U0001D6DB\U0001D6FB\U0001D715\U0001D735\U0001D74F" +
            "\U0001D76F\U0001D789\U0001D7A9\U0001D7C3",
        }
    )]
    public void MathsStart(Rune rune)
    {
        Assert.That(Predicates.IsNameStart(rune), Is.True);
        Assert.That(Predicates.IsNameContinue(rune), Is.True);
    }

    [TestCaseSource(
        nameof(UnicodeSetSource),
        new object[] {
            "\u00B2\u00B3\u00B9\u2070\u2074-\u207E\u2080-\u208E\u2202\u2207\u221E\U0001D6C1" +
            "\U0001D6DB\U0001D6FB\U0001D715\U0001D735\U0001D74F\U0001D76F\U0001D789\U0001D7A9" +
            "\U0001D7C3",
        }
    )]
    public void MathsContinue(Rune rune)
    {
        Assert.That(Predicates.IsNameContinue(rune), Is.True);
    }

    [TestCaseSource(
        nameof(UnicodeSetSource),
        new object[] {
            "\u034F\u115F\u1160\u17B4\u17B5\u180B-\u180D\u180F\u200C\u200D\u3164\uFE00-\uFE0F" +
            "\uFFA0\U000E0100-\U000E01EF",
        }
    )]
    public void Ignorables(Rune rune)
    {
        Assert.That(Predicates.IsNameStart(rune), Is.False);
        Assert.That(Predicates.IsNameContinue(rune), Is.False);
    }

    private static IEnumerable<TestCaseData<Rune>> UnicodeSetSource(string set)
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
