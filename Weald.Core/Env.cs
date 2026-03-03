using System.Collections.Immutable;

namespace Weald.Core;

public sealed class Env
{
    internal readonly record struct Scope(string Name, Symbol Symbol);

    private readonly ImmutableDictionary<AstVariableRead, Symbol> _table;
    private readonly ImmutableArray<Scope> _scopes;

    public static readonly Env Empty =
        new(
            table: ImmutableDictionary.Create<AstVariableRead, Symbol>(
                ReferenceEqualityComparer.Instance
            ),
            scopes: []
        );

    private Env(
        ImmutableDictionary<AstVariableRead, Symbol> table,
        ImmutableArray<Scope> scopes
    )
    {
        _table = table;
        _scopes = scopes;
    }

    public Symbol this[AstVariableRead read] {
        [Pure]
        get => _table.TryGetValue(read, out var symbol) ? symbol : Symbol.Undefined;
    }

    [MustUseReturnValue]
    public Env Resolve(IAst ast, ProblemList problems)
    {
        var resolver = new Resolver(
            _table.ToBuilder(),
            _scopes.ToBuilder(),
            problems
        );

        resolver.Resolve(ast);

        var (table, scopes) = resolver.Drain();
        return new Env(table, scopes);
    }

    public static class Problems
    {
        public static ProblemDesc UndefinedVariable(string name) => new() {
            Id = "env/undefined-variable",
            Message = $"could not find a declaration for '{name}'",
        };
    }
}

file sealed class Resolver(
    ImmutableDictionary<AstVariableRead, Symbol>.Builder table,
    ImmutableArray<Env.Scope>.Builder scopes,
    ProblemList problems
)
{
    public void Resolve(IAst ast)
    {
        switch (ast) {
            case AstVariableDecl decl: {
                decl.Value.Walk(Resolve);
                Declare(decl);
                break;
            }

            case AstVariableRead read: {
                Bind(read);
                break;
            }

            case AstBlock block: {
                DelimitScope(() => {
                    block.Stmts.Walk(Resolve);
                });
                break;
            }

            default: {
                ast.Walk(Resolve);
                break;
            }
        }
    }

    public (ImmutableDictionary<AstVariableRead, Symbol>, ImmutableArray<Env.Scope>) Drain()
    {
        return (table.ToImmutable(), scopes.DrainToImmutable());
    }

    private void Declare(AstVariableDecl decl)
    {
        scopes.Add(new Env.Scope(decl.Name.Value, new Symbol()));
    }

    private void Bind(AstVariableRead read)
    {
        foreach (var scope in scopes.AsEnumerable().Reverse()) {
            if (scope.Name == read.Name) {
                table.Add(read, scope.Symbol);
                return;
            }
        }

        problems.Report(Env.Problems.UndefinedVariable(read.Name), read.Loc);
        table.Add(read, Symbol.Undefined);
    }

    private void DelimitScope(Action action)
    {
        var prevCount = scopes.Count;
        action();

        var delta = scopes.Count - prevCount;
        scopes.RemoveRange(prevCount, delta);
    }
}
