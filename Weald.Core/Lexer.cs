using System.Collections.Immutable;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using static Weald.Core.TokenTag;

namespace Weald.Core;

public static class Lexer
{
    [Pure]
    public static ImmutableArray<Token> Tokenise(Source source)
    {
        var tokens = ImmutableArray.CreateBuilder<Token>();
        new Core(source, tokens).Tokenise();
        return tokens.DrainToImmutable();
    }
}

file struct Core(Source source, ImmutableArray<Token>.Builder tokens)
{
    private Cursor _cursor = new(source);

    public void Tokenise()
    {
        SkipBom();
        SkipHashbang();

        EmitTokens();

        EmitEnd();
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

    private void EmitTokens()
    {
        while (true) {
            EmitIgnorable();

            if (_cursor.IsEmpty) break;

            var mark = _cursor.NewMark();
            var c = _cursor.Peek;
            switch (c.Value) {
                case '_' or >= 'A' and <= 'Z' or >= 'a' and <= 'z': {
                    EmitName();
                    break;
                }

                case >= '0' and <= '9':
                case '+' or '-' when _cursor.CheckNext(RuneOps.IsDecDigit): {
                    EmitNumber();
                    break;
                }

                case '"': EmitStringStd(); break;
                case '`': EmitStringRaw(); break;

                #region Punctuation

                case '(':  EmitPunct(PParenOpen, mark); break;
                case ')':  EmitPunct(PParenClose, mark); break;
                case '[':  EmitPunct(PBracketOpen, mark); break;
                case ']':  EmitPunct(PBracketClose, mark); break;
                case '{':  EmitPunct(PBraceOpen, mark); break;
                case '}':  EmitPunct(PBraceClose, mark); break;
                case '*':  EmitPunct(PStar, mark); break;
                case '\\': EmitPunct(PBackslash, mark); break;
                case '%':  EmitPunct(PPercent, mark); break;
                case '^':  EmitPunct(PCaret, mark); break;
                case '+':  EmitPunct(PPlus, mark); break;
                case '-':  EmitPunct(PMinus, mark); break;
                case ',':  EmitPunct(PComma, mark); break;
                case ':':  EmitPunct(PColon, mark); break;
                case '.':  EmitPunct(PDot, mark); break;
                case '/':  EmitPunct(PSlash, mark); break;
                // ! !=
                case '!': EmitPunct('=', PBangEqual, PBang, mark); break;
                // & &&
                case '&': EmitPunct('&', PAndAnd, PAnd, mark); break;
                // | ||
                case '|': EmitPunct('|', POrOr, POr, mark); break;
                // < <=
                case '<': EmitPunct('=', PLessEqual, PLess, mark); break;
                // = ==
                case '=': EmitPunct('=', PEqualEqual, PEqual, mark); break;
                // > >=
                case '>': EmitPunct('=', PGreaterEqual, PGreater, mark); break;

                #endregion Punctuation

                default: {
                    if (RuneOps.IsNameStart(c)) {
                        EmitName();
                    }
                    else {
                        EmitInvalid();
                    }

                    break;
                }
            }
        }
    }

    private void EmitIgnorable()
    {
        var mark = _cursor.NewMark();
        var hadNewline = false;
        while (!_cursor.IsEmpty) {
            switch (_cursor.Peek) {
                case Rune(' ' or '\t' or '\u200E' or '\u200F'): {
                    _cursor.Next();
                    break;
                }

                case Rune('\n' or '\r'): {
                    hadNewline = true;
                    _cursor.Next();
                    break;
                }

                case Rune('-') when _cursor.Match("--"): {
                    _ = _cursor.NextUntil(RuneOps.IsNewline, OnForbiddenInComment);
                    break;
                }

                default: goto end;
            }
        }

    end:
        if (hadNewline) {
            Emit(Token.Newline(_cursor.Locate(mark)));
        }
    }

    private void OnForbiddenInComment(Rune rune, Loc loc)
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

        Emit(Token.Invalid(message, loc));
    }

    private void EmitNumber()
    {
        Debug.Assert(_cursor.Check(RuneOps.IsNumberStart));

        var mark = _cursor.NewMark();
        if (_cursor.Match(RuneOps.IsSign)) {
            Debug.Assert(_cursor.Check(RuneOps.IsDecDigit));
        }

        var isInvalid = false;

        var (isDigit, radix) = Radix();
        Digits(isDigit, ref isInvalid);

        if (radix == 'd') {
            if (_cursor.Check('.') && _cursor.MatchNext(RuneOps.IsDecDigit)) {
                radix = 'f';

                Digits(isDigit, ref isInvalid);
            }

            if (_cursor.Check('e') && _cursor.MatchNext(RuneOps.IsNumberStart)) {
                radix = 'f';

                _ = _cursor.Match(RuneOps.IsSign);
                Digits(isDigit, ref isInvalid);
            }
        }

        var name = radix == 'f' ? "float" : "integer";

        var suffixMark = _cursor.NewMark();
        var hasNameSuffix = _cursor.NextWhile(RuneOps.IsNameChar);

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

            isInvalid = true;
            Emit(
                Token.Invalid(
                    hint is not null ? $"{message}; {hint}" : message,
                    _cursor.Locate(suffixMark)
                )
            );
        }

        if (isInvalid) {
            return;
        }

        var text = _cursor.Text(mark);
        var loc = _cursor.Locate(mark);
        var token = radix switch {
            'f' => Token.Float(text, loc),
            _   => Token.Integer(text, loc),
        };
        Emit(token);
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

    private void Digits(Predicate<Rune> isDigit, ref bool isInvalid)
    {
        _ = _cursor.NextWhile(isDigit);

        while (_cursor.Match('_')) {
            var digitMark = _cursor.NewMark();
            if (!_cursor.NextWhile(isDigit)) {
                isInvalid = true;
                Emit(
                    Token.Invalid(
                        "invalid underscore placement; underscores must be followed by a digit",
                        _cursor.Locate(digitMark)
                    )
                );
            }
        }
    }

    private void EmitName()
    {
        Debug.Assert(_cursor.Check(RuneOps.IsNameContinue));

        var mark = _cursor.NewMark();
        _ = _cursor.NextWhile(RuneOps.IsNameContinue);

        var isInvalid = false;
        while (_cursor.Match(RuneOps.IsNameMedial)) {
            var continueMark = _cursor.NewMark();
            if (!_cursor.NextWhile(RuneOps.IsNameContinue)) {
                isInvalid = true;
                Emit(
                    Token.Invalid(
                        "invalid hyphen placement; hyphens must be followed by a name character",
                        _cursor.Locate(continueMark)
                    )
                );
            }
        }

        var hasFinal = _cursor.Match(RuneOps.IsNameFinal);

        var trailingMark = _cursor.NewMark();
        if (hasFinal && _cursor.NextWhile(RuneOps.IsNameChar)) {
            isInvalid = true;
            Emit(
                Token.Invalid(
                    "trailing characters after name final; did you mean to put a space after the name?",
                    _cursor.Locate(trailingMark)
                )
            );
        }

        var bidiMark = _cursor.NewMark();
        if (_cursor.Check(RuneOps.IsBidiMark) && _cursor.MatchNext(RuneOps.IsNameChar)) {
            isInvalid = true;
            Emit(
                Token.Invalid(
                    "embedded bidirectional mark in name; spaces must be used to separate names",
                    _cursor.Locate(bidiMark)
                )
            );
        }

        if (isInvalid) {
            return;
        }

        var loc = _cursor.Locate(mark);

        var value = _cursor.Text(mark).Normalize(NormalizationForm.FormC);
        var token = TokenTag.GetKeyword(value) is {} kw
            ? Token.Keyword(kw, loc)
            : Token.Name(value, loc);
        Emit(token);
    }

    private void EmitStringStd()
    {
        if (_cursor.Check("\"\"\"")) {
            EmitStringStdBlock();
        }
        else {
            EmitStringStdLine();
        }
    }

    private void EmitStringStdLine()
    {
        Debug.Assert(_cursor.Check('"'));

        var mark = _cursor.NewMark();
        _cursor.Next();

        var isInvalid = false;
        while (_cursor.CheckNot(static rune => rune is Rune('"') || RuneOps.IsNewline(rune))) {
            var stopReason = _cursor.NextUntil(
                static rune => rune is Rune('\\' or '"') || RuneOps.IsNewline(rune),
                OnForbiddenInStdString
            );
            if (stopReason == Cursor.StopReason.Forbidden) {
                isInvalid = true;
            }

            if (_cursor.Match('\\')) {
                if (_cursor.IsEmpty) {
                    break;
                }
                else if (_cursor.Check(RuneOps.IsNewline)) {
                    SkipSingleNewline();
                }
                else {
                    _cursor.Next();
                }
            }
        }

        if (!_cursor.Match('"')) {
            isInvalid = true;

            var message = _cursor.Check(RuneOps.IsNewline)
                ? @"newline in string literal; did you mean to place a '\' before the newline to" +
                      " form a line continuation?"
                : "unclosed string literal";
            Emit(Token.Invalid(message, _cursor.Locate(mark)));
        }

        if (isInvalid) {
            return;
        }

        var text = _cursor.Text(mark);
        var loc = _cursor.Locate(mark);
        Emit(Token.String(text, loc));
    }

    private void SkipSingleNewline()
    {
        if (_cursor.Match('\r')) {
            _ = _cursor.Match('\n');
        }
        else {
            Debug.Assert(_cursor.Check('\n'));
            _cursor.Next();
        }
    }

    private void EmitStringStdBlock()
    {
        const string triQuotes = "\"\"\"";

        Debug.Assert(_cursor.Check(triQuotes));

        var mark = _cursor.NewMark();
        _cursor.NextSeq(3);

        var isInvalid = false;
        while (_cursor.CheckNot(triQuotes)) {
            var stopReason = _cursor.NextUntil(
                static rune => rune is Rune('\\' or '"'),
                OnForbiddenInStdString
            );
            if (stopReason == Cursor.StopReason.Forbidden) {
                isInvalid = true;
            }

            if (_cursor.Match('\\')) {
                if (_cursor.IsEmpty) {
                    break;
                }
                else {
                    _cursor.Next();
                }
            }
            else if (_cursor.IsEmpty || _cursor.Check(triQuotes)) {
                break;
            }
            else {
                Debug.Assert(_cursor.Check('"'));
                _cursor.Next();
            }
        }

        if (!_cursor.Match(triQuotes)) {
            isInvalid = true;
            Emit(Token.Invalid("unclosed block string literal", _cursor.Locate(mark)));
        }

        if (isInvalid) {
            return;
        }

        var text = _cursor.Text(mark);
        var loc = _cursor.Locate(mark);
        Emit(Token.String(text, loc));
    }

    private void OnForbiddenInStdString(Rune rune, Loc loc)
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

        Emit(Token.Invalid(message, loc));
    }

    private void EmitStringRaw()
    {
        if (_cursor.Check("```")) {
            EmitStringRawBlock();
        }
        else {
            EmitStringRawLine();
        }
    }

    private void EmitStringRawLine()
    {
        Debug.Assert(_cursor.Check('`'));

        var mark = _cursor.NewMark();
        _cursor.Next();

        var stopReason = _cursor.NextUntil(
            static rune => rune is Rune('`') || RuneOps.IsNewline(rune),
            OnForbiddenInRawString
        );

        if (!_cursor.Match('`')) {
            var message = _cursor.Check(RuneOps.IsNewline)
                ? "newline in raw string literal"
                : "unclosed raw string literal";

            Emit(Token.Invalid(message, _cursor.Locate(mark)));
            return;
        }

        if (stopReason == Cursor.StopReason.Forbidden) {
            return;
        }

        var text = _cursor.Text(mark);
        var loc = _cursor.Locate(mark);
        Emit(Token.String(text, loc));
    }

    private void EmitStringRawBlock()
    {
        const string triQuotes = "```";

        Debug.Assert(_cursor.Check(triQuotes));

        var mark = _cursor.NewMark();
        _cursor.NextSeq(3);

        var isInvalid = false;
        var stopReason = _cursor.NextUntil(triQuotes, OnForbiddenInRawString);
        if (stopReason == Cursor.StopReason.Forbidden) {
            isInvalid = true;
        }

        if (!_cursor.Match(triQuotes)) {
            isInvalid = true;
            Emit(
                Token.Invalid("unclosed raw block string literal", _cursor.Locate(mark))
            );
        }

        if (isInvalid) {
            return;
        }

        var text = _cursor.Text(mark);
        var loc = _cursor.Locate(mark);
        Emit(Token.String(text, loc));
    }

    private void OnForbiddenInRawString(Rune rune, Loc loc)
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

        Emit(Token.Invalid(message, loc));
    }

    private void EmitPunct(TokenTag tag, Cursor.Mark mark)
    {
        _cursor.Next();
        Emit(Token.Punctuation(tag, _cursor.Locate(mark)));
    }

    private void EmitPunct(char secondChar, TokenTag ifMatch, TokenTag ifNot, Cursor.Mark mark)
    {
        _cursor.Next();
        var token = _cursor.Match(secondChar)
            ? Token.Punctuation(ifMatch, _cursor.Locate(mark))
            : Token.Punctuation(ifNot, _cursor.Locate(mark));
        Emit(token);
    }

    private void EmitInvalid()
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

            _ when RuneOps.IsPunctuation(c) => $"invalid punctuation '{c}'",

            _ => $"unexpected character {Rune.Escape(c)}",
        };

        Emit(Token.Invalid(message, _cursor.Locate(mark)));
    }

    private void EmitEnd()
    {
        Emit(Token.End(_cursor.Locate()));
    }

    [System.Diagnostics.CodeAnalysis.SuppressMessage("Style", "IDE0251:Make member \'readonly\'")]
    private void Emit(Token token)
    {
        tokens.Add(token);
    }
}

file struct Cursor(Source source)
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

    [Pure]
    public readonly bool CheckNext([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        var next = _index + Peek.Utf16SequenceLength;
        return
            next < source.Length &&
            Rune.TryGetRuneAt(source.Body, next, out var c) &&
            predicate(c);
    }

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

    [MustUseReturnValue]
    public StopReason NextUntil(string expected, Action<Rune, Loc> onForbidden)
    {
        var stopReason = StopReason.Empty;
        while (!IsEmpty) {
            if (Check(expected)) {
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
