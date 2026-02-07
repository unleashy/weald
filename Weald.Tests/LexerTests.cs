using Weald.Core;

namespace Weald.Tests;

public class LexerTests
{
    private static readonly VerifySettings Settings = new();

    static LexerTests()
    {
        Settings.UseDirectory("Snapshots/Lexer");
        Settings.UseTypeName("Lexer");
    }

    private static SettingsTask Verify(string text)
    {
        var source = Source.FromString("", text);
        var lexer = new Lexer(source);

        return Verifier.Verify(string.Join('\n', lexer), Settings);
    }

    [Test]
    public Task Empty() => Verify("");

    [Test]
    public Task OnlyWhitespace() => Verify(" \t\u200E  \u200F\t ");

    [Test]
    public Task Newlines() => Verify("\n \r\n \r  \n\r");

    [TestCase("EmptyEnd", "--")]
    [TestCase("EmptyNl", "--\n")]
    [TestCase("Basic", "-- abcde\r\n")]
    [TestCase("Unicode", "-- 🌈🌈🌈🌈\n")]
    [TestCase("MultipleNls", "\n -- \r --")]
    [TestCase("Dashes", "-------------------")]
    public Task Comments(string name, string text) => Verify(text).UseTextForParameters(name);

    [Test]
    public Task Bom() => Verify("\uFEFF");

    [TestCase("EmptyEnd", "#!")]
    [TestCase("EmptyNl", "#!\n")]
    [TestCase("Basic", "#! foobar\r\n")]
    [TestCase("Unicode", "#! 🌈🌈🌈🌈\n")]
    [TestCase("WithBom", "\uFEFF#!abcde\n")]
    public Task Hashbang(string name, string text) => Verify(text).UseTextForParameters(name);

    [Test]
    public Task Punctuation() =>
        Verify(@". , : \ | + - * / % ^ ! && || < <= == != >= > = ( ) [ ] { }");

    [Test]
    public Task InvalidPunctuation() => Verify(@"#");
}
