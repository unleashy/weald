namespace Weald.Core;

public enum TokenTag : byte
{
    Invalid,
    End,
    Newline,
    Name,
    Int,
    Float,
    String,

    #region Keywords

    KwDiscard,
    KwElse,
    KwFalse,
    KwIf,
    KwLet,
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
    PAnd,
    PAndAnd,
    PPercent,
    PCaret,
    POr,
    POrOr,
    PPlus,
    PMinus,
    PComma,
    PColon,
    PQuestion,
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

    public static Token Name(string text, Loc loc) => new(TokenTag.Name, text, loc);

    public static Token Integer(string text, Loc loc) => new(TokenTag.Int, text, loc);

    public static Token Float(string text, Loc loc) => new(TokenTag.Float, text, loc);

    public static Token String(string text, Loc loc) => new(TokenTag.String, text, loc);

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

    public override string ToString()
    {
        var tag = $"Token.{Tag}";
        var text = Text is {} t ? $"={t.Escape()}" : "";
        var loc = $"@{Loc}";

        return tag + text + loc;
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
                "_"     => TokenTag.KwDiscard,
                "else"  => TokenTag.KwElse,
                "false" => TokenTag.KwFalse,
                "if"    => TokenTag.KwIf,
                "let"   => TokenTag.KwLet,
                "true"  => TokenTag.KwTrue,
                _ => null,
            };
    }
}
