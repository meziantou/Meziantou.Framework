using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfLexerTests
{
    [Theory]
    [InlineData("123", 123L)]
    [InlineData("+17", 17L)]
    [InlineData("-98", -98L)]
    [InlineData("0", 0L)]
    [InlineData("00987", 987L)]
    public void Integers(string text, long expected)
    {
        var token = new PdfLexer(Encoding.ASCII.GetBytes(text)).Next();

        Assert.Equal(PdfTokenKind.Integer, token.Kind);
        Assert.Equal(expected, token.IntegerValue);
    }

    [Theory]
    [InlineData("34.5", 34.5)]
    [InlineData("-3.62", -3.62)]
    [InlineData("+123.6", 123.6)]
    [InlineData("4.", 4)]
    [InlineData("-.002", -0.002)]
    [InlineData("0.0", 0)]
    [InlineData("--5.5", -5.5)]
    [InlineData("4.-5", 4)]
    [InlineData("-", 0)]
    [InlineData(".", 0)]
    [InlineData("99999999999999999999", 99999999999999999999.0)]
    public void Reals(string text, double expected)
    {
        var token = new PdfLexer(Encoding.ASCII.GetBytes(text)).Next();

        Assert.True(token.Kind is PdfTokenKind.Real or PdfTokenKind.Integer);
        Assert.Equal(expected, token.Kind == PdfTokenKind.Real ? token.RealValue : token.IntegerValue, 9);
    }

    [Theory]
    [InlineData("(This is a string)", "This is a string")]
    [InlineData("(Strings may contain newlines\nand such.)", "Strings may contain newlines\nand such.")]
    [InlineData("(Strings may contain balanced parentheses ( ) and\nspecial characters (*!&}^% and so on).)", "Strings may contain balanced parentheses ( ) and\nspecial characters (*!&}^% and so on).")]
    [InlineData("()", "")]
    [InlineData(@"(\(\)\\\n\r\t\b\f)", "()\\\n\r\t\b\f")]
    [InlineData("(These \\\ntwo strings \\\r\nare the same.)", "These two strings are the same.")]
    [InlineData(@"(\0533)", "+3")]
    [InlineData(@"(\053)", "+")]
    [InlineData(@"(\53)", "+")]
    [InlineData(@"(\q)", "q")]
    [InlineData("(a\r\nb\rc)", "a\nb\nc")]
    public void LiteralStrings(string text, string expected)
    {
        var lexer = new PdfLexer(Encoding.Latin1.GetBytes(text));
        var token = lexer.Next();

        Assert.Equal(PdfTokenKind.LiteralString, token.Kind);
        Assert.Equal(expected, Encoding.Latin1.GetString(lexer.DecodeLiteralString(token)));
        Assert.Equal(text.Length, lexer.Position);
    }

    [Theory]
    [InlineData("<4E6F762073686D6F7A206B6120706F702E>", "Nov shmoz ka pop.")]
    [InlineData("<901FA3>", "\u0090\u001F\u00A3")]
    [InlineData("<901FA>", "\u0090\u001F\u00A0")]
    [InlineData("<48 65\n6C6C 6f>", "Hello")]
    [InlineData("<>", "")]
    public void HexadecimalStrings(string text, string expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Next();

        Assert.Equal(PdfTokenKind.HexString, token.Kind);
        Assert.Equal(expected, Encoding.Latin1.GetString(lexer.DecodeHexString(token)));
    }

    [Theory]
    [InlineData("/Name1", "Name1")]
    [InlineData("/A;Name_With-Various***Characters?", "A;Name_With-Various***Characters?")]
    [InlineData("/1.2", "1.2")]
    [InlineData("/$$", "$$")]
    [InlineData("/@pattern", "@pattern")]
    [InlineData("/.notdef", ".notdef")]
    [InlineData("/Lime#20Green", "Lime Green")]
    [InlineData("/paired#28#29parentheses", "paired()parentheses")]
    [InlineData("/The_Key_of_F#23_Minor", "The_Key_of_F#_Minor")]
    [InlineData("/A#42", "AB")]
    [InlineData("/", "")]
    [InlineData("/Caf#C3#A9", "Caf\u00E9")]
    public void Names(string text, string expected)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes(text));
        var token = lexer.Next();

        Assert.Equal(PdfTokenKind.Name, token.Kind);
        Assert.Equal(expected, PdfName.FromBytes(lexer.DecodeName(token)).Value);
    }

    [Fact]
    public void DelimitersCommentsAndKeywords()
    {
        var lexer = new PdfLexer("<< /A [1 2] >> % comment\r\n{ obj } true"u8.ToArray());
        PdfTokenKind[] expected =
        [
            PdfTokenKind.DictionaryStart, PdfTokenKind.Name, PdfTokenKind.ArrayStart, PdfTokenKind.Integer, PdfTokenKind.Integer, PdfTokenKind.ArrayEnd, PdfTokenKind.DictionaryEnd,
            PdfTokenKind.ProcedureStart, PdfTokenKind.Keyword, PdfTokenKind.ProcedureEnd, PdfTokenKind.Keyword, PdfTokenKind.EndOfInput,
        ];
        foreach (var kind in expected)
        {
            Assert.Equal(kind, lexer.Next().Kind);
        }
    }

    [Fact]
    public void WhitespaceIncludesNullAndFormFeed()
    {
        var lexer = new PdfLexer("\0\t\n\f\r 12"u8.ToArray());

        Assert.Equal(12, lexer.Next().IntegerValue);
    }

    [Fact]
    public void UnterminatedStringsEndAtTheEndOfInput()
    {
        var lexer = new PdfLexer("(abc"u8.ToArray());
        var token = lexer.Next();

        Assert.Equal(PdfTokenKind.LiteralString, token.Kind);
        Assert.Equal("abc", Encoding.ASCII.GetString(lexer.DecodeLiteralString(token)));
        Assert.Equal(PdfTokenKind.EndOfInput, lexer.Next().Kind);
    }
}
