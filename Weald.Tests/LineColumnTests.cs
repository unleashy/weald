using Weald.Core;

namespace Weald.Tests;

public class LineColumnTests
{
    [TestCaseSource(nameof(OfIndexCases))]
    public LineColumn OfIndex(string text, int index)
    {
        return LineColumn.FromIndex(text, index);
    }

    public static readonly List<TestCaseData> OfIndexCases = [
        // basics
        new TestCaseData("", 0).Returns(new LineColumn(1, 1)),
        new TestCaseData("a", 0).Returns(new LineColumn(1, 1)),
        new TestCaseData("a\nbc", 1).Returns(new LineColumn(1, 2)),
        new TestCaseData("a\nbc", 2).Returns(new LineColumn(2, 1)),
        new TestCaseData("a\nbc", 3).Returns(new LineColumn(2, 2)),
        new TestCaseData("a\nbc", 4).Returns(new LineColumn(2, 3)),

        // \r\n handling
        new TestCaseData("a\nbc\r\ndef", 4).Returns(new LineColumn(2, 3)),
        new TestCaseData("a\nbc\r\ndef", 5).Returns(new LineColumn(2, 3)),
        new TestCaseData("a\nbc\r\ndef", 6).Returns(new LineColumn(3, 1)),
        new TestCaseData("a\nbc\r\ndef", 9).Returns(new LineColumn(3, 4)),

        // unicode handling
        new TestCaseData("🌈1\n2🧠", 2).Returns(new LineColumn(1, 2)),
        new TestCaseData("🌈1\n2🧠", 7).Returns(new LineColumn(2, 3)),

        // combining chars
        new TestCaseData("jo\u0302\u0325-", 4).Returns(new LineColumn(1, 3)),
    ];

    [TestCaseSource(nameof(RangeToStringCases))]
    public string RangeToString(LineColumn start, LineColumn end)
    {
        return new LineColumn.Range(start, end).ToString();
    }

    public static readonly List<TestCaseData> RangeToStringCases = [
        new TestCaseData(new LineColumn(1, 1), new LineColumn(1, 1)).Returns("1:1"),
        new TestCaseData(new LineColumn(2, 1), new LineColumn(3, 1)).Returns("2:1"),
        new TestCaseData(new LineColumn(2, 1), new LineColumn(2, 99)).Returns("2:1-99"),
        new TestCaseData(new LineColumn(3, 1), new LineColumn(5, 1)).Returns("3:1-5:1"),
        new TestCaseData(new LineColumn(50, 32), new LineColumn(99, 99)).Returns("50:32-99:99"),
    ];
}
