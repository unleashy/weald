namespace Weald.Core;

public static class Syntax
{
    [MustUseReturnValue]
    public static AstScript Analyse(Source source, ProblemList problems)
    {
        return Parser.Parse(Lexer.Tokenise(source), problems);
    }
}
