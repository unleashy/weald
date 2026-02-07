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
        while (true) {
            _cursor.Sync();

            var result = _state switch {
                State.Start      => NextStart(out _state),
                State.Tokenising => NextTokenising(out _state),
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
        return Token.End(_cursor.Loc);
    }

    private Token? NextTokenising(out State state)
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
            return NextNewlineOrComment();
        }
        else if (_cursor.Match("--")) {
            return NextComment();
        }
        else if (RuneOps.IsPunctuation(c)) {
            return NextPunctuation();
        }

        _cursor.Next();
        return Token.Invalid($"unexpected character {Rune.Escape(c)}", _cursor.Loc);
    }

    private Token? NextWhitespace()
    {
        _cursor.NextWhile(RuneOps.IsWhitespace);
        return null;
    }

    private Token? NextNewlineOrComment()
    {
        Debug.Assert(_cursor.IsEmpty || RuneOps.IsNewline(_cursor.Peek));

        while (!_cursor.IsEmpty) {
            _cursor.NextWhile(RuneOps.IsIgnorable);

            if (_cursor.Match("--")) {
                _cursor.NextUntil(RuneOps.IsNewline);
                continue;
            }

            return Token.Newline(_cursor.Loc);
        }

        return null;
    }

    private Token? NextComment()
    {
        _cursor.NextUntil(RuneOps.IsNewline);
        return NextNewlineOrComment();
    }

    private Token NextPunctuation()
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

        return tag is {} it
            ? Token.Punctuation(it, _cursor.Loc)
            : Token.Invalid("invalid punctuation", _cursor.Loc);
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
    private int _start = 0;
    private int _now = 0;

    public Cursor(Source source)
    {
        _source = source;
    }

    public readonly bool IsEmpty => _now >= _source.Length;

    public readonly Rune Peek {
        get {
            Debug.Assert(!IsEmpty, "cannot peek past the end");
            return Rune.TryGetRuneAt(_source.Body, _now, out var c) ? c : Rune.ReplacementChar;
        }
    }

    [Pure]
    public readonly Rune? PeekNext()
    {
        var next = _now + Peek.Utf16SequenceLength;
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
        _now += Peek.Utf16SequenceLength;
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
        var end = _now + expected.Length;
        if (end <= _source.Length && _source[_now .. end].SequenceEqual(expected)) {
            _now += expected.Length;
            return true;
        }
        else {
            return false;
        }
    }

    public void Sync()
    {
        _start = _now;
    }

    public readonly Loc Loc => Loc.FromRange(_start, _now);
}
