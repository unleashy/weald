namespace Weald.Core;

public enum TokenTag : byte
{
    Invalid,
    End,
}

public readonly record struct Token(TokenTag Tag, string Text, Loc Loc)
{
    public Token() : this(TokenTag.Invalid, "", default)
    {}

    public static Token End(Loc loc) => new(TokenTag.End, "", loc);

    public static Token Invalid(string message, Loc loc) => new(TokenTag.Invalid, message, loc);

    public override string ToString() => $"Token.{Tag}={Text.Escape()}@{Loc}";
}
