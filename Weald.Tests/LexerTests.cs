using System.Buffers;
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

    private static readonly Gen<string> NameEnd = Gen.String[Gen.Char["?!"], 0, 1];

    [Test]
    public void NamesAscii()
    {
        Gen.Select(NameStartAscii, Gen.String[NameContinueAscii], NameEnd)
            .Select((s, c, e) => s + c + e)
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
        Gen.Select(NameStartAscii, Gen.String[NameContinueAscii, 1, 100], NameEnd)
            .Select((s, c, e) => s + "-" + c + e)
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
    public Task NamesUnicode() => Verify("おやすみなさい a山b 本-ℹ देवनागरी?");

    [Test]
    public Task NamesOverlongFinal() => Verify("foo?? bar!? bux?! baz!!");

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

    private static readonly Gen<string> NumberSign = Gen.String[Gen.Char["+-"], 0, 1];

    private static readonly Gen<string> IntegerDecPositive =
        Gen.Select(
            Gen.String[Gen.Char["0123456789"], 1, 1],
            Gen.String[Gen.Char["0123456789_"], 0, 50]
        )
            .Select((first, rest) => first + rest)
            .Where(s => !(s.Contains("__", StringComparison.Ordinal) || s.EndsWith('_')));

    private static readonly Gen<string> IntegerDec =
        Gen.Select(NumberSign, IntegerDecPositive).Select((sign, i) => sign + i);

    [Test]
    public void Integers()
    {
        IntegerDec.Sample(sut => {
            AssertLex(sut,
                $"Token.Integer/Dec={sut.Escape()}@0:{sut.Length}",
                $"Token.End@{sut.Length}:0"
            );
        });
    }

    [Test]
    public void IntegersHex()
    {
        Gen.Select(
            NumberSign,
            Gen.String[Gen.Char["0123456789abcdefABCDEF"], 1, 1],
            Gen.String[Gen.Char["0123456789abcdefABCDEF_"], 0, 50]
        )
            .Select((sign, first, rest) => sign + "0x" + first + rest)
            .Where(s => !(s.Contains("__", StringComparison.Ordinal) || s.EndsWith('_')))
            .Sample(sut => {
                var text = sut.Replace("0x", "", StringComparison.Ordinal).Escape();
                AssertLex(sut,
                    $"Token.Integer/Hex={text}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public void IntegersBin()
    {
        Gen.Select(
            NumberSign,
            Gen.String[Gen.Char["01"], 1, 1],
            Gen.String[Gen.Char["01_"], 0, 50]
        )
            .Select((sign, first, rest) => sign + "0b" + first + rest)
            .Where(s => !(s.Contains("__", StringComparison.Ordinal) || s.EndsWith('_')))
            .Sample(sut => {
                var text = sut.Replace("0b", "", StringComparison.Ordinal).Escape();
                AssertLex(sut,
                    $"Token.Integer/Bin={text}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public Task IntegersBadSeparation() =>
        Verify("1__2 -9__ +123_456___7 0x__A 0xCA__FE 0b__0 0b10__11");

    [Test]
    public Task IntegersBadSuffix() => Verify("0a 123_c 987- 0x- 0xAbck 0b- 0b1112");

    private static readonly Gen<string> FloatExponent = IntegerDec.Select(n => $"e{n}");

    [Test]
    public void Floats()
    {
        Gen.Select(
            IntegerDec,
            Gen.OneOf(
                Gen.Select(IntegerDecPositive, Gen.OneOf(Gen.Const(""), FloatExponent))
                    .Select((frac, exp) => $".{frac}{exp}"),
                FloatExponent
            )
        )
            .Select((i, f) => i + f)
            .Sample(sut => {
                AssertLex(sut,
                    $"Token.Float={sut.Escape()}@0:{sut.Length}",
                    $"Token.End@{sut.Length}:0"
                );
            });
    }

    [Test]
    public Task FloatCrazy() => Verify("-0.3_975e-83403267696694266");

    [Test]
    public Task FloatsBadSeparation() =>
        Verify("1__2.3__4 -314_.567 9e+56_ 910_e-2 98.76e_23");

    [Test]
    public Task FloatsBadSuffix() => Verify("3.14xl 75.43e 11.22- 66.55E");

    private static readonly SearchValues<char> EscapedChars = SearchValues.Create("\"\\\r\n");

    private static readonly Gen<string> StringWithEscapes =
        Gen
            .OneOf(
                Gen.String.Where(s => !s.ContainsAny(EscapedChars)),
                Gen.Char["\"\\enrt"].Select(c => $"\\{c}")
            )
            .Array[0, 50]
            .Select(xs => string.Join("", xs));

    [Test]
    public void StringsStd()
    {
        StringWithEscapes.Sample(sut => {
            AssertLex($"\"{sut}\"",
                $"Token.String=\"{sut}\"@0:{sut.Length + 2}",
                $"Token.End@{sut.Length + 2}:0"
            );
        });
    }

    [Test]
    public void StringsStdUnclosed()
    {
        StringWithEscapes.Sample(sut => {
            AssertLex($"\"{sut}",
                $"Token.Invalid=\"unclosed string literal\"@0:{sut.Length + 1}",
                $"Token.End@{sut.Length + 1}:0"
            );
        });
    }

    [Test]
    public void StringsStdUnclosedWithNewline()
    {
        Gen.Select(StringWithEscapes, Gen.Char["\r\n"]).Sample((sut, nl) => {
            AssertLex($"\"{sut}{nl}",
                $"Token.Invalid=\"newline in string literal; did you mean to place a '\\\\'" +
                    $" before the newline to form a line continuation?\"@0:{sut.Length + 1}",
                $"Token.Newline@{sut.Length + 1}:1",
                $"Token.End@{sut.Length + 2}:0"
            );
        });
    }

    [Test]
    public Task StringsUnicode() => Verify(
        """
        "\xD2\x83\xE9" "\xff\x10\x0f" "\u1234\u5678\u9aBc\ufeff"
        "\u{0}\u{0065}\u{10FFFF}\u{00FE4C}"
        """
    );

    [Test]
    public Task StringsInvalidEscapes() => Verify(
        """
        "\a" "\b\c" "\{\\\x" "\xzx" "\xaZ" "\u\u0\u00\u000\u000g\ug0065"
        "\u{ffffff}\u{0065\u{x}\u}"
        """
    );

    [Test]
    public Task StringsContinuation() => Verify(
        """
        "foo\
        bar"
        "baz\

                     bux"
        """
    );

    private static readonly Gen<string> StringRaw =
        Gen.String.Where(s => !s.Contains('`', StringComparison.Ordinal));

    [Test]
    public void StringsRaw()
    {
        StringRaw.Sample(sut => {
            AssertLex($"`{sut}`",
                $"Token.String={sut.Escape()}@0:{sut.Length + 2}",
                $"Token.End@{sut.Length + 2}:0"
            );
        });
    }

    [Test]
    public void StringsRawUnclosed()
    {
        StringRaw.Sample(sut => {
            AssertLex($"`{sut}",
                $"Token.Invalid=\"unclosed raw string literal\"@0:{sut.Length + 1}",
                $"Token.End@{sut.Length + 1}:0"
            );
        });
    }

    [Test]
    public void StringsRawUnclosedWithNewline()
    {
        Gen.Select(StringRaw, Gen.Char["\r\n"]).Sample((sut, nl) => {
            AssertLex($"`{sut}{nl}",
                $"Token.Invalid=\"newline in raw string literal\"@0:{sut.Length + 1}",
                $"Token.Newline@{sut.Length + 1}:1",
                $"Token.End@{sut.Length + 2}:0"
            );
        });
    }
}
