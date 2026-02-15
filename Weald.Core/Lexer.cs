using System.Collections;
using System.Globalization;
using System.Text;

namespace Weald.Core;

public struct Lexer(Source source) : IEnumerable<Token>
{
    private enum State : byte
    {
        Start,
        Tokenising,
        End,
    }

    private Cursor _cursor = new(source);
    private State _state = State.Start;

    public Token Next()
    {
        var startMark = _cursor.NewMark();

        while (true) {
            var result = _state switch {
                State.Start      => NextStart(),
                State.Tokenising => NextTokenising(startMark),
                State.End        => NextEnd(),
            };

            if (result is {} token) return token;
        }
    }

    private Token? NextStart()
    {
        SkipBom();
        SkipHashbang();
        _state = State.Tokenising;
        return null;
    }

    private void SkipBom()
    {
        _ = _cursor.Match('\uFEFF');
    }

    private void SkipHashbang()
    {
        if (_cursor.Match("#!")) {
            _ = _cursor.NextUntil(RuneOps.IsNewline);
        }
    }

    private readonly Token NextEnd()
    {
        return Token.End(_cursor.Locate());
    }

    private Token? NextTokenising(Cursor.Mark startMark)
    {
        if (_cursor.IsEmpty) {
            _state = State.End;
            return null;
        }

        if (_cursor.Match("--")) return NextComment(startMark);

        return _cursor.Peek switch {
            var c when RuneOps.IsWhitespace(c)  => NextWhitespace(),
            var c when RuneOps.IsNewline(c)     => NextNewlineOrComment(startMark),

            var c when RuneOps.IsNumberStart(c) => NextNumber(),
            var c when RuneOps.IsNameStart(c)   => NextName(),
            Rune('"')                           => NextStringStd(),
            Rune('`')                           => NextStringRaw(),

            var c when RuneOps.IsPunctuation(c) => NextPunctuation(),

            _ => NextInvalid(),
        };
    }

    private Token? NextWhitespace()
    {
        _ = _cursor.NextWhile(RuneOps.IsWhitespace);
        return null;
    }

    private Token? NextNewlineOrComment(Cursor.Mark startMark)
    {
        Debug.Assert(_cursor.IsEmpty || RuneOps.IsNewline(_cursor.Peek));

        while (!_cursor.IsEmpty) {
            _ = _cursor.NextWhile(RuneOps.IsIgnorable);

            if (_cursor.Match("--")) {
                _ = _cursor.NextUntil(RuneOps.IsNewline);
                continue;
            }

            return Token.Newline(_cursor.Locate(startMark));
        }

        return null;
    }

    private Token? NextComment(Cursor.Mark startMark)
    {
        _ = _cursor.NextUntil(RuneOps.IsNewline);
        return NextNewlineOrComment(startMark);
    }

    private Token NextNumber()
    {
        Debug.Assert(_cursor.Check(RuneOps.IsNumberStart));

        var mark = _cursor.NewMark();

        if (_cursor.Check(RuneOps.IsSign)) {
            if (!_cursor.MatchNext(RuneOps.IsDecDigit)) {
                return NextPunctuation();
            }
        }

        var validUnderscores = true;

        var (isDigit, radix) = Radix();
        Digits(isDigit, ref validUnderscores);

        if (radix == 'd') {
            if (_cursor.Check('.') && _cursor.MatchNext(RuneOps.IsDecDigit)) {
                radix = 'f';

                Digits(isDigit, ref validUnderscores);
            }

            if (_cursor.Check('e') && _cursor.MatchNext(RuneOps.IsNumberStart)) {
                radix = 'f';

                _ = _cursor.Match(RuneOps.IsSign);
                Digits(isDigit, ref validUnderscores);
            }
        }

        var name = radix == 'f' ? "float" : "integer";

        var suffixMark = _cursor.NewMark();
        var hasNameSuffix = _cursor.NextWhile(RuneOps.IsNameChar);

        var loc = _cursor.Locate(mark);

        if (hasNameSuffix) {
            var firstSuffix = _cursor.Text(suffixMark).EnumerateRunes().First();

            var hint = firstSuffix switch {
                Rune('X') => "did you mean to use '0x' instead?",
                Rune('B') => "did you mean to use '0b' instead?",
                Rune('-') => "did you mean to put a space after the number?",
                Rune('e') => "are you missing a float exponent?",
                Rune('E') => "did you mean to use 'e' instead?",
                _         => null,
            };

            var message =
                RuneOps.IsDecDigit(firstSuffix)
                    ? $"trailing digit of incorrect base in {name}"
                    : $"trailing name character in {name}";

            return Token.Invalid(
                hint is not null ? $"{message}; {hint}" : message,
                loc
            );
        }

        if (!validUnderscores) {
            return Token.Invalid(
                $"invalid underscore placement in {name}; underscores must be followed by a digit",
                loc
            );
        }

        var text  = _cursor.Text(mark);
        return radix switch {
            'f' => Token.Float(text, loc),
            'x' => Token.HexInteger(text.Replace("0x", "", StringComparison.Ordinal), loc),
            'b' => Token.BinInteger(text.Replace("0b", "", StringComparison.Ordinal), loc),
            _   => Token.DecInteger(text, loc),
        };
    }

    private (Predicate<Rune>, char) Radix()
    {
        if (_cursor.Match('0') && !_cursor.IsEmpty) {
            switch (_cursor.Peek) {
                case Rune('x'): _cursor.Next(); return (RuneOps.IsHexDigit, 'x');
                case Rune('b'): _cursor.Next(); return (RuneOps.IsBinDigit, 'b');
                default: break;
            }
        }

        return (RuneOps.IsDecDigit, 'd');
    }

    private void Digits(Predicate<Rune> isDigit, ref bool validUnderscores)
    {
        _ = _cursor.NextWhile(isDigit);

        while (_cursor.Match('_')) {
            if (!_cursor.NextWhile(isDigit)) {
                validUnderscores = false;
            }
        }
    }

    private Token NextName()
    {
        Debug.Assert(_cursor.Check(RuneOps.IsNameContinue));

        var mark = _cursor.NewMark();
        _ = _cursor.NextWhile(RuneOps.IsNameContinue);

        var validHyphens = true;
        while (_cursor.Match(RuneOps.IsNameMedial)) {
            if (!_cursor.NextWhile(RuneOps.IsNameContinue)) {
                validHyphens = false;
            }
        }

        var hasFinal = _cursor.Match(RuneOps.IsNameFinal);
        var hasTrailing = hasFinal && _cursor.NextWhile(RuneOps.IsNameChar);

        var loc = _cursor.Locate(mark);

        if (hasTrailing) {
            return Token.Invalid(
                "trailing characters after name final; did you mean to put a space after the name?",
                loc
            );
        }

        if (!validHyphens) {
            return Token.Invalid(
                "invalid hyphen placement in name; hyphens must be followed by a name character",
                loc
            );
        }

        var value = _cursor.Text(mark).Normalize(NormalizationForm.FormC);
        return TokenTag.GetKeyword(value) is {} kw
            ? Token.Keyword(kw, loc)
            : Token.Name(value, loc);
    }

    private Token NextStringStd()
    {
        Debug.Assert(_cursor.Check(IsQuote));

        var mark = _cursor.NewMark();
        _cursor.Next();

        string? content = null;
        List<string> invalidEscapes = [];
        while (_cursor.CheckNot(IsEnd)) {
            var contentMark = _cursor.NewMark();
            _ = _cursor.NextUntil(IsBreak);

            var consumedText = _cursor.Text(contentMark);
            if (content == null) {
                content = consumedText;
            }
            else {
                content += consumedText;
            }

            if (_cursor.Match(IsEscape)) {
                if (_cursor.IsEmpty) {
                    break;
                }

                if (NextEscapeSequence(ref invalidEscapes) is {} escape) {
                    content += escape;
                }
            }
        }

        if (!_cursor.Match(IsQuote)) {
            var message = _cursor.Check(RuneOps.IsNewline)
                ? @"newline in string literal; did you mean to place a '\' before the newline to" +
                      " form a line continuation?"
                : "unclosed string literal";

            return Token.Invalid(message, _cursor.Locate(mark));
        }

        var loc = _cursor.Locate(mark);

        if (invalidEscapes.Count > 0) {
            var invalidEscapeStr = invalidEscapes.Select(s => $@"\{s}").JoinToString(", ", "", "");
            var message = invalidEscapes.Count == 1
                ? $"1 invalid escape sequence: {invalidEscapeStr}"
                : $"{invalidEscapes.Count} invalid escape sequences: {invalidEscapeStr}";
            return Token.Invalid(message, loc);
        }

        return Token.String(content ?? "", loc);

        static bool IsQuote(Rune rune) => rune is Rune('"');
        static bool IsEnd(Rune rune) => IsQuote(rune) || RuneOps.IsNewline(rune);
        static bool IsEscape(Rune rune) => rune is Rune('\\');
        static bool IsBreak(Rune rune) => IsEscape(rune) || IsEnd(rune);
    }

    private string? NextEscapeSequence(ref List<string> invalidEscapes)
    {
        var mark = _cursor.NewMark();
        var escape = _cursor.Peek;
        _cursor.Next();

        switch (escape) {
            case Rune('"'):  return "\"";
            case Rune('\\'): return "\\";
            case Rune('e'):  return "\e";
            case Rune('n'):  return "\n";
            case Rune('r'):  return "\r";
            case Rune('t'):  return "\t";

            case Rune('x'): {
                var hexMark = _cursor.NewMark();
                var digits = _cursor.MatchSeq(2, RuneOps.IsHexDigit);
                var hex = _cursor.Text(hexMark);

                if (digits && Rune.ParseHex(hex) is {} rune) {
                    return rune.ToString();
                }

                break;
            }

            case Rune('u') when _cursor.Match('{'): {
                var hexMark = _cursor.NewMark();
                _ = _cursor.MatchSeq(6, RuneOps.IsHexDigit);
                var hex = _cursor.Text(hexMark);
                var enclosed = _cursor.Match('}');

                if (enclosed && Rune.ParseHex(hex) is {} rune) {
                    return rune.ToString();
                }

                break;
            }

            case Rune('u'): {
                var hexMark = _cursor.NewMark();
                var digits = _cursor.MatchSeq(4, RuneOps.IsHexDigit);
                var hex = _cursor.Text(hexMark);

                if (digits && Rune.ParseHex(hex) is {} rune) {
                    return rune.ToString();
                }

                break;
            }

            case Rune('\n' or '\r'): {
                _ = _cursor.NextWhile(RuneOps.IsIgnorable);
                return "";
            }

            default: break;
        }

        invalidEscapes.Add(_cursor.Text(mark));
        return null;
    }

    private Token NextStringRaw()
    {
        Debug.Assert(_cursor.Check(IsQuote));

        var mark = _cursor.NewMark();
        _cursor.Next();

        var contentMark = _cursor.NewMark();
        _ = _cursor.NextUntil(IsEnd);
        var content = _cursor.Text(contentMark);

        if (!_cursor.Match(IsQuote)) {
            var message = _cursor.Check(RuneOps.IsNewline)
                ? "newline in raw string literal"
                : "unclosed raw string literal";

            return Token.Invalid(message, _cursor.Locate(mark));
        }

        var loc = _cursor.Locate(mark);
        return Token.String(content, loc);

        static bool IsQuote(Rune rune) => rune is Rune('`');
        static bool IsEnd(Rune rune) => IsQuote(rune) || RuneOps.IsNewline(rune);
    }

    private Token NextPunctuation()
    {
        Debug.Assert(_cursor.Check(RuneOps.IsPunctuation));

        var mark = _cursor.NewMark();
        var c = _cursor.Peek;
        _cursor.Next();

        TokenTag? tag = c switch {
            Rune('(')  => TokenTag.PParenOpen,
            Rune(')')  => TokenTag.PParenClose,
            Rune('[')  => TokenTag.PBracketOpen,
            Rune(']')  => TokenTag.PBracketClose,
            Rune('{')  => TokenTag.PBraceOpen,
            Rune('}')  => TokenTag.PBraceClose,
            Rune('*')  => TokenTag.PStar,
            Rune('\\') => TokenTag.PBackslash,
            Rune('%')  => TokenTag.PPercent,
            Rune('^')  => TokenTag.PCaret,
            Rune('+')  => TokenTag.PPlus,
            Rune('-')  => TokenTag.PMinus,
            Rune(',')  => TokenTag.PComma,
            Rune(':')  => TokenTag.PColon,
            Rune('.')  => TokenTag.PDot,
            Rune('/')  => TokenTag.PSlash,

            // ! !=
            Rune('!') => _cursor.Match('=') ? TokenTag.PBangEqual : TokenTag.PBang,

            // & &&
            Rune('&') => _cursor.Match('&') ? TokenTag.PAndAnd : null,

            // | ||
            Rune('|') => _cursor.Match('|') ? TokenTag.POrOr : TokenTag.POr,

            // < <=
            Rune('<') => _cursor.Match('=') ? TokenTag.PLessEqual : TokenTag.PLess,

            // = ==
            Rune('=') => _cursor.Match('=') ? TokenTag.PEqualEqual : TokenTag.PEqual,

            // > >=
            Rune('>') => _cursor.Match('=') ? TokenTag.PGreaterEqual : TokenTag.PGreater,

            _ => null,
        };

        var loc = _cursor.Locate(mark);

        return tag is {} it
            ? Token.Punctuation(it, loc)
            : Token.Invalid($"invalid punctuation '{Rune.Escape(c)}'", loc);
    }

    private Token NextInvalid()
    {
        var mark = _cursor.NewMark();
        var c = _cursor.Peek;
        _cursor.Next();

        var message = (c, Rune.GetUnicodeCategory(c)) switch {
            (Rune('\u0085' or '\u2028' or '\u2029'), _) =>
                $"unexpected newline character {Rune.Escape(c)}; " +
                @"only LF \n, CRLF \r\n, or CR \r are allowed",

            (Rune('\f' or '\v'), _) or (_, UnicodeCategory.SpaceSeparator) =>
                $"unexpected whitespace character {Rune.Escape(c)}; only space and tab are allowed",

            (_, UnicodeCategory.Control) =>
                $"unexpected control character {Rune.Escape(c)}",

            (_, UnicodeCategory.Surrogate) =>
                $"unexpected surrogate {Rune.Escape(c)}",

            _ => $"unexpected character {Rune.Escape(c)}",
        };

        return Token.Invalid(message, _cursor.Locate(mark));
    }

    #region Enumerable implementation

    public IEnumerator<Token> GetEnumerator()
    {
        Token token;
        do {
            token = Next();
            yield return token;
        } while (token.Tag != TokenTag.End);
    }

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    #endregion
}

internal struct Cursor(Source source)
{
    public readonly record struct Mark(int Position);

    private int _index = 0;

    public readonly bool IsEmpty => _index >= source.Length;

    public readonly Rune Peek {
        get {
            Debug.Assert(!IsEmpty, "cannot peek past the end");
            return Rune.TryGetRuneAt(source.Body, _index, out var c) ? c : Rune.ReplacementChar;
        }
    }

    public void Next()
    {
        Debug.Assert(!IsEmpty, "cannot advance past the end");
        _index += Peek.Utf16SequenceLength;
    }

    [Pure]
    public readonly bool Check([RequireStaticDelegate] Predicate<Rune> predicate) =>
        !IsEmpty && predicate(Peek);

    [Pure]
    public readonly bool Check(char expected) => !IsEmpty && Peek.Value == expected;

    [Pure]
    public readonly bool Check(string expected)
    {
        var end = _index + expected.Length;
        return end <= source.Length && source[_index .. end].SequenceEqual(expected);
    }

    [Pure]
    public readonly bool CheckNot([RequireStaticDelegate] Predicate<Rune> predicate) =>
        !(IsEmpty || predicate(Peek));

    [MustUseReturnValue]
    public bool Match([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        if (Check(predicate)) {
            Next();
            return true;
        }
        else {
            return false;
        }
    }

    [MustUseReturnValue]
    public bool Match(char expected)
    {
        if (Check(expected)) {
            ++_index;
            return true;
        }
        else {
            return false;
        }
    }

    [MustUseReturnValue]
    public bool Match(string expected)
    {
        if (Check(expected)) {
            _index += expected.Length;
            return true;
        }
        else {
            return false;
        }
    }

    [MustUseReturnValue]
    public bool MatchSeq(int count, [RequireStaticDelegate] Predicate<Rune> predicate)
    {
        for (var i = 0; i < count; ++i) {
            if (!Match(predicate)) {
                return false;
            }
        }

        return true;
    }

    [MustUseReturnValue]
    public bool MatchNext([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        var next = _index + Peek.Utf16SequenceLength;
        var matched =
            next < source.Length &&
            Rune.TryGetRuneAt(source.Body, next, out var c) &&
            predicate(c);

        if (matched) {
            _index = next;
        }

        return matched;
    }

    [MustUseReturnValue]
    public bool NextWhile([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        var accepted = false;

        while (Match(predicate)) {
            accepted = true;
        }

        return accepted;
    }

    [MustUseReturnValue]
    public bool NextUntil([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        while (!IsEmpty) {
            if (Check(predicate)) {
                return true;
            }
            else {
                Next();
            }
        }

        return false;
    }

    [Pure]
    public readonly Mark NewMark()
    {
        return new Mark(_index);
    }

    [Pure]
    public readonly Loc Locate() => Loc.FromLength(_index, 0);

    [Pure]
    public readonly Loc Locate(Mark mark) => Loc.FromRange(mark.Position, _index);

    [Pure]
    public readonly string Text(Mark mark) => source.Body[mark.Position .. _index];
}
