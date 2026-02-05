using System.Collections;
using System.Text;

namespace Weald.Core;

public static class Lexer
{
    [Pure]
    public static TokenEnumerator Tokenise(Source source) => new(source);

    internal static Token NextToken(ref Cursor cursor)
    {
        if (!cursor.MoveNext()) {
            return Token.End(cursor.Loc);
        }

        switch (cursor.Current) {
            
        }

        return Token.Invalid(cursor.Current.ToString(), cursor.Loc);
    }
}

public struct TokenEnumerator : IEnumerator<Token>, IEnumerable<Token>
{
    private Cursor _cursor;
    private Token _current = new();

    internal TokenEnumerator(Source source)
    {
        _cursor = new Cursor(source);
    }

    public readonly Token Current => _current;

    public bool MoveNext()
    {
        if (_current.Tag == TokenTag.End) {
            _current = new Token();
            return false;
        }

        _cursor.Sync();
        _current = Lexer.NextToken(ref _cursor);
        return true;
    }

    public void Reset()
    {
        _cursor.Reset();
    }

    #region Explicit implementations

    readonly object IEnumerator.Current => Current;

    readonly IEnumerator IEnumerable.GetEnumerator() => this;

    readonly IEnumerator<Token> IEnumerable<Token>.GetEnumerator() => this;

    readonly void IDisposable.Dispose() {}

    #endregion
}

internal struct Cursor(Source source) : IEnumerator<Rune>, IEnumerable<Rune>
{
    private Rune _current = default;
    private int _start = 0;
    private int _now = 0;

    public readonly Rune Current => _current;

    public bool MoveNext()
    {
        if (_now >= source.Length)
        {
            _current = default;
            return false;
        }

        if (!Rune.TryGetRuneAt(source, _now, out _current)) {
            _current = Rune.ReplacementChar;
        }

        _now += _current.Utf16SequenceLength;
        return true;
    }

    public void Sync()
    {
        _start = _now;
    }

    public void Reset()
    {
        _start = 0;
        _now = 0;
    }

    public readonly Loc Loc => Loc.FromRange(_start, _now);

    #region Explicit implementations

    readonly object IEnumerator.Current => _current;

    readonly IEnumerator IEnumerable.GetEnumerator() => this;

    readonly IEnumerator<Rune> IEnumerable<Rune>.GetEnumerator() => this;

    readonly void IDisposable.Dispose() {}

    #endregion
}
