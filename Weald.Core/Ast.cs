using System.Collections.Immutable;

namespace Weald.Core;

public interface IAst
{
    public Loc Loc { get; }

    public IEnumerable<(string Name, object? Value)> Props();
    public string DisplayName();
    public IEnumerable<IAst?> Children();
}

[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
public sealed class GenAstAttribute : Attribute;

[GenAst]
public sealed partial record AstName : IAst
{
    public required string Value { get; init; }
}

#region Expressions

public interface IAstExpr : IAst;

[GenAst]
public sealed partial record AstMissing : IAstExpr;

[GenAst]
public sealed partial record AstTrue : IAstExpr;

[GenAst]
public sealed partial record AstFalse : IAstExpr;

[GenAst]
public sealed partial record AstInt : IAstExpr
{
    public required Int128 Value { get; init; }
}

[GenAst]
public sealed partial record AstFloat : IAstExpr
{
    public required double Value { get; init; }
}

[GenAst]
public sealed partial record AstString : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required string Interpreted { get; init; }
    public required Loc ContentLoc { get; init; }
    public required Loc ClosingLoc { get; init; }
}

[GenAst]
public sealed partial record AstVariableRead : IAstExpr
{
    public required string Name { get; init; }
}

[GenAst]
public sealed partial record AstGroup : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required IAstExpr Body { get; init; }
    public required Loc ClosingLoc { get; init; }
}

[GenAst]
public sealed partial record AstBlock : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required AstStmts Stmts { get; init; }
    public required Loc ClosingLoc { get; init; }
}

[GenAst]
public sealed partial record AstIf : IAstExpr
{
    public required Loc KwIfLoc { get; init; }
    public required IAstExpr Predicate { get; init; }
    public Loc? TernaryThenLoc { get; init; }
    public required IAstExpr Then { get; init; }
    public AstElse? Else { get; init; }
}

[GenAst]
public sealed partial record AstElse : IAst
{
    public required Loc KwElseLoc { get; init; }
    public required IAstExpr Body { get; init; }
}

[GenAst]
public sealed partial record AstAnd : IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
}

[GenAst]
public sealed partial record AstOr : IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
}

[GenAst]
public sealed partial record AstCall : IAstExpr
{
    public required IAstExpr Receiver { get; init; }
    public Loc? OperatorLoc { get; init; }
    public required AstName Function { get; init; }
    public Loc? OpeningLoc { get; init; }
    public AstArguments? Arguments { get; init; }
    public Loc? ClosingLoc { get; init; }
}

[GenAst]
public sealed partial record AstArguments : IAst
{
    public required ImmutableArray<IAstExpr> Items { get; init; }
}

#endregion Expressions

#region Statements and declarations

public interface IAstStmt : IAst;

[GenAst]
public sealed partial record AstStmtExpr : IAstStmt
{
    public required IAstExpr Expr { get; init; }
}

[GenAst]
public sealed partial record AstStmts : IAst
{
    public required ImmutableArray<IAstStmt> Items { get; init; }
}

public interface IAstDecl : IAstStmt;

[GenAst]
public sealed partial record AstVariableDecl : IAstDecl
{
    public required Loc KwLetLoc { get; init; }
    public required AstName Name { get; init; }
    public required Loc EqLoc { get; init; }
    public required IAstExpr Value { get; init; }
}

#endregion Statements and declarations

[GenAst]
public sealed partial record AstScript
{
    public required AstStmts Stmts { get; init; }
}
