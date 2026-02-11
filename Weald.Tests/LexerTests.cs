using System.Diagnostics.CodeAnalysis;
using CsCheck;
using Weald.Core;
using Weald.Extensions;

namespace Weald.Tests;

[SuppressMessage("ReSharper", "InvokeAsExtensionMember")]
public class LexerTests : BaseTest
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

    private static void AssertLex(string body, string token, params string[] rest)
    {
        var lexer = new Lexer(Source.FromString("", body));
        Assert.That(
            lexer.Select(t => t.ToString()),
            Is.EquivalentTo([token, ..rest]),
            actualExpression: body.Escape()
        );
    }

    [Test]
    public void Empty()
    {
        AssertLex("", "Token.End@0:0");
    }

    [Test]
    public void OnlyWhitespace()
    {
        Gen.String[" \t\u200E\u200F"].Sample(sut => {
            AssertLex(sut, $"Token.End@{sut.Length}:0");
        });
    }

    [Test]
    public void Newlines()
    {
        Gen.String[" \n\r"]
            .Where(s => s.Any(c => c is '\n' or '\r'))
            .Sample(sut => {
                AssertLex(sut, $"Token.Newline@0:{sut.Length}", $"Token.End@{sut.Length}:0");
            });
    }

    [TestCase("EmptyEnd", "--")]
    [TestCase("EmptyNl", "--\n")]
    [TestCase("Basic", "-- abcde\r\n")]
    [TestCase("Unicode", "-- 🌈🌈🌈🌈\n")]
    [TestCase("MultipleNls", "\n -- \r --")]
    [TestCase("Dashes", "-------------------")]
    public Task Comments(string name, string text) => Verify(text).UseTextForParameters(name);

    [Test]
    public void GenComments()
    {
        Gen.String
            .Where(s => !s.Any(c => c is '\n' or '\r'))
            .Select(s => $"--${s}\n")
            .Sample(sut => {
                AssertLex(sut, $"Token.Newline@0:{sut.Length}", $"Token.End@{sut.Length}:0");
            });
    }

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
        Verify(@"( ) [ ] { } * \ && % ^ | || + - , : . / < <= = == ! != >= >");

    [Test]
    public Task InvalidPunctuation() => Verify("#");

    [TestCase("AsciiControl", "\0\x1\x2\x3\x4\x5\x6\x7\x8\xB\xC\xE\xF\x7F\x1A")]
    public Task ForbiddenChars(string name, string text) =>
        Verify(text).UseTextForParameters(name);

    private static readonly Gen<char> NameStartAscii =
        Gen.Char["abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_"];

    private static readonly Gen<char> NameContinueAscii =
        Gen.Char["0123456789abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ_"];

    [Test]
    public void NamesAscii()
    {
        Gen.Select(NameStartAscii, Gen.String[NameContinueAscii])
            .Select((s, c) => s + c)
            .Sample(sut => {
                AssertLex(sut,
                    $"Token.Name={sut.Escape()}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public void NamesAsciiHyphenated()
    {
        Gen.Select(NameStartAscii, Gen.String[NameContinueAscii, 1, 100])
            .Select((s, c) => s + "-" + c)
            .Sample(sut => {
                AssertLex(sut,
                    $"Token.Name={sut.Escape()}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public Task NamesBadHyphenation() => Verify("abc- _-123- x-- _--_ ");

    [Test]
    public Task NamesUnicode() => Verify("おやすみなさい a山b 本-ℹ देवनागरी");

    [Test]
    public Task Keywords() => Verify("_ false true");

    [Test]
    public void NamesAreNormalised()
    {
        const string sut = "noe\u0308l-\u212b";
        var lexer = new Lexer(Source.FromString("", sut));

        var actual = lexer.Next();

        Assert.That(actual, Is.EqualTo(Token.Name("noël-Å", Loc.FromLength(0, sut.Length))));
    }

    [Test]
    public void Integers()
    {
        Gen.Select(
            Gen.String[Gen.Char["+-"], 0, 1],
            Gen.String[Gen.Char["0123456789"], 1, 1],
            Gen.String[Gen.Char["0123456789_"], 0, 50]
        )
            .Select((sign, first, rest) => sign + first + rest)
            .Where(s => !(s.Contains("__") || s.EndsWith('_')))
            .Sample(sut => {
                AssertLex(sut,
                    $"Token.Integer={sut.Escape()}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public Task IntegersBadSeparation() => Verify("1__2 -9__ +123_456___7");

    [Test]
    public Task IntegersBadSuffix() => Verify("0a 123_c 987-");
}
