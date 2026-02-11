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

    #region Keywords

    KwDiscard,
    KwFalse,
    KwTrue,

    #endregion

    #region Punctuation

    PParenOpen,
    PParenClose,
    PBracketOpen,
    PBracketClose,
    PBraceOpen,
    PBraceClose,
    PStar,
    PBackslash,
    PAndAnd,
    PPercent,
    PCaret,
    POr,
    POrOr,
    PPlus,
    PMinus,
    PComma,
    PColon,
    PDot,
    PSlash,
    PLess,
    PLessEqual,
    PEqual,
    PEqualEqual,
    PBang,
    PBangEqual,
    PGreaterEqual,
    PGreater,

    #endregion
}

public readonly record struct Token(TokenTag Tag, string? Text, Loc Loc)
{
    public static Token End(Loc loc) => new(TokenTag.End, null, loc);

    public static Token Invalid(string message, Loc loc) => new(TokenTag.Invalid, message, loc);

    public static Token Newline(Loc loc) => new(TokenTag.Newline, null, loc);

    public static Token Name(string value, Loc loc) => new(TokenTag.Name, value, loc);

    public static Token Punctuation(TokenTag tag, Loc loc)
    {
        Debug.Assert(TokenTag.IsPunctuation(tag));
        return new Token(tag, null, loc);
    }

    public static Token Keyword(TokenTag tag, Loc loc)
    {
        Debug.Assert(TokenTag.IsKeyword(tag));
        return new Token(tag, null, loc);
    }

    public override string ToString() =>
        Text switch {
            {} text => $"Token.{Tag}={text.Escape()}@{Loc}",
            _       => $"Token.{Tag}@{Loc}",
        };
}

public static class TokenTagExtensions
{
    extension(TokenTag)
    {
        public static bool IsPunctuation(TokenTag tag) =>
            typeof(TokenTag).GetEnumName(tag)!.StartsWith('P');

        public static bool IsKeyword(TokenTag tag) =>
            typeof(TokenTag).GetEnumName(tag)!.StartsWith("Kw", StringComparison.Ordinal);

        public static TokenTag? GetKeyword(string term) =>
            term switch {
                "_" => TokenTag.KwDiscard,
                "false" => TokenTag.KwFalse,
                "true" => TokenTag.KwTrue,
                _ => null,
            };
    }
}
