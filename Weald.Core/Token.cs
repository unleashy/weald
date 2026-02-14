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

public enum TokenSubTag : byte
{
    None,
    Dec,
    Hex,
    Bin,
}

public readonly record struct Token(TokenTag Tag, TokenSubTag SubTag, string? Text, Loc Loc)
{
    public static Token End(Loc loc) => new(TokenTag.End, TokenSubTag.None, null, loc);

    public static Token Invalid(string message, Loc loc) =>
        new(TokenTag.Invalid, TokenSubTag.None, message, loc);

    public static Token Newline(Loc loc) =>
        new(TokenTag.Newline, TokenSubTag.None, null, loc);

    public static Token Name(string value, Loc loc) =>
        new(TokenTag.Name, TokenSubTag.None, value, loc);

    public static Token DecInteger(string value, Loc loc) =>
        new(TokenTag.Integer, TokenSubTag.Dec, value, loc);

    public static Token HexInteger(string value, Loc loc) =>
        new(TokenTag.Integer, TokenSubTag.Hex, value, loc);

    public static Token BinInteger(string value, Loc loc) =>
        new(TokenTag.Integer, TokenSubTag.Bin, value, loc);

    public static Token Float(string value, Loc loc) =>
        new(TokenTag.Float, TokenSubTag.None, value, loc);

    public static Token String(string value, Loc loc) =>
        new(TokenTag.String, TokenSubTag.None, value, loc);

    public static Token Punctuation(TokenTag tag, Loc loc)
    {
        Debug.Assert(TokenTag.IsPunctuation(tag));
        return new Token(tag, TokenSubTag.None, null, loc);
    }

    public static Token Keyword(TokenTag tag, Loc loc)
    {
        Debug.Assert(TokenTag.IsKeyword(tag));
        return new Token(tag, TokenSubTag.None, null, loc);
    }

    public override string ToString()
    {
        var tag = $"Token.{Tag}";
        var subtag = SubTag != TokenSubTag.None ? $"/{SubTag}" : "";
        var text = Text is {} t ? $"={t.Escape()}" : "";
        var loc = $"@{Loc}";

        return tag + subtag + text + loc;
    }
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
