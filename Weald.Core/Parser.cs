using System.Collections.Immutable;
using System.Globalization;
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
        Expr(minPower, prevOp: default, fallbackDesc);

    private IAstExpr Expr(PowerLevel minPower, BinaryOperator prevOp, ProblemDesc fallbackDesc)
    {
        var left = ExprPrefix(fallbackDesc);

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
            left = ExprInfix(left, op, Parser.Problems.ExpectedExpr);

            if (isAmbiguous) {
                problems.Report(Parser.Problems.AmbiguousExpr, Loc.Join(prevOp.Loc, left.Loc));
            }
        }

        return left;
    }

    private IAstExpr ExprInfix(IAstExpr left, BinaryOperator op, ProblemDesc fallbackDesc)
    {
        var right = Expr(op.NextPower, op, fallbackDesc);

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

    private IAstExpr ExprPrefix(ProblemDesc descIfMissing)
    {
        var token = lexer.Current;
        if (!IsBreakpoint(token.Tag)) {
            lexer.MoveNext();
        }

        switch (token.Tag) {
            case TokenTag.PBang:  return Unary(token, "unary !");
            case TokenTag.PMinus: return Unary(token, "unary -");
            case TokenTag.PPlus:  return Unary(token, "unary +");

            case TokenTag.PParenOpen: return Group(token);
            case TokenTag.PBraceOpen: return Block(token);

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

    private AstCall Unary(Token token, string name)
    {
        var receiver =
            Expr(minPower: PowerLevel.Unary, fallbackDesc: Parser.Problems.ExpectedExpr);

        return new AstCall {
            Receiver = receiver,
            Function = new AstName { Value = name, Loc = token.Loc },
            Arguments = null,
            Loc = Loc.Join(token.Loc, receiver.Loc),
        };
    }

    private AstGroup Group(Token opening)
    {
        var body = WithBreakpoint(PParenClose, () =>
            Expr(minPower: PowerLevel.Lowest, fallbackDesc: Parser.Problems.ExpectedExprInGroup)
        );

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

    private static AstString String(Token token)
    {
        throw new NotImplementedException();
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
