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

    [Fact]
    public void LeadingZerosOfALongIntegerAreNotCounted()
    {
        var token = new PdfLexer(Encoding.ASCII.GetBytes(new string('0', 2000) + "5")).Next();

        Assert.Equal(PdfTokenKind.Integer, token.Kind);
        Assert.Equal(5, token.IntegerValue);
    }

    [Theory]
    [InlineData(300, false)]
    [InlineData(400, true)]
    [InlineData(PdfLexer.MaxSignificantNumberLength, true)]
    [InlineData(PdfLexer.MaxSignificantNumberLength + 1, true)]
    [InlineData(5000, true)]
    public void IntegerBeyondTheRangeOfADoubleSaturates(int digits, bool saturates)
    {
        foreach (var sign in new[] { "", "-" })
        {
            var token = new PdfLexer(Encoding.ASCII.GetBytes(sign + "1" + new string('0', digits - 1))).Next();

            Assert.Equal(PdfTokenKind.Real, token.Kind);
            var expected = saturates ? double.MaxValue : double.Parse("1e" + (digits - 1).ToString(System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture);
            Assert.Equal(sign.Length == 0 ? expected : -expected, token.RealValue);
        }
    }

    [Fact]
    public void LongRealKeepsItsIntegerPart()
    {
        // The integer part is complete; the decimal digits beyond the buffer are far below the precision of a double
        var text = "000" + "12345" + new string('0', 300) + "." + new string('7', 2000);
        var token = new PdfLexer(Encoding.ASCII.GetBytes(text)).Next();

        Assert.Equal(PdfTokenKind.Real, token.Kind);
        Assert.Equal(12345e300, token.RealValue, 1e290);
    }

    [Fact]
    public void LongDecimalPartIsParsed()
    {
        var token = new PdfLexer(Encoding.ASCII.GetBytes("-0." + new string('0', 100) + "25" + new string('0', 2000))).Next();

        Assert.Equal(PdfTokenKind.Real, token.Kind);
        Assert.Equal(-2.5e-101, token.RealValue, 1e-110);
    }

    [Fact]
    public void LiteralStringWithoutEscapeIsCopiedOnce()
    {
        var content = new string('a', 1024 * 1024);
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes("(" + content + ")"));
        var token = lexer.Next();
        var bytes = lexer.DecodeLiteralString(token);

        // The smallest of several measurements: the measurement of a large allocation sometimes also counts
        // the unused part of the thread's allocation context (up to about 8 KB)
        var allocated = long.MaxValue;
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            bytes = lexer.DecodeLiteralString(token);
            allocated = Math.Min(allocated, GC.GetAllocatedBytesForCurrentThread() - before);
        }

        Assert.Equal(content, Encoding.ASCII.GetString(bytes));
        Assert.InRange(allocated, content.Length, content.Length + 1024);
    }

    [Fact]
    public void RepeatedNamesOfAFileAreOneInstance()
    {
        var file = HandWrittenPdf.CreateSimple()
            .Add(6, "<< /CustomKey /CustomValue >>")
            .Add(7, "<< /CustomKey /CustomValue >>");
        var document = PdfDocument.Open(file.ToArray());
        Assert.True(document.Objects.TryGetReference(6, out var first));
        Assert.True(document.Objects.TryGetReference(7, out var second));

        var a = Assert.IsType<PdfDictionary>(first.Resolve());
        var b = Assert.IsType<PdfDictionary>(second.Resolve());

        Assert.Same(a.Keys.Single(), b.Keys.Single());
        Assert.Same(a.GetName(PdfName.Create("CustomKey")), b.GetName(PdfName.Create("CustomKey")));
        Assert.Equal("CustomValue", a.GetName(PdfName.Create("CustomKey"))!.Value);
    }

    [Fact]
    public void NameCacheIsBounded()
    {
        var cache = new PdfNameCache();
        var names = new HashSet<PdfName>();
        for (var i = 0; i < 10_000; i++)
        {
            var lexer = new PdfLexer(Encoding.ASCII.GetBytes("/N" + i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            names.Add(cache.Get(lexer, lexer.Next()));
        }

        var longName = new PdfLexer(Encoding.ASCII.GetBytes("/" + new string('L', 100)));
        names.Add(cache.Get(longName, longName.Next()));

        Assert.HasCount(10_001, names);
        Assert.Equal(4096, cache.Count);
    }

    [Fact]
    public void NameCacheDecodesEscapes()
    {
        var cache = new PdfNameCache();
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes("/A#20B /Type /Typ#65"));

        var escaped = cache.Get(lexer, lexer.Next());
        var type = cache.Get(lexer, lexer.Next());
        var escapedType = cache.Get(lexer, lexer.Next());

        Assert.Equal("A B", escaped.Value);
        Assert.Same(PdfNames.Type, type);
        Assert.Same(PdfNames.Type, escapedType);
    }
}
