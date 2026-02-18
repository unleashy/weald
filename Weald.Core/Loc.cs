namespace Weald.Core;

public readonly record struct Loc
{
    [NonNegativeValue] public int Start { get; } = 0;
    [NonNegativeValue] public int Length { get; } = 0;

    [Pure]
    public static Loc FromLength(int start, int length)
    {
        Debug.Assert(0 <= start, "start must be non-negative");
        Debug.Assert(0 <= length, "length must be non-negative");
        Debug.Assert(((long) start + length) <= int.MaxValue, "end index must fit within an int");

        return new Loc(start, length);
    }

    [Pure]
    public static Loc FromRange(int start, int end)
    {
        Debug.Assert(0 <= start, "start must be non-negative");
        Debug.Assert(start <= end, "start must be less than or equal to end");

        return new Loc(start, end - start);
    }

    private Loc(int start, int length)
    {
        Start = start;
        Length = length;
    }

    public override string ToString() => $"{Start}:{Length}";
}
