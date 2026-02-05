namespace Weald.Core;

public enum TokenTag : byte
{
    Invalid,
    End,
    Newline,
    Name,
    Integer,
    Float,
    String,

    // Keywords
    KwTrue,
    KwFalse,

    // Punctuation
    PDot,
    PComma,
    PColon,
    PBackslash,
    PPipe,
    PPlus,
    PMinus,
    PStar,
    PSlash,
    PPercent,
    PCaret,
    PBang,
    PAndAnd,
    POrOr,
    PLess,
    PLessEqual,
    PEqual,
    PBangEqual,
    PGreaterEqual,
    PGreater,
    PParenOpen,
    PParenClose,
    PBracketOpen,
    PBracketClose,
    PBraceOpen,
    PBraceClose,
}

public readonly record struct Token(TokenTag Tag, string Text, Loc Loc)
{
    public Token() : this(TokenTag.Invalid, "", default)
    {}

    public static Token End(Loc loc) => new(TokenTag.End, "", loc);

    public static Token Invalid(string message, Loc loc) => new(TokenTag.Invalid, message, loc);

    public override string ToString() => $"Token.{Tag}={(Text ?? "").Escape()}@{Loc}";
}
