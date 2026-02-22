namespace Weald.Core;

public sealed class SourceInfo
{
    public Source Source { get; }
    public LineIndices LineIndices { get; }

    [Pure]
    public static SourceInfo Create(Source source)
    {
        return new SourceInfo(source, LineIndices.For(source.Body));
    }

    private SourceInfo(Source source, LineIndices lineIndices)
    {
        Source = source;
        LineIndices = lineIndices;
    }

    public LineColumn.Range LineColumnAt(Loc loc) => LineColumn.FromLoc(Body, loc);

    public IEnumerable<Line> Lines()
    {
        var indices = LineIndices.Indices;
        for (var line = 0; line < indices.Length; ++line) {
            var start = Index.FromStart(indices[line]);
            var end =
                line + 1 < indices.Length
                    ? Index.FromStart(indices[line + 1])
                    : Index.FromEnd(0);

            yield return new Line(1 + line, Body[start .. end]);
        }
    }

    public string Name => Source.Name;
    public string Body => Source.Body;
}

public readonly record struct Line(int Number, string Text);
