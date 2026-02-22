using System.Collections.Immutable;

namespace Weald.Core;

public readonly record struct LineColumn
{
    public readonly record struct Range(LineColumn Start, LineColumn End)
    {
        public override string ToString()
        {
            var s = Start.ToString();

            if (Start.Line < End.Line - 1) {
                s += "-" + End;
            }
            else if (Start.Column < End.Column - 1) {
                s += "-" + End.Column;
            }

            return s;
        }
    }

    [ValueRange(1, int.MaxValue)] public int Line { get; } = 1;
    [ValueRange(1, int.MaxValue)] public int Column { get; } = 1;

    public LineColumn(int line, int column)
    {
        Debug.Assert(1 <= line, "line must be greater than or equal to 1");
        Debug.Assert(1 <= column, "column must be greater than or equal to 1");

        Line = line;
        Column = column;
    }

    [Pure]
    public static LineColumn FromIndex(string text, int index)
    {
        Debug.Assert(0 <= index, "index must be greater than or equal to 0");
        Debug.Assert(index <= text.Length, "index must be less than or equal to text.Length");

        var (line, startLineIndex) = LineIndices.For(text).Get(index);
        var column = text[startLineIndex .. index].LengthInGraphemeClusters();

        // edge case: pointing at \n in \r\n chain. adjust column number to make the \r\n seem
        // like a single character
        if (IsLfInCrLf(text, index)) {
            column -= 1;
        }

        return new LineColumn(line + 1, column + 1);
    }

    [Pure]
    public static Range FromLoc(string text, Loc loc) =>
        new() {
            Start = FromIndex(text, loc.Start),
            End = FromIndex(text, loc.Start + loc.Length),
        };

    public override string ToString() => $"{Line}:{Column}";

    private static bool IsLfInCrLf(string text, int index) =>
        0 < index &&
        index < text.Length &&
        text[index] == '\n' &&
        text[index - 1] == '\r';
}

public readonly struct LineIndices(ImmutableArray<int> indices)
{
    private static readonly Dictionary<string, LineIndices> Cache = [];

    [Pure]
    public static LineIndices For(string text) => Cache.GetValueElse(text, Compute);

    [Pure]
    public (int line, int startLineIndex) Get(int index)
    {
        var line = indices.BinarySearch(index);
        if (line < 0) line = ~line - 1;

        return (line, indices[line]);
    }

    public ImmutableArray<int> Indices => indices;

    [Pure]
    private static LineIndices Compute(string text)
    {
        var indices = ImmutableArray.CreateBuilder<int>(initialCapacity: 1);

        indices.Add(0);

        for (var i = 0; i < text.Length; ++i) {
            if (IsCrLf(text, i)) {
                indices.Add(i + 2);
                i += 1; // skip \n
            }
            else if (text[i] == '\n') {
                indices.Add(i + 1);
            }
        }

        return new LineIndices(indices.DrainToImmutable());
    }

    [Pure]
    private static bool IsCrLf(string text, int index) =>
        index + 1 < text.Length && text[index] == '\r' && text[index + 1] == '\n';
}
