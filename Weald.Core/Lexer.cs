using System.Collections;
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
                State.Start      => NextStart(out _state),
                State.Tokenising => NextTokenising(startMark, out _state),
                State.End        => NextEnd(),
            };

            if (result is {} token) return token;
        }
    }

    private Token? NextStart(out State state)
    {
        SkipBom();
        SkipHashbang();
        state = State.Tokenising;
        return null;
    }

    private void SkipBom()
    {
        _ = _cursor.Match('\uFEFF');
    }

    private void SkipHashbang()
    {
        if (_cursor.Match("#!")) {
            _cursor.NextUntil(RuneOps.IsNewline);
        }
    }

    private readonly Token NextEnd()
    {
        return Token.End(_cursor.Locate());
    }

    private Token? NextTokenising(Cursor.Mark startMark, out State state)
    {
        if (_cursor.IsEmpty) {
            state = State.End;
            return null;
        }

        state = State.Tokenising;
        var c = _cursor.Peek;

        if (RuneOps.IsWhitespace(c)) {
            return NextWhitespace();
        }
        else if (RuneOps.IsNewline(c)) {
            return NextNewlineOrComment(startMark);
        }
        else if (_cursor.Match("--")) {
            return NextComment(startMark);
        }

        var tokenMark = _cursor.NewMark();

        if (RuneOps.IsPunctuation(c)) {
            return NextPunctuation(tokenMark);
        }

        _cursor.Next();
        return Token.Invalid($"unexpected character {Rune.Escape(c)}", _cursor.Locate(tokenMark));
    }

    private Token? NextWhitespace()
    {
        _cursor.NextWhile(RuneOps.IsWhitespace);
        return null;
    }

    private Token? NextNewlineOrComment(Cursor.Mark startMark)
    {
        Debug.Assert(_cursor.IsEmpty || RuneOps.IsNewline(_cursor.Peek));

        while (!_cursor.IsEmpty) {
            _cursor.NextWhile(RuneOps.IsIgnorable);

            if (_cursor.Match("--")) {
                _cursor.NextUntil(RuneOps.IsNewline);
                continue;
            }

            return Token.Newline(_cursor.Locate(startMark));
        }

        return null;
    }

    private Token? NextComment(Cursor.Mark startMark)
    {
        _cursor.NextUntil(RuneOps.IsNewline);
        return NextNewlineOrComment(startMark);
    }

    private Token NextPunctuation(Cursor.Mark tokenMark)
    {
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

        var loc = _cursor.Locate(tokenMark);

        return tag is {} it
            ? Token.Punctuation(it, loc)
            : Token.Invalid("invalid punctuation", loc);
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

internal struct Cursor
{
    private readonly Source _source;
    private int _index = 0;

    public Cursor(Source source)
    {
        _source = source;
    }

    public readonly bool IsEmpty => _index >= _source.Length;

    public readonly Rune Peek {
        get {
            Debug.Assert(!IsEmpty, "cannot peek past the end");
            return Rune.TryGetRuneAt(_source.Body, _index, out var c) ? c : Rune.ReplacementChar;
        }
    }

    [Pure]
    public readonly Rune? PeekNext()
    {
        var next = _index + Peek.Utf16SequenceLength;
        if (next >= _source.Length) return null;

        if (Rune.TryGetRuneAt(_source.Body, next, out var c)) {
            return c;
        }
        else {
            return null;
        }
    }

    public void Next()
    {
        Debug.Assert(!IsEmpty, "cannot advance past the end");
        _index += Peek.Utf16SequenceLength;
    }

    public void NextWhile([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        while (!IsEmpty && predicate(Peek)) {
            Next();
        }
    }

    public void NextUntil([RequireStaticDelegate] Predicate<Rune> predicate)
    {
        while (!IsEmpty && !predicate(Peek)) {
            Next();
        }
    }

    [MustUseReturnValue]
    public bool Match(char expected)
    {
        if (!IsEmpty && Peek.Value == expected) {
            Next();
            return true;
        }
        else {
            return false;
        }
    }

    [MustUseReturnValue]
    public bool Match(string expected)
    {
        var end = _index + expected.Length;
        if (end <= _source.Length && _source[_index .. end].SequenceEqual(expected)) {
            _index += expected.Length;
            return true;
        }
        else {
            return false;
        }
    }

    public Mark NewMark()
    {
        return new Mark(_index);
    }

    public readonly record struct Mark(int Position);

    public readonly Loc Locate() => Loc.FromLength(_index, 0);

    public readonly Loc Locate(Mark mark) => Loc.FromRange(mark.Position, _index);
}
