using System.Buffers;
using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using static Weald.Core.TokenTag;

namespace Weald.Core;

public static class Parser
{
    public readonly record struct Result(AstScript Ast, ImmutableArray<Problem> Problems)
    {
        public bool HasProblems => Problems.Length > 0;
    }

    [Pure]
    public static Result Parse(ImmutableArray<Token> tokens)
    {
        if (tokens.IsDefaultOrEmpty || tokens[^1].Tag != End) {
            throw new ArgumentException("the last token must be End", nameof(tokens));
        }

        var problems = new ProblemArrayBuilder();
        var ast = new Core(new ParserLexer(tokens, problems), problems).Parse();
        return new Result(ast, problems.Drain());
    }

    public static class Problems
    {
        public static readonly ProblemDesc ExpectedEnd = new() {
            Id = "syntax/expected-end",
            Message = "unexpected token after being done parsing",
        };

        public static readonly ProblemDesc ExpectedLetName = new() {
            Id = "syntax/expected-let-name",
            Message = "expected a name after 'let'",
        };

        public static readonly ProblemDesc ExpectedLetEq = new() {
            Id = "syntax/expected-let-eq",
            Message = "expected '=' after 'let'",
        };

        public static readonly ProblemDesc ExpectedLetExpr = new() {
            Id = "syntax/expected-let-expr",
            Message = "expected an expression for let",
        };

        public static readonly ProblemDesc ExpectedStmt = new() {
            Id = "syntax/expected-stmt",
            Message = "expected a statement",
        };

        public static readonly ProblemDesc AmbiguousExpr = new() {
            Id = "syntax/ambiguous-expr",
            Message = "ambiguous expression; use parentheses to express precedence clearly",
        };

        public static readonly ProblemDesc ExpectedExpr = new() {
            Id = "syntax/expected-expr",
            Message = "expected an expression",
        };

        public static readonly ProblemDesc ExpectedExprInGroup = new() {
            Id = "syntax/expected-expr-in-group",
            Message = "expected an expression after '('",
        };

        public static readonly ProblemDesc ExpectedPredicate = new() {
            Id = "syntax/expected-predicate",
            Message = "expected a predicate for 'if'",
        };

        public static readonly ProblemDesc ExpectedIfBody = new() {
            Id = "syntax/expected-if-body",
            Message = "expected '{' or '?' after 'if' predicate",
        };

        public static readonly ProblemDesc ExpectedElseBody = new() {
            Id = "syntax/expected-else-body",
            Message = "expected '{' or 'if' after 'else'",
        };

        public static readonly ProblemDesc ExpectedExprInTernaryThen = new() {
            Id = "syntax/expected-expr-in-ternary-then",
            Message = "expected an expression after '?'",
        };

        public static readonly ProblemDesc ExpectedTernaryElse = new() {
            Id = "syntax/expected-ternary-else",
            Message = "expected ':' after '?' expression",
        };

        public static readonly ProblemDesc ExpectedExprInTernaryElse = new() {
            Id = "syntax/expected-expr-in-ternary-else",
            Message = "expected an expression after ':'",
        };

        public static readonly ProblemDesc BlockInTernary = new() {
            Id = "syntax/block-in-ternary",
            Message = "blocks are not allowed in ternary expressions; rewrite to a standard 'if'",
        };

        public static readonly ProblemDesc UnclosedGroup = new() {
            Id = "syntax/unclosed-group",
            Message = "expected a matching ')'",
        };

        public static readonly ProblemDesc UnclosedBlock = new() {
            Id = "syntax/unclosed-block",
            Message = "expected a matching '}'",
        };

        public static readonly ProblemDesc InvalidInt = new() {
            Id = "syntax/invalid-int",
            Message = "integer literal overflows 128 bits",
        };

        public static readonly ProblemDesc InvalidFloat = new() {
            Id = "syntax/invalid-float",
            Message = "float literal cannot be accurately parsed",
        };

        public static ProblemDesc InvalidEscape(Rune rune) => new() {
            Id = "syntax/invalid-escape",
            Message = $@"unrecognised escape sequence '\{Rune.Escape(rune)}'",
        };

        public static readonly ProblemDesc InvalidHexEscape = new() {
            Id = "syntax/invalid-escape",
            Message = @"invalid \x escape sequence; expected exactly 2 hexadecimal digits",
        };

        public static readonly ProblemDesc InvalidUnicodeEscape = new() {
            Id = "syntax/invalid-escape",
            Message = @"invalid \u escape sequence; expected exactly 4 hexadecimal digits",
        };

        public static ProblemDesc InvalidUnicodeEscapePoint(ReadOnlySpan<char> hex) => new() {
            Id = "syntax/invalid-escape",
            Message = $@"invalid \u escape sequence; '{hex}' is not a Unicode code point",
        };

        public static readonly ProblemDesc InvalidUnicodeBraceEscape = new() {
            Id = "syntax/invalid-escape",
            Message = @"invalid \u{} escape sequence; expected up to 6 hexadecimal digits within braces",
        };

        public static ProblemDesc InvalidUnicodeBraceEscapePoint(ReadOnlySpan<char> hex) => new() {
            Id = "syntax/invalid-escape",
            Message = $@"invalid \u{{}} escape sequence; '{hex}' is not a Unicode code point",
        };
    }
}

file sealed class Core(ParserLexer lexer, ProblemArrayBuilder problems)
{
    private Stack<TokenTag> _breakpoints = new();

    public AstScript Parse()
    {
        return Script();
    }

    private bool IsBreakpoint(TokenTag tag) => _breakpoints.Contains(tag);

    private TokenTag CurrentBreakpoint => _breakpoints.Peek();

    private T WithBreakpoint<T>(TokenTag breakpoint, Func<T> f)
    {
        _breakpoints.Push(breakpoint);
        var result = f();
        _breakpoints.Pop();
        return result;
    }

    private AstScript Script()
    {
        lexer.MoveNext();

        var stmts = WithBreakpoint(End, Stmts);

        var last = lexer.Current;
        if (lexer.MoveNext() && problems.IsEmpty) {
            problems.Report(Parser.Problems.ExpectedEnd, last.Loc);
        }

        var fullLoc = Loc.Join(new Loc(), lexer.Current.Loc);
        return new AstScript { Stmts = stmts, Loc = fullLoc };
    }

    private AstStmts Stmts()
    {
        var stmts = new AstArrayBuilder<IAstStmt>();

        while (lexer.CheckNot(CurrentBreakpoint)) {
            stmts.Add(Stmt());

            if (!lexer.MatchNewline()) break;
        }

        var (items, loc) = stmts.Drain();
        return new AstStmts { Items = items, Loc = loc };
    }

    private IAstStmt Stmt()
    {
        if (lexer.Check(KwLet)) {
            return VariableDecl();
        }

        return StmtExpr();
    }

    private AstVariableDecl VariableDecl()
    {
        var kwLet = lexer.Assert(KwLet);
        var name = lexer.Expect(TokenTag.Name, Parser.Problems.ExpectedLetName);
        var eq = lexer.Expect(PEqual, Parser.Problems.ExpectedLetEq);
        var value =
            Expr(minPower: PowerLevel.Lowest, fallbackDesc: Parser.Problems.ExpectedLetExpr);

        return new AstVariableDecl {
            KwLetLoc = kwLet.Loc,
            Name = new AstName { Value = name.Text.AssertPresence(), Loc = name.Loc },
            EqLoc = eq.Loc,
            Value = value,
            Loc = Loc.Join(kwLet.Loc, value.Loc),
        };
    }

    private AstStmtExpr StmtExpr()
    {
        var expr = Expr(minPower: PowerLevel.Lowest, fallbackDesc: Parser.Problems.ExpectedStmt);
        return new AstStmtExpr { Expr = expr, Loc = expr.Loc };
    }

    private IAstExpr Expr(PowerLevel minPower, ProblemDesc fallbackDesc) =>
        Expr(minPower, prevOp: default, fallbackDesc, out _);

    private IAstExpr Expr(PowerLevel minPower, ProblemDesc fallbackDesc, out bool hasBlock) =>
        Expr(minPower, prevOp: default, fallbackDesc, out hasBlock);

    private IAstExpr Expr(
        PowerLevel minPower,
        BinaryOperator prevOp,
        ProblemDesc fallbackDesc,
        out bool hasBlock
    )
    {
        var left = ExprPrefix(fallbackDesc, out hasBlock);

        while (!lexer.IsEmpty) {
            var token = lexer.Current;

            if (BinaryOperator.From(token) is not {} op) {
                break;
            }

            var isAmbiguous = op.IsAmbiguousWith(prevOp);

            if (!(isAmbiguous || op.Accepts(minPower))) {
                break;
            }

            lexer.MoveNext();
            left = ExprInfix(left, op, Parser.Problems.ExpectedExpr, out hasBlock);

            if (isAmbiguous) {
                problems.Report(Parser.Problems.AmbiguousExpr, Loc.Join(prevOp.Loc, left.Loc));
            }
        }

        return left;
    }

    private IAstExpr ExprInfix(
        IAstExpr left,
        BinaryOperator op,
        ProblemDesc fallbackDesc,
        out bool hasBlock
    )
    {
        var right = Expr(op.NextPower, op, fallbackDesc, out hasBlock);

        return op.Tag switch {
            PAndAnd => new AstAnd {
                Left = left,
                OperatorLoc = op.Loc,
                Right = right,
                Loc = Loc.Join(left.Loc, right.Loc),
            },

            POrOr => new AstOr {
                Left = left,
                OperatorLoc = op.Loc,
                Right = right,
                Loc = Loc.Join(left.Loc, right.Loc),
            },

            _ => new AstCall {
                Receiver = left,
                Function = new AstName { Value = op.Name, Loc = op.Loc },
                Arguments = new AstArguments {
                    Items = [right],
                    Loc = right.Loc,
                },
                Loc = Loc.Join(left.Loc, right.Loc),
            },
        };
    }

    private IAstExpr ExprPrefix(ProblemDesc descIfMissing, out bool hasBlock)
    {
        hasBlock = false;

        var token = lexer.Current;
        if (!IsBreakpoint(token.Tag)) {
            lexer.MoveNext();
        }

        switch (token.Tag) {
            case TokenTag.PBang:  return Unary(token, "unary !", out hasBlock);
            case TokenTag.PMinus: return Unary(token, "unary -", out hasBlock);
            case TokenTag.PPlus:  return Unary(token, "unary +", out hasBlock);

            case TokenTag.PBraceOpen: {
                hasBlock = true;
                return Block(token);
            }
            case TokenTag.KwIf: {
                hasBlock = true;
                return If(token);
            }

            case TokenTag.PParenOpen: return Group(token, out hasBlock);

            case TokenTag.Name:    return Name(token);
            case TokenTag.KwTrue:  return True(token);
            case TokenTag.KwFalse: return False(token);
            case TokenTag.Int:     return Int(token);
            case TokenTag.Float:   return Float(token);
            case TokenTag.String:  return String(token);

            case TokenTag.Invalid: return new AstMissing { Loc = token.Loc };

            default: {
                problems.Report(descIfMissing, token.Loc);
                return new AstMissing { Loc = token.Loc };
            }
        }
    }

    private AstCall Unary(Token token, string name, out bool hasBlock)
    {
        var receiver =
            Expr(
                minPower: PowerLevel.Unary,
                fallbackDesc: Parser.Problems.ExpectedExpr,
                out hasBlock
            );

        return new AstCall {
            Receiver = receiver,
            Function = new AstName { Value = name, Loc = token.Loc },
            Arguments = null,
            Loc = Loc.Join(token.Loc, receiver.Loc),
        };
    }

    private AstGroup Group(Token opening, out bool hasBlock)
    {
        var hasBlockTmp = false;
        var body = WithBreakpoint(PParenClose, () =>
            Expr(
                minPower: PowerLevel.Lowest,
                fallbackDesc: Parser.Problems.ExpectedExprInGroup,
                out hasBlockTmp
            )
        );

        hasBlock = hasBlockTmp;

        var closing = lexer.Expect(PParenClose, Parser.Problems.UnclosedGroup);

        return new AstGroup {
            Body = body,
            OpeningLoc = opening.Loc,
            ClosingLoc = closing.Loc,
            Loc = Loc.Join(opening.Loc, closing.Loc),
        };
    }

    private AstBlock Block(Token opening)
    {
        var stmts = WithBreakpoint(PBraceClose, Stmts);

        var closing = lexer.Expect(PBraceClose, Parser.Problems.UnclosedBlock);

        return new AstBlock {
            Stmts = stmts,
            OpeningLoc = opening.Loc,
            ClosingLoc = closing.Loc,
            Loc = Loc.Join(opening.Loc, closing.Loc),
        };
    }

    private AstIf If(Token kwIf)
    {
        var predicate =
            Expr(minPower: PowerLevel.Lowest, fallbackDesc: Parser.Problems.ExpectedPredicate);

        return lexer.Match(PQuestion, out var ternaryThen)
            ? IfTernary(kwIf, predicate, ternaryThen)
            : IfNormal(kwIf, predicate);
    }

    private AstIf IfNormal(Token kwIf, IAstExpr predicate)
    {
        IAstExpr thenBlock;
        if (lexer.Match(PBraceOpen, out var opening)) {
            thenBlock = Block(opening);
        }
        else {
            thenBlock = new AstMissing { Loc = Loc.FromLength(predicate.Loc.End(), 0) };
            problems.Report(Parser.Problems.ExpectedIfBody, thenBlock.Loc);
        }

        AstElse? elseBlock = null;
        if (lexer.Match(KwElse, out var kwElse)) {
            IAstExpr body;
            if (lexer.Match(PBraceOpen, out var elseOpening)) {
                body = Block(elseOpening);
            }
            else if (lexer.Match(KwIf, out var elseKwIf)) {
                body = If(elseKwIf);
            }
            else {
                body = new AstMissing { Loc = Loc.FromLength(kwElse.Loc.End(), 0) };
                problems.Report(Parser.Problems.ExpectedElseBody, body.Loc);
            }

            elseBlock = new AstElse {
                KwElseLoc = kwElse.Loc,
                Body = body,
                Loc = Loc.Join(kwElse.Loc, body.Loc),
            };
        }

        return new AstIf {
            Predicate = predicate,
            Then = thenBlock,
            Else = elseBlock,
            KwIfLoc = kwIf.Loc,
            Loc = Loc.Join(kwIf.Loc, elseBlock?.Loc ?? thenBlock.Loc),
        };
    }

    private AstIf IfTernary(Token kwIf, IAstExpr predicate, Token ternaryThen)
    {
        var thenExpr =
            Expr(
                minPower: PowerLevel.Lowest,
                fallbackDesc: Parser.Problems.ExpectedExprInTernaryThen,
                out var thenHasBlock
            );

        if (thenHasBlock) {
            problems.Report(Parser.Problems.BlockInTernary, thenExpr.Loc);
        }

        AstElse? elsePart;
        if (lexer.Match(PColon, out var kwElse)) {
            var elseExpr = Expr(
                minPower: PowerLevel.Lowest,
                fallbackDesc: Parser.Problems.ExpectedExprInTernaryElse,
                out var elseHasBlock
            );

            if (elseHasBlock) {
                problems.Report(Parser.Problems.BlockInTernary, elseExpr.Loc);
            }

            elsePart = new AstElse {
                KwElseLoc = kwElse.Loc,
                Body = elseExpr,
                Loc = Loc.Join(kwElse.Loc, elseExpr.Loc),
            };
        }
        else {
            elsePart = null;
            problems.Report(
                Parser.Problems.ExpectedTernaryElse,
                Loc.FromLength(thenExpr.Loc.End(), 0)
            );
        }

        return new AstIf {
            KwIfLoc = kwIf.Loc,
            Predicate = predicate,
            TernaryThenLoc = ternaryThen.Loc,
            Then = thenExpr,
            Else = elsePart,
            Loc = Loc.Join(kwIf.Loc, elsePart?.Loc ?? thenExpr.Loc),
        };
    }

    private static AstVariableRead Name(Token token) =>
        new() { Name = token.Text.AssertPresence(), Loc = token.Loc };

    private static AstTrue True(Token token) => new() { Loc = token.Loc };

    private static AstFalse False(Token token) => new() { Loc = token.Loc };

    private IAstExpr Int(Token token)
    {
        var num = token.Text.AssertPresence();

        var hasSign = num[0] is '+' or '-';
        var isNegative = num[0] == '-';
        var radix = 10;
        if (num.Length > 2) {
            // The radix char is located on the second character (0x..., 0b...), unless there is a
            // sign, which means it's one to the right (-0x..., +0b...)
            var radixChar = hasSign ? num[2] : num[1];
            radix = radixChar switch {
                'x' => 16,
                'b' => 2,
                _   => 10,
            };
        }

        Int128 value = 0;

        try {
            // Determine where the number effectively starts by skipping the sign and radix chars
            var start = (hasSign ? 1 : 0) + (radix != 10 ? 2 : 0);

            checked {
                foreach (var c in num.AsSpan(start ..)) {
                    if (c != '_') {
                        value = value * radix + char.ToDigit(c, radix);
                    }
                }

                if (isNegative) value = -value;
            }
        }
        catch (OverflowException) {
            problems.Report(Parser.Problems.InvalidInt, token.Loc);
            return new AstMissing { Loc = token.Loc };
        }

        return new AstInt { Value = value, Loc = token.Loc };
    }

    private IAstExpr Float(Token token)
    {
        var num = token.Text.AssertPresence();

        var ok = double.TryParse(
            num.Replace("_", "", StringComparison.Ordinal),
            NumberStyles.AllowLeadingSign |
                NumberStyles.AllowDecimalPoint |
                NumberStyles.AllowExponent,
            CultureInfo.InvariantCulture,
            out var value
        );

        Debug.Assert(ok, "double.TryParse failed: bug in compiler");

        if (double.IsInfinity(value) || double.IsNaN(value)) {
            problems.Report(Parser.Problems.InvalidFloat, token.Loc);
            return new AstMissing { Loc = token.Loc };
        }

        return new AstFloat { Value = value, Loc = token.Loc };
    }

    private AstString String(Token token)
    {
        var body = token.Text.AssertPresence();

        if (body.StartsWith("```", StringComparison.Ordinal)) {
            return StringRawBlock(token, body);
        }
        else if (body.StartsWith('`')) {
            return StringRawLine(token, body);
        }
        else if (body.StartsWith("\"\"\"", StringComparison.Ordinal)) {
            return StringStdBlock(token, body);
        }
        else {
            Debug.Assert(body.StartsWith('"'));
            return StringStdLine(token, body);
        }
    }

    private AstString StringStdLine(Token token, string body)
    {
        const string quote = "\"";

        var (openingLoc, contentLoc, closingLoc) = StringLocs(token, quote);

        string? interpreted;
        if (body.Contains('\\', StringComparison.Ordinal)) {
            interpreted = Unescape(body.AsSpan(quote.Length .. ^quote.Length), body, token.Loc);
        }
        else {
            interpreted = body[quote.Length .. ^quote.Length];
        }

        return new AstString {
            Interpreted = interpreted,
            OpeningLoc = openingLoc,
            ContentLoc = contentLoc,
            ClosingLoc = closingLoc,
            Loc = token.Loc,
        };
    }

    private static AstString StringRawLine(Token token, string body)
    {
        const string quote = "`";

        var (openingLoc, contentLoc, closingLoc) = StringLocs(token, quote);

        return new AstString {
            Interpreted = body,
            OpeningLoc = openingLoc,
            ContentLoc = contentLoc,
            ClosingLoc = closingLoc,
            Loc = token.Loc,
        };
    }

    private AstString StringStdBlock(Token token, string body)
    {
        const string quote = "\"\"\"";

        var (openingLoc, contentLoc, closingLoc) = StringLocs(token, quote);

        var interpreted = Unescape(Unindent(body, quote), body, token.Loc);

        return new AstString {
            Interpreted = interpreted,
            OpeningLoc = openingLoc,
            ContentLoc = contentLoc,
            ClosingLoc = closingLoc,
            Loc = token.Loc,
        };
    }

    private static AstString StringRawBlock(Token token, string body)
    {
        const string quote = "```";

        var (openingLoc, contentLoc, closingLoc) = StringLocs(token, quote);

        var interpreted = Unindent(body, quote);

        return new AstString {
            Interpreted = interpreted,
            OpeningLoc = openingLoc,
            ContentLoc = contentLoc,
            ClosingLoc = closingLoc,
            Loc = token.Loc,
        };
    }

    private static readonly SearchValues<char> Whitespace = SearchValues.Create(" \t");

    private static string Unindent(string body, string quote)
    {
        var raw = body.AsSpan(quote.Length .. ^quote.Length);
        var trimmed = raw.Trim(" \t").Trim("\n\r");

        // while EnumerateLines considers stuff like FF/NEL/LS/PS as newlines, that's okay because
        // the lexer explicitly forbids those within strings, so we can freely use EnumerateLines
        // to split the span at LF, CR, and CRLF, just like we want!
        Debug.Assert(!trimmed.ContainsAny(SearchValues.Create("\f\u0085\u2028\u2029")));
        var lines = trimmed.EnumerateLines();

        ReadOnlySpan<char> commonPrefix = "";
        var noPrefix = true;
        foreach (var line in lines) {
            var prefix = line.TakePrefix(Whitespace);
            if (line != prefix && (noPrefix || commonPrefix.StartsWith(prefix))) {
                commonPrefix = prefix;
                noPrefix = false;
            }
        }

        var joined = new StringBuilder();
        foreach (var line in lines) {
            if (line != line.TakePrefix(Whitespace)) {
                joined.Append(line[commonPrefix.Length ..]);
            }

            joined.Append('\n');
        }

        // remove last \n
        joined.Length -= 1;

        return joined.ToString();
    }

    private string Unescape(ReadOnlySpan<char> str, ReadOnlySpan<char> original, Loc loc)
    {
        var result = new StringBuilder();

        int nextEscape;
        var escapeIndex = 0;
        while ((nextEscape = str.IndexOf('\\')) != -1) {
            var escape = str[nextEscape + 1];

            result.Append(str[0 .. nextEscape]);
            str = str[(nextEscape + 2) ..];

            if (UnescapeNext(ref str, original, escape, escapeIndex, loc) is {} rune) {
                result.Append(rune.ToString());
            }

            ++escapeIndex;
        }

        result.Append(str);
        return result.ToString();
    }

    private Rune? UnescapeNext(
        ref ReadOnlySpan<char> str,
        ReadOnlySpan<char> original,
        char escape,
        int escapeIndex,
        Loc loc
    )
    {
        switch (escape) {
            case '"':  return new Rune('"');
            case '\\': return new Rune('\\');
            case 'e':  return new Rune('\e');
            case 'n':  return new Rune('\n');
            case 'r':  return new Rune('\r');
            case 't':  return new Rune('\t');

            case '\n' or '\r': {
                var nextNonWs = str.IndexOfAnyExcept(' ', '\t');
                if (nextNonWs == -1) nextNonWs = str.Length;
                str = str[nextNonWs ..];
                return null;
            }

            case 'x': {
                return UnescapeHex(ref str, original, escapeIndex, loc);
            }

            case 'u': {
                if (str.Length > 0 && str[0] == '{') {
                    return UnescapeUnicodeBraced(ref str, original, escapeIndex, loc);
                }
                else {
                    return UnescapeUnicode(ref str, original, escapeIndex, loc);
                }
            }

            default: {
                var escapeLoc = FindEscapeLoc(original, escapeIndex, escapeLen: 2, loc);
                problems.Report(Parser.Problems.InvalidEscape(new Rune(escape)), escapeLoc);
                return null;
            }
        }
    }

    private Rune? UnescapeHex(
        ref ReadOnlySpan<char> str,
        ReadOnlySpan<char> original,
        int escapeIndex,
        Loc loc
    )
    {
        var next = int.Min(2, str.Length);
        var hex = str[0 .. next];
        if (hex.Length == 2 && Rune.ParseHex(hex) is {} rune) {
            str = str[next ..];
            return rune;
        }
        else {
            var escapeLoc = FindEscapeLoc(original, escapeIndex, escapeLen: 2 + hex.Length, loc);
            problems.Report(Parser.Problems.InvalidHexEscape, escapeLoc);
            return null;
        }
    }

    private Rune? UnescapeUnicode(
        ref ReadOnlySpan<char> str,
        ReadOnlySpan<char> original,
        int escapeIndex,
        Loc loc
    )
    {
        var next = int.Min(4, str.Length);
        var hex = str[0 .. next];

        if (hex.Length == 4) {
            if (Rune.ParseHex(hex) is {} rune) {
                str = str[next .. ];
                return rune;
            }
            else {
                problems.Report(
                    Parser.Problems.InvalidUnicodeEscapePoint(hex),
                    FindEscapeLoc(original, escapeIndex, escapeLen: 2 + hex.Length, loc)
                );
            }
        }
        else {
            problems.Report(
                Parser.Problems.InvalidUnicodeEscape,
                FindEscapeLoc(original, escapeIndex, escapeLen: 2 + hex.Length, loc)
            );
        }

        return null;
    }

    private Rune? UnescapeUnicodeBraced(
        ref ReadOnlySpan<char> str,
        ReadOnlySpan<char> original,
        int escapeIndex,
        Loc loc
    )
    {
        Debug.Assert(str[0] == '{');
        str = str[1 ..];

        var closing = str.IndexOf('}');
        var hexEnd = closing == -1 ? int.Min(str.Length, 6) : int.Min(closing, 6);
        var hex = str[0 .. hexEnd];

        // calculate the full escape sequence length including \u{ and }
        var escapeLen = 3 + hex.Length + (closing is >= 0 and <= 6 ? 1 : 0);

        if (hex.Length is >= 1 and <= 6 && closing is >= 0 and <= 6) {
            if (Rune.ParseHex(hex) is {} rune) {
                str = str[(hexEnd + 1) ..];
                return rune;
            }
            else {
                problems.Report(
                    Parser.Problems.InvalidUnicodeBraceEscapePoint(hex),
                    FindEscapeLoc(original, escapeIndex, escapeLen, loc)
                );
            }
        }
        else {
            problems.Report(
                Parser.Problems.InvalidUnicodeBraceEscape,
                FindEscapeLoc(original, escapeIndex, escapeLen, loc)
            );
        }

        return null;
    }

    private static (Loc Opening, Loc Content, Loc Closing) StringLocs(Token token, string quote)
    {
        var opening = Loc.FromLength(token.Loc.Start, quote.Length);
        var closing = Loc.FromLength(token.Loc.End() - quote.Length, quote.Length);
        var content = Loc.FromRange(opening.End(), closing.Start);
        return (opening, content, closing);
    }

    private static Loc FindEscapeLoc(
        ReadOnlySpan<char> str,
        int escapeIndex,
        int escapeLen,
        Loc loc
    )
    {
        var actualEscapeIndex = -1;
        for (var i = 0; i < str.Length; ++i) {
            var c = str[i];
            if (c == '\\' && ++actualEscapeIndex == escapeIndex) {
                actualEscapeIndex = i;
                break;
            }
        }

        Debug.Assert(actualEscapeIndex != -1);

        return Loc.FromLength(loc.Start + actualEscapeIndex, escapeLen);
    }
}

file enum PowerLevel
{
    Lowest = 1,

    // && ||
    Logic = Lowest,
    // == != < <= > >=
    Cmp,
    // + -
    Add,
    // * / %
    Mul,
    // ^
    Pow,
    // ! + -
    Unary,
}

file readonly record struct BinaryOperator(
    string Name,
    PowerLevel Power,
    PowerLevel NextPower,
    TokenTag Tag,
    Loc Loc
)
{
    public static BinaryOperator? From(Token token) =>
        token.Tag switch {
            PAndAnd       => LeftAssoc("&&", PowerLevel.Logic, token),
            POrOr         => LeftAssoc("||", PowerLevel.Logic, token),

            PEqualEqual   => LeftAssoc("==", PowerLevel.Cmp, token),
            PBangEqual    => LeftAssoc("!=", PowerLevel.Cmp, token),
            PLess         => LeftAssoc("<",  PowerLevel.Cmp, token),
            PLessEqual    => LeftAssoc("<=", PowerLevel.Cmp, token),
            PGreater      => LeftAssoc(">",  PowerLevel.Cmp, token),
            PGreaterEqual => LeftAssoc(">=", PowerLevel.Cmp, token),

            PPlus         => LeftAssoc("+", PowerLevel.Add, token),
            PMinus        => LeftAssoc("-", PowerLevel.Add, token),

            PStar         => LeftAssoc("*", PowerLevel.Mul, token),
            PSlash        => LeftAssoc("/", PowerLevel.Mul, token),
            PPercent      => LeftAssoc("%", PowerLevel.Mul, token),

            PCaret        => RightAssoc("^", PowerLevel.Pow, token),

            _ => null,
        };

    public bool Accepts(PowerLevel power) => Power >= power;

    public bool IsAmbiguousWith(BinaryOperator other) =>
        (Tag, other.Tag) switch {
            (PAndAnd, POrOr) or
            (POrOr, PAndAnd) or
            (
                PEqualEqual or PBangEqual or PLess or PLessEqual or PGreaterEqual or PGreater,
                PEqualEqual or PBangEqual or PLess or PLessEqual or PGreaterEqual or PGreater
            ) => true,

            _ => false,
        };

    private static BinaryOperator LeftAssoc(string name, PowerLevel power, Token token) =>
        new(name, power, power + 1, token.Tag, token.Loc);

    private static BinaryOperator RightAssoc(string name, PowerLevel power, Token token) =>
        new(name, power, power, token.Tag, token.Loc);
}

file sealed class ParserLexer(ImmutableArray<Token> tokens, ProblemArrayBuilder problems)
{
    private ImmutableArray<Token> _tokens = tokens;
    private Token? _lastNewline;

    public Token Current {
        [Pure]
        get;
        private set;
    }

    public bool IsEmpty {
        [Pure]
        get => Current.Tag == End;
    }

    public bool MoveNext()
    {
        _lastNewline = null;

        while (_tokens.Length > 0) {
            var token = _tokens[0];
            _tokens = _tokens[1 ..];

            switch (token.Tag) {
                case Newline: {
                    _lastNewline = token;
                    continue;
                }

                case Invalid: {
                    Debug.Assert(token.Text is not null);
                    Current = token;
                    problems.Report("syntax/invalid-token", token.Text, token.Loc);
                    return true;
                }

                default: {
                    Current = token;
                    return true;
                }
            }
        }

        Debug.Assert(Current.Tag == End);
        return false;
    }

    [Pure]
    public bool Check(TokenTag tag) => Current.Tag == tag;

    [Pure]
    public bool CheckNot(TokenTag tag) => !(IsEmpty || Check(tag));

    [MustUseReturnValue]
    public bool Match(TokenTag tag, out Token token)
    {
        var ok = Check(tag);
        if (ok) {
            token = Current;
            MoveNext();
        }
        else {
            token = default;
        }

        return ok;
    }

    [MustUseReturnValue]
    public bool MatchNewline() => _lastNewline is not null || IsEmpty;

    [MustUseReturnValue]
    public Token Expect(TokenTag tag, ProblemDesc problemDesc)
    {
        var prev = Current;
        if (prev.Tag == tag) {
            MoveNext();
            return prev;
        }

        problems.Report(problemDesc, prev.Loc);
        return Token.Invalid("", prev.Loc);
    }

    [MustUseReturnValue]
    [AssertionMethod]
    public Token Assert(TokenTag tag)
    {
        Debug.Assert(Check(tag));
        var token = Current;
        MoveNext();
        return token;
    }
}

file sealed class AstArrayBuilder<T>
    where T : IAst
{
    private ImmutableArray<T>.Builder _items = ImmutableArray.CreateBuilder<T>();
    private Loc _loc;

    public void Add(T item)
    {
        _items.Add(item);
        _loc = Loc.Join(_loc, item.Loc);
    }

    [MustUseReturnValue]
    public (ImmutableArray<T>, Loc) Drain() => (_items.DrainToImmutable(), _loc);
}
