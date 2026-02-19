// ReSharper disable UnusedType.Global
// ReSharper disable UnusedMember.Global
// ReSharper disable UnusedAutoPropertyAccessor.Global
// ReSharper disable ClassNeverInstantiated.Global
using System.Collections.Immutable;
using System.Diagnostics.CodeAnalysis;

namespace Weald.Core;

[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties)]
public interface IAst
{
    public Loc Loc { get; }
}

public sealed record AstName : IAst
{
    public required string Value { get; init; }
    public required Loc Loc { get; init; }
}

#region Expressions

public interface IAstExpr : IAst;

public sealed record AstMissing : IAstExpr
{
    public required Loc Loc { get; init; }
}

public sealed record AstTrue : IAstExpr
{
    public required Loc Loc { get; init; }
}

public sealed record AstFalse : IAstExpr
{
    public required Loc Loc { get; init; }
}

public sealed record AstInt : IAstExpr
{
    public required Int128 Value { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstFloat : IAstExpr
{
    public required double Value { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstString : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required string Interpreted { get; init; }
    public required Loc ContentLoc { get; init; }
    public required Loc ClosingLoc { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstVariableRead : IAstExpr
{
    public required string Name { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstGroup : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required IAstExpr Body { get; init; }
    public required Loc ClosingLoc { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstBlock : IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required AstStmts Stmts { get; init; }
    public required Loc ClosingLoc { get; init; }
    public required Loc Loc { get; init; }
}

public interface IAstIfAlternate : IAst;

public sealed record AstIf : IAstExpr, IAstIfAlternate
{
    public required Loc KwIfLoc { get; init; }
    public required IAstExpr Predicate { get; init; }
    public Loc? TernaryThenLoc { get; init; }
    public required IAstExpr Then { get; init; }
    public IAstIfAlternate? Else { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstElse : IAstIfAlternate
{
    public required Loc KwElseLoc { get; init; }
    public required IAstExpr Body { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstAnd : IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstOr : IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstCall : IAstExpr
{
    public required IAstExpr Receiver { get; init; }
    public Loc? OperatorLoc { get; init; }
    public required AstName Function { get; init; }
    public Loc? OpeningLoc { get; init; }
    public AstArguments? Arguments { get; init; }
    public Loc? ClosingLoc { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstArguments : IAst
{
    public required ImmutableArray<IAstExpr> Items { get; init; }
    public required Loc Loc { get; init; }
}

#endregion Expressions

#region Statements and declarations

public interface IAstStmt : IAst;

public sealed record AstStmtExpr : IAstStmt
{
    public required IAstExpr Expr { get; init; }
    public required Loc Loc { get; init; }
}

public sealed record AstStmts : IAst
{
    public required ImmutableArray<IAstStmt> Items { get; init; }
    public required Loc Loc { get; init; }
}

public interface IAstDecl : IAstStmt;

public sealed record AstVariableDecl : IAstDecl
{
    public required Loc KwLetLoc { get; init; }
    public required AstName Name { get; init; }
    public required Loc EqLoc { get; init; }
    public required IAstExpr Value { get; init; }
    public required Loc Loc { get; init; }
}

#endregion Statements and declarations

public sealed record AstScript : IAst
{
    public required AstStmts Stmts { get; init; }
    public required Loc Loc { get; init; }
}
