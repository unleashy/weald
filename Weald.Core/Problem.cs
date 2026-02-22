using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace Weald.Core;

[method: SetsRequiredMembers]
public readonly record struct ProblemDesc(string Id, string Message)
{
    public required string Id { get; init; } = Id;
    public required string Message { get; init; } = Message;
}

[method: SetsRequiredMembers]
public readonly record struct Problem(ProblemDesc Desc, Loc Loc)
{
    public required ProblemDesc Desc { get; init; } = Desc;
    public required Loc Loc { get; init; } = Loc;

    public string FormatForConsole(SourceInfo info)
    {
        var lineColumn = info.LineColumnAt(Loc);

        var lcText = $"\e[1m{info.Name}:{lineColumn}\e[0m";
        var category = Desc.Id[0 .. Desc.Id.IndexOf('/', StringComparison.Ordinal)];
        var id = $"\e[1;31m{category} error\e[0m \e[90m[{Desc.Id}]\e[0m";

        var relevantLines = info.Lines()
            .Skip(lineColumn.Start.Line - 1)
            .Take(1 + lineColumn.End.Line - lineColumn.Start.Line)
            .ToImmutableArray();

        var lastLineN = relevantLines.Last().Number.ToString(CultureInfo.InvariantCulture);
        var pad = lastLineN.Length;
        var offset = $" {lastLineN.PadLeft(pad)} | ".Length;

        var underline = "";
        if (!(lineColumn.Start.Line < lineColumn.End.Line - 1)) {
            var spaces = new string(' ', lineColumn.Start.Column + offset - 1);
            var carets = new string(
                '^',
                int.Max(1, lineColumn.End.Column - lineColumn.Start.Column)
            );
            underline = $"{spaces}\e[31m{carets}\e[0m";
        }

        var lineRender = relevantLines
            .Select(line => {
                var lineS = line.Number.ToString(CultureInfo.InvariantCulture).PadLeft(pad);
                return $"\e[90m {lineS} | \e[0m{line.Text}";
            })
            .Append(underline)
            .JoinToString('\n');

        return $"{lcText}: {id}\n{Desc.Message}\n\n{lineRender}";
    }
}

public sealed class ProblemArrayBuilder
{
    private ImmutableArray<Problem>.Builder _problems = ImmutableArray.CreateBuilder<Problem>();

    public bool IsEmpty => _problems.Count == 0;

    public void Report(string id, string message, Loc loc)
    {
        Report(new ProblemDesc(id, message), loc);
    }

    public void Report(ProblemDesc desc, Loc loc)
    {
        Report(new Problem(desc, loc));
    }

    public void Report(Problem problem) => _problems.Add(problem);

    public ImmutableArray<Problem> Drain() => _problems.DrainToImmutable();
}
