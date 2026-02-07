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
    POr,
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
    PEqualEqual,
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

public readonly record struct Token(TokenTag Tag, string? Text, Loc Loc)
{
    public static Token End(Loc loc) => new(TokenTag.End, null, loc);

    public static Token Invalid(string message, Loc loc) => new(TokenTag.Invalid, message, loc);

    public static Token Newline(Loc loc) => new(TokenTag.Newline, null, loc);

    public static Token Punctuation(TokenTag tag, Loc loc) => new(tag, null, loc);

    public override string ToString() =>
        Text switch {
            {} text => $"Token.{Tag}={text.Escape()}@{Loc}",
            _       => $"Token.{Tag}@{Loc}",
        };
}
