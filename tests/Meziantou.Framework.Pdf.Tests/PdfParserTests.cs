using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Parsing;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfParserTests
{
    [Fact]
    public void ParsesEveryKindOfObject()
    {
        var value = Parse("<< /Bool true /Null null /Int -12 /Real 1.5 /Name /N /String (s) /Hex <41> /Array [1 (a) /b] /Dict << /Nested 1 >> /Ref 12 0 R >>");

        var dictionary = Assert.IsType<PdfDictionary>(value);
        Assert.HasCount(10, dictionary);
        Assert.Same(PdfBoolean.True, dictionary[PdfName.Create("Bool")]);
        Assert.Same(PdfNull.Instance, dictionary[PdfName.Create("Null")]);
        Assert.Equal(-12, Assert.IsType<PdfInteger>(dictionary[PdfName.Create("Int")]).Value);
        Assert.Equal(1.5, Assert.IsType<PdfReal>(dictionary[PdfName.Create("Real")]).Value);
        Assert.Equal("N", Assert.IsType<PdfName>(dictionary[PdfName.Create("Name")]).Value);
        Assert.False(Assert.IsType<PdfString>(dictionary[PdfName.Create("String")]).IsHexadecimal);
        Assert.True(Assert.IsType<PdfString>(dictionary[PdfName.Create("Hex")]).IsHexadecimal);
        Assert.HasCount(3, Assert.IsType<PdfArray>(dictionary[PdfName.Create("Array")]));
        Assert.IsType<PdfDictionary>(dictionary[PdfName.Create("Dict")]);
        var reference = Assert.IsType<PdfReference>(dictionary[PdfName.Create("Ref")]);
        Assert.Equal(12, reference.ObjectNumber);
        Assert.Equal(0, reference.Generation);
    }

    [Fact]
    public void NullValueIsAnAbsentEntry()
    {
        var dictionary = Assert.IsType<PdfDictionary>(Parse("<< /A null >>"));

        Assert.True(dictionary.ContainsKey(PdfName.Create("A")));
        Assert.Null(dictionary.GetResolved(PdfName.Create("A")));
    }

    [Fact]
    public void IntegersThatAreNotReferences()
    {
        var array = Assert.IsType<PdfArray>(Parse("[1 2 3 0 R 4]"));

        Assert.HasCount(4, array);
        Assert.IsType<PdfInteger>(array[0]);
        Assert.IsType<PdfReference>(array[2]);
        Assert.IsType<PdfInteger>(array[3]);
    }

    [Fact]
    public void LastDuplicateKeyWins()
    {
        var dictionary = Assert.IsType<PdfDictionary>(Parse("<< /A 1 /A 2 >>"));

        Assert.HasCount(1, dictionary);
        Assert.Equal(2, dictionary.GetInteger(PdfName.Create("A")));
    }

    [Fact]
    public void KeyWithoutValueEndsTheDictionary()
    {
        var dictionary = Assert.IsType<PdfDictionary>(Parse("<< /A 1 /B >>"));

        Assert.HasCount(1, dictionary);
    }

    [Fact]
    public void UnclosedArrayThrows()
    {
        Assert.Throws<InvalidPdfContentException>(() => Parse("[1 2"));
    }

    [Fact]
    public void NestingDepthIsLimited()
    {
        var text = new string('[', 300) + new string(']', 300);

        Assert.Throws<PdfResourceLimitException>(() => Parse(text));
    }

    [Fact]
    public void IndirectObjectWithStream()
    {
        var lexer = new PdfLexer("7 0 obj\n<< /Length 5 >>\nstream\r\nHello\nendstream\nendobj"u8.ToArray());
        var parser = new PdfObjectParser(lexer, new PdfIndirectObjectCollection(), 32);

        var value = parser.ParseIndirectObject(out var number, out var generation);

        Assert.Equal(7, number);
        Assert.Equal(0, generation);
        var stream = Assert.IsType<PdfStream>(value);
        Assert.Equal("Hello", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Theory]
    [InlineData("<< /Length 99 >>\nstream\nHello\nendstream")]
    [InlineData("<< /Length 2 >>\nstream\nHello\nendstream")]
    [InlineData("<< >>\nstream\nHello\r\nendstream")]
    [InlineData("<< /Length -1 >>\nstream\rHello\rendstream")]
    public void StreamWithWrongLengthEndsAtEndstream(string body)
    {
        var lexer = new PdfLexer(Encoding.ASCII.GetBytes("1 0 obj\n" + body + "\nendobj"));
        var parser = new PdfObjectParser(lexer, new PdfIndirectObjectCollection(), 32);

        var stream = Assert.IsType<PdfStream>(parser.ParseIndirectObject(out _, out _));

        Assert.Equal("Hello", Encoding.ASCII.GetString(stream.EncodedData.Span));
    }

    [Fact]
    public void EmptyIndirectObjectIsNull()
    {
        var lexer = new PdfLexer("1 0 obj endobj"u8.ToArray());
        var parser = new PdfObjectParser(lexer, new PdfIndirectObjectCollection(), 32);

        Assert.Same(PdfNull.Instance, parser.ParseIndirectObject(out _, out _));
    }

    [Fact]
    public void ObjectsRoundTripThroughTheirSyntax()
    {
        var text = "<</Type /Page /Kids [1 0 R (a\\(b\\)) <00FF>] /N -1.25 /Name /A#20B /T true /U null>>";

        Assert.Equal(text, Parse(text).ToString());
    }

    private static PdfObject Parse(string text)
    {
        var parser = new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(text)), new PdfIndirectObjectCollection(), 256);
        return parser.ParseObject();
    }
}
