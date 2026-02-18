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

public abstract record Ast
{
    public required Loc Loc { get; init; }
}

#region Expressions

public interface IAstExpr : IAst;

public sealed record AstMissing : Ast, IAstExpr;

public sealed record AstTrue : Ast, IAstExpr;

public sealed record AstFalse : Ast, IAstExpr;

public sealed record AstInt : Ast, IAstExpr
{
    public required Int128 Value { get; init; }
}

public sealed record AstFloat : Ast, IAstExpr
{
    public required double Value { get; init; }
}

public sealed record AstString : Ast, IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required string Interpreted { get; init; }
    public required Loc ContentLoc { get; init; }
    public required Loc ClosingLoc { get; init; }
}

public sealed record AstVariableRead : Ast, IAstExpr
{
    public required string Name { get; init; }
}

public sealed record AstGroup : Ast, IAstExpr
{
    public required Loc OpeningLoc { get; init; }
    public required IAstExpr Body { get; init; }
    public required Loc ClosingLoc { get; init; }
}

public sealed record AstBlock : Ast, IAstExpr, IAstStmt
{
    public required Loc OpeningLoc { get; init; }
    public required AstStmts Stmts { get; init; }
    public required Loc ClosingLoc { get; init; }
}

public interface IAstIfAlternate;

public sealed record AstIf : Ast, IAstExpr, IAstIfAlternate, IAstStmt
{
    public required Loc KwIfLoc { get; init; }
    public required IAstExpr Predicate { get; init; }
    public Loc? TernaryThenLoc { get; init; }
    public required IAstExpr Then { get; init; }
    public IAstIfAlternate? Else { get; init; }
}

public sealed record AstElse : Ast, IAstIfAlternate
{
    public required Loc KwElseLoc { get; init; }
    public required IAstExpr Body { get; init; }
}

public sealed record AstAnd : Ast, IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
}

public sealed record AstOr : Ast, IAstExpr
{
    public required IAstExpr Left { get; init; }
    public required Loc OperatorLoc { get; init; }
    public required IAstExpr Right { get; init; }
}

public sealed record AstCall : Ast, IAstExpr
{
    public required IAstExpr Receiver { get; init; }
    public Loc? OperatorLoc { get; init; }
    public required string Name { get; init; }
    public required Loc FunctionLoc { get; init; }
    public Loc? OpeningLoc { get; init; }
    public required AstArguments Arguments { get; init; }
    public Loc? ClosingLoc { get; init; }
}

public sealed record AstArguments : Ast
{
    public required ImmutableArray<IAstExpr> Items { get; init; }
}

#endregion Expressions

#region Statements and declarations

public interface IAstStmt : IAst;

public sealed record AstStmts : Ast, IAst
{
    public required ImmutableArray<IAstStmt> Items { get; init; }
}

public interface IAstDecl : IAstStmt;

public sealed record AstVariableDecl : Ast, IAstDecl
{
    public required Loc KwLetLoc { get; init; }
    public required string Name { get; init; }
    public required Loc EqLoc { get; init; }
    public required IAstExpr Value { get; init; }
}

#endregion Statements and declarations
