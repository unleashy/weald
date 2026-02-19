using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

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
