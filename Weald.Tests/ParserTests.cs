using Weald.Core;

namespace Weald.Tests;

public class ParserTests : BaseTest
{
    private static readonly VerifySettings Settings = new();

    static ParserTests()
    {
        Settings.UseDirectory("Snapshots/Parser");
        Settings.UseTypeName("Parser");
    }

    private static SettingsTask Verify(string text)
    {
        var source = Source.FromString("", text);
        var lexer = Lexer.Tokenise(source);
        var result = Parser.Parse(lexer);

        return Verifier
            .Verify(AstPrinter.Print(result.Ast), Settings)
            .AppendValue("problems", result.Problems);
    }

    [Test]
    public Task Empty() => Verify("");

    [Test]
    public Task InvalidToken() => Verify("~");

    [Test]
    public Task SingleName() => Verify("name");

    [Test]
    public Task VariableDeclSimple() => Verify("let boolean = true");

    [TestCase("And", "true && false")]
    [TestCase("Or", "true || false")]
    [TestCase("AndWithOr", "true && (true || false)")]
    [TestCase("OrWithAnd", "(true && false) || true")]
    [TestCase("AndWithOrAmbiguous", "true && false || true")]
    [TestCase("OrWithAndAmbiguous", "true || false && true")]
    public Task ExprLogic(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Equal", "1 == 1")]
    [TestCase("NotEqual", "2 != 3")]
    [TestCase("Less", "4 < 5")]
    [TestCase("LessEqual", "6 <= 6")]
    [TestCase("Greater", "8 > 7")]
    [TestCase("GreaterEqual", "9 >= 8")]
    [TestCase("Ambiguous", "1 == 2 != 3")]
    [TestCase("Missing", "1 >= ")]
    [TestCase("MissingGrouped", "1 < (2 >= )")]
    public Task ExprCmp(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Add", "6 + 3.14")]
    [TestCase("Sub", "1 - -5")]
    [TestCase("Mul", "e * i")]
    [TestCase("Div", "10 / 2")]
    [TestCase("Mod", "999 % 7")]
    [TestCase("All", "a + b * c / d - e % f")]
    public Task ExprAddMul(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Single", "2^32")]
    [TestCase("Stacked", "2^3^4^5")]
    [TestCase("Unstacked", "((2^3)^4)^5")]
    public Task ExprPow(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Not", "!a")]
    [TestCase("Neg", "-b")]
    [TestCase("Pos", "+c")]
    public Task ExprUnary(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("OverflowPos", "170_141_183_460_469_231_731_687_303_715_884_105_728")]
    [TestCase("OverflowNeg", "-170_141_183_460_469_231_731_687_303_715_884_105_729")]
    [TestCase("HexOverflowPos", "0x80_000_000_000_000_000_000_000_000_000_000")]
    [TestCase("HexOverflowNeg", "-0x80_000_000_000_000_000_000_000_000_000_001")]
    [TestCase("BinOverflowPos", "0b10_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000")]
    [TestCase("BinOverflowNeg", "-0b10_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_000_001")]
    public Task Ints(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("OverflowPos", "+1.8e308")]
    [TestCase("OverflowNeg", "-1.8e308")]
    public Task Floats(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Empty", "{}")]
    [TestCase("Single", "{ 1 + 2 }")]
    [TestCase("Multiple", "{\nfoo\nbar\n}")]
    [TestCase("Unclosed", "{ foo\nbar ")]
    [TestCase("Unopened", "foo }")]
    [TestCase("MultipleSingleline", "{ foo bar }")]
    public Task Blocks(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Single", "(1 < 2)")]
    [TestCase("Empty", "()")]
    [TestCase("Multiple", "(foo bar)")]
    [TestCase("Unclosed", "(foo")]
    [TestCase("Unopened", "foo)")]
    [TestCase("MultipleUnclosed", "(foo bar")]
    [TestCase("OverridePrecedence", "(1 + 2) * 3")]
    public Task Groups(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Simple", "if true {}")]
    [TestCase("Condition", "if 1 == 2 {}")]
    [TestCase("Else", "if !true {} else {}")]
    [TestCase("ElseIf", "if !true {} else if true {}")]
    [TestCase("NoPredicate", "if ) {}")]
    [TestCase("NoThenBody", "if true 1")]
    [TestCase("NoElseBody", "if true {} else 1")]
    public Task Ifs(string name, string sut) => Verify(sut).UseTextForParameters(name);

    [TestCase("Simple", "if true ? 0 : 1")]
    [TestCase("NoCondition", "if ? 0 : 1")]
    [TestCase("NoThenBody", "if true ? ) : 1")]
    [TestCase("NoElseBody", "if true ? 0 1")]
    [TestCase("BlockInThen", "if true ? {} : 1")]
    [TestCase("BlockInElse", "if true ? 0 : {}")]
    [TestCase("BlockNested", "if true ? ({} + 2) : (1 + {})")]
    public Task Ternary(string name, string sut) => Verify(sut).UseTextForParameters(name);
}
