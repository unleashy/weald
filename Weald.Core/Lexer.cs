using System.Collections;
using System.Globalization;
using System.Runtime.CompilerServices;
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
    private Queue<Token> _queue = [];

    public Token Next()
    {
        var startMark = _cursor.NewMark();

        while (true) {
            if (_queue.Count > 0) {
                return _queue.Dequeue();
            }

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
            _ = _cursor.NextUntil(RuneOps.IsNewline, OnForbiddenInComment);
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
                _ = _cursor.NextUntil(RuneOps.IsNewline, OnForbiddenInComment);
                continue;
            }

            return Token.Newline(_cursor.Locate(startMark));
        }

        return null;
    }

    private Token? NextComment(Cursor.Mark startMark)
    {
        _ = _cursor.NextUntil(RuneOps.IsNewline, OnForbiddenInComment);
        return NextNewlineOrComment(startMark);
    }

    private readonly void OnForbiddenInComment(Rune rune, Loc loc)
    {
        var message = (rune, Rune.GetUnicodeCategory(rune)) switch {
            (Rune('\u2028' or '\u2029'), _) =>
                $"forbidden newline character {Rune.Escape(rune)} in comment " +
                    @"only LF \n, CRLF \r\n, or CR \r are allowed",

            (_, UnicodeCategory.Control) =>
                $"forbidden control character {Rune.Escape(rune)} in comment",

            (_, UnicodeCategory.Surrogate) =>
                "surrogate in comment; maybe the source file is not UTF-8?",

            _ => throw new SwitchExpressionException(rune),
        };

        _queue.Enqueue(Token.Invalid(message, loc));
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

    private Token? NextStringStd() =>
        _cursor.Check("\"\"\"")
            ? NextStringStdBlock()
            : NextStringStdLine();

    private Token? NextStringStdLine()
    {
        Debug.Assert(_cursor.Check(IsQuote));

        var mark = _cursor.NewMark();
        _cursor.Next();

        string? content = null;
        var isInvalid = false;
        while (_cursor.CheckNot(IsEnd)) {
            var contentMark = _cursor.NewMark();
            var stopReason = _cursor.NextUntil(IsBreak, OnForbiddenInStdString);
            if (stopReason == Cursor.StopReason.Forbidden) {
                isInvalid = true;
            }

            var consumedText = _cursor.Text(contentMark);
            if (content == null) {
                content = consumedText;
            }
            else {
                content += consumedText;
            }

            if (_cursor.Check(IsEscape)) {
                if (NextEscapeSequence() is {} escape) {
                    content += escape;
                }
                else {
                    isInvalid = true;
                }
            }
        }

        if (!_cursor.Match(IsQuote)) {
            var message = _cursor.Check(RuneOps.IsNewline)
                ? @"newline in string literal; did you mean to place a '\' before the newline to" +
                      " form a line continuation?"
                : "unclosed string literal";

            _queue.Enqueue(Token.Invalid(message, _cursor.Locate(mark)));
            return null;
        }

        if (isInvalid) {
            return null;
        }

        var loc = _cursor.Locate(mark);
        return Token.String(content ?? "", loc);

        static bool IsQuote(Rune rune) => rune is Rune('"');
        static bool IsEnd(Rune rune) => IsQuote(rune) || RuneOps.IsNewline(rune);
        static bool IsEscape(Rune rune) => rune is Rune('\\');
        static bool IsBreak(Rune rune) => IsEscape(rune) || IsEnd(rune);
    }

    private Token? NextStringStdBlock()
    {
        const string triQuotes = "\"\"\"";

        Debug.Assert(_cursor.Check(triQuotes));

        var mark = _cursor.NewMark();
        _cursor.NextSeq(3);

        _ = _cursor.NextWhile(RuneOps.IsWhitespace);
        _ = _cursor.Match(RuneOps.IsNewline);

        var isInvalid = false;
        List<string> lines = [];
        string? commonPrefix = null;
        while (_cursor.CheckNot(triQuotes)) {
            var rawMark = _cursor.NewMark();

            var line = "";
            while (true) {
                var contentMark = _cursor.NewMark();
                var stopReason = _cursor.NextUntil(
                    static rune => RuneOps.IsNewline(rune) || IsEscape(rune) || IsQuote(rune),
                    OnForbiddenInStdString
                );
                if (stopReason == Cursor.StopReason.Forbidden) {
                    isInvalid = true;
                }

                line += _cursor.Text(contentMark);

                if (_cursor.Check(IsEscape)) {
                    if (NextEscapeSequence() is {} escape) {
                        line += escape;
                    }
                    else {
                        isInvalid = true;
                    }
                }
                else if (
                    _cursor.IsEmpty || _cursor.Check(triQuotes) || _cursor.Check(RuneOps.IsNewline)
                ) {
                    break;
                }
                else {
                    Debug.Assert(_cursor.Check(IsQuote));
                    _cursor.Next();

                    line += '"';
                }
            }

            var rawLine = _cursor.Text(rawMark);
            var prefix = rawLine.TakePrefix(c => c is ' ' or '\t');
            if (prefix == rawLine) {
                if (!_cursor.Check(triQuotes)) {
                    lines.Add("");
                }
            }
            else {
                if (
                    commonPrefix == null ||
                    commonPrefix.StartsWith(prefix, StringComparison.Ordinal)
                ) {
                    commonPrefix = prefix;
                }

                lines.Add(line);
            }

            _ = _cursor.Match(RuneOps.IsNewline);
        }

        if (!_cursor.Match(triQuotes)) {
            _queue.Enqueue(Token.Invalid("unclosed block string literal", _cursor.Locate(mark)));
            return null;
        }

        if (isInvalid) {
            return null;
        }

        if (lines.Count > 0) {
            lines[^1] = lines[^1].TrimEnd(' ', '\t');
        }

        var content =
            commonPrefix is null
                ? lines.JoinToString('\n')
                : lines.Select(line => line[commonPrefix.Length ..]).JoinToString('\n');

        var loc = _cursor.Locate(mark);
        return Token.String(content, loc);

        static bool IsQuote(Rune rune) => rune is Rune('"');
        static bool IsEscape(Rune rune) => rune is Rune('\\');
    }

    private string? NextEscapeSequence()
    {
        Debug.Assert(_cursor.Check('\\'));

        var mark = _cursor.NewMark();
        _cursor.Next();

        if (_cursor.IsEmpty) {
            return null;
        }

        var escape = _cursor.Peek;
        _cursor.Next();

        string invalidMessage;
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

                if (digits) {
                    var rune = Rune.ParseHex(hex);
                    Debug.Assert(rune is not null);
                    return rune.ToString();
                }
                else {
                    invalidMessage =
                        @"invalid \x escape sequence; expected exactly 2 hexadecimal digits";
                }

                break;
            }

            case Rune('u') when _cursor.Match('{'): {
                var hexMark = _cursor.NewMark();
                _ = _cursor.MatchSeq(6, RuneOps.IsHexDigit);
                var hex = _cursor.Text(hexMark);
                var enclosed = _cursor.Match('}');

                if (enclosed) {
                    if (Rune.ParseHex(hex) is {} rune) {
                        return rune.ToString();
                    }
                    else if (hex == "") {
                        invalidMessage = @"invalid empty \u{} escape sequence";
                    }
                    else {
                        invalidMessage =
                            $@"invalid \u{{}} escape sequence; '{hex}' is not a Unicode code point";
                    }
                }
                else {
                    invalidMessage =
                        @"unclosed \u{} escape sequence; expected a '}' after at most 6 hexadecimal digits";
                }

                break;
            }

            case Rune('u'): {
                var hexMark = _cursor.NewMark();
                var digits = _cursor.MatchSeq(4, RuneOps.IsHexDigit);
                var hex = _cursor.Text(hexMark);

                if (digits) {
                    if (Rune.ParseHex(hex) is {} rune) {
                        return rune.ToString();
                    }
                    else {
                        invalidMessage =
                            $@"invalid \u escape sequence; '{hex}' is not a Unicode code point";
                    }
                }
                else {
                    invalidMessage =
                        @"invalid \u escape sequence; expected exactly 4 hexadecimal digits";
                }

                break;
            }

            case Rune('\n' or '\r'): {
                _ = _cursor.NextWhile(RuneOps.IsIgnorable);
                return "";
            }

            default: {
                invalidMessage = $"unrecognised escape sequence '\\{Rune.Escape(escape)}'";
                break;
            }
        }

        var loc = _cursor.Locate(mark);
        _queue.Enqueue(Token.Invalid(invalidMessage, loc));
        return null;
    }

    private readonly void OnForbiddenInStdString(Rune rune, Loc loc)
    {
        var message = (rune, Rune.GetUnicodeCategory(rune)) switch {
            (Rune('\u2028' or '\u2029'), _) =>
                $"forbidden newline character {Rune.Escape(rune)} in string; escape it with" +
                $" {Rune.Escape(rune)}",

            (_, UnicodeCategory.Control) =>
                $"forbidden control character {Rune.Escape(rune)} in string; escape it with" +
                $" {Rune.Escape(rune)}",

            (_, UnicodeCategory.Surrogate) =>
                "surrogate in string; maybe the source file is not UTF-8?",

            _ => throw new SwitchExpressionException(rune),
        };

        _queue.Enqueue(Token.Invalid(message, loc));
    }

    private Token? NextStringRaw() =>
        _cursor.Check("```")
            ? NextStringRawBlock()
            : NextStringRawLine();

    private Token? NextStringRawLine()
    {
        Debug.Assert(_cursor.Check(IsQuote));

        var mark = _cursor.NewMark();
        _cursor.Next();

        var isInvalid = false;
        var contentMark = _cursor.NewMark();
        var stopReason = _cursor.NextUntil(IsEnd, OnForbiddenInRawString);
        if (stopReason == Cursor.StopReason.Forbidden) {
            isInvalid = true;
        }

        var content = _cursor.Text(contentMark);

        if (!_cursor.Match(IsQuote)) {
            var message = _cursor.Check(RuneOps.IsNewline)
                ? "newline in raw string literal"
                : "unclosed raw string literal";

            _queue.Enqueue(Token.Invalid(message, _cursor.Locate(mark)));
            return null;
        }

        if (isInvalid) {
            return null;
        }

        var loc = _cursor.Locate(mark);
        return Token.String(content, loc);

        static bool IsQuote(Rune rune) => rune is Rune('`');
        static bool IsEnd(Rune rune) => IsQuote(rune) || RuneOps.IsNewline(rune);
    }

    private Token? NextStringRawBlock()
    {
        const string triQuotes = "```";

        Debug.Assert(_cursor.Check(triQuotes));

        var mark = _cursor.NewMark();
        _cursor.NextSeq(3);

        _ = _cursor.NextWhile(RuneOps.IsWhitespace);
        _ = _cursor.Match(RuneOps.IsNewline);

        var isInvalid = false;
        List<string> lines = [];
        string? commonPrefix = null;
        while (_cursor.CheckNot(triQuotes)) {
            var rawMark = _cursor.NewMark();

            var line = "";
            while (true) {
                var contentMark = _cursor.NewMark();
                var stopReason = _cursor.NextUntil(
                    static rune => RuneOps.IsNewline(rune) || IsQuote(rune),
                    OnForbiddenInRawString
                );
                if (stopReason == Cursor.StopReason.Forbidden) {
                    isInvalid = true;
                }

                line += _cursor.Text(contentMark);

                if (
                    _cursor.IsEmpty || _cursor.Check(triQuotes) || _cursor.Check(RuneOps.IsNewline)
                ) {
                    break;
                }
                else {
                    Debug.Assert(_cursor.Check(IsQuote));
                    _cursor.Next();

                    line += '`';
                }
            }

            var rawLine = _cursor.Text(rawMark);
            var prefix = rawLine.TakePrefix(static c => c is ' ' or '\t');
            if (prefix == rawLine) {
                if (!_cursor.Check(triQuotes)) {
                    lines.Add("");
                }
            }
            else {
                if (
                    commonPrefix == null ||
                    commonPrefix.StartsWith(prefix, StringComparison.Ordinal)
                ) {
                    commonPrefix = prefix;
                }

                lines.Add(line);
            }

            _ = _cursor.Match(RuneOps.IsNewline);
        }

        if (!_cursor.Match(triQuotes)) {
            _queue.Enqueue(
                Token.Invalid("unclosed raw block string literal", _cursor.Locate(mark))
            );
            return null;
        }

        if (isInvalid) {
            return null;
        }

        if (lines.Count > 0) {
            lines[^1] = lines[^1].TrimEnd(' ', '\t');
        }

        var content =
            commonPrefix is null
                ? lines.JoinToString('\n')
                : lines.Select(line => line[commonPrefix.Length ..]).JoinToString('\n');

        var loc = _cursor.Locate(mark);
        return Token.String(content, loc);

        static bool IsQuote(Rune rune) => rune is Rune('`');
    }

    private readonly void OnForbiddenInRawString(Rune rune, Loc loc)
    {
        var message = (rune, Rune.GetUnicodeCategory(rune)) switch {
            (Rune('\u2028' or '\u2029'), _) =>
                $"forbidden newline character {Rune.Escape(rune)} in raw string",

            (_, UnicodeCategory.Control) =>
                $"forbidden control character {Rune.Escape(rune)} in raw string",

            (_, UnicodeCategory.Surrogate) =>
                "surrogate in raw string; maybe the source file is not UTF-8?",

            _ => throw new SwitchExpressionException(rune),
        };

        _queue.Enqueue(Token.Invalid(message, loc));
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
                "unexpected surrogate; maybe the source file is not UTF-8?",

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
    public enum StopReason
    {
        Matched,
        Empty,
        Forbidden,
    }

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

    public void NextSeq(int k)
    {
        Debug.Assert(k >= 1, "k must be >= 1");

        for (var i = 0; !IsEmpty && i < k; ++i) {
            Next();
        }
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
        !IsEmpty && !predicate(Peek);

    [Pure]
    public readonly bool CheckNot(string expected) => !IsEmpty && !Check(expected);

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
    public StopReason NextUntil(
        [RequireStaticDelegate] Predicate<Rune> predicate,
        Action<Rune, Loc> onForbidden
    )
    {
        var stopReason = StopReason.Empty;
        while (!IsEmpty) {
            if (Check(predicate)) {
                if (stopReason == StopReason.Empty) {
                    stopReason = StopReason.Matched;
                }

                break;
            }
            else if (Check(RuneOps.IsForbidden)) {
                var rune = Peek;
                onForbidden(rune, Loc.FromLength(_index, rune.Utf16SequenceLength));
                stopReason = StopReason.Forbidden;
            }

            Next();
        }

        return stopReason;
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
