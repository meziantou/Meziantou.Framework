using System.Globalization;
using System.Text;
using Meziantou.Framework.Pdf.Content;
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

    [Theory]
    [InlineData("1 2.5 -3 /N (s) <41> [1 (a)] << /K 1 >> true false null op", "i1 r2.5 i-3 /N (73) <41> [i1 (61)] <</K i1>> true false null op;")]
    [InlineData("1 ] 2 >> 3 ) 4 > { 5 } m", "i1 i2 i3 i4 i5 m;")]
    [InlineData("1 2 m 3 [1 2", "i1 i2 m;rest:i3")]
    [InlineData("1 << /A 1", "rest:i1")]
    [InlineData("1 m [1 [2]", "i1 m;")]
    [InlineData("(a) [1", "rest:(61)")]
    [InlineData("1 2 3", "rest:i1 i2 i3")]
    [InlineData("[1 foo true 2] TJ", "[i1 null true i2] TJ;")]
    [InlineData("[1 endobj 2] TJ", "[i1] endobj;i2 TJ;")]
    [InlineData("[1 2 endobj", "[i1 i2] endobj;")]
    [InlineData("[1 ) 2 >> 3 { } ] TJ", "[i1 i2 i3] TJ;")]
    [InlineData("[[1] (a)] TJ", "[[i1] (61)] TJ;")]
    [InlineData("[<< /A 1 >>] TJ", "[<</A i1>>] TJ;")]
    [InlineData("[] [(a) <62> -12.5 /N] TJ", "[] [(61) <62> r-12.5 /N] TJ;")]
    [InlineData("(a\\(b\\)\\n\\101\r\nc\\\r\nd) Tj", "(612862290A410A6364) Tj;")]
    [InlineData("<4 1 4> <> <zz41> Tj", "<4140> <> <41> Tj;")]
    [InlineData("/A#20B /#41 / /A#2 Tf", "/A B /A / /A#2 Tf;")]
    [InlineData("BI /W 1 /H 1 ID \u0000\u00FF EI Q", "<</W i1 /H i1>> (00FF) BI;Q;")]
    [InlineData("1 % comment ]\n2 m", "i1 i2 m;")]
    [InlineData("--1 1.2.3 .5 - . -0.0 -0. 1e5 m", "i-1 r1.2 r0.5 i0 r0 r-0 r0 1e5;m;")]
    [InlineData("1 \u00E9t\u00E9", "i1 \u00E9t\u00E9;")]
    [InlineData("/P << /A [1 2] /B >> BDC", "/P <</A [i1 i2]>> BDC;")]
    public void ContentStreamOperations(string content, string expected)
    {
        Assert.Equal(expected, ReadContent(content));
    }

    [Fact]
    public void ContentStreamOperandsAreLimited()
    {
        var content = string.Concat(Enumerable.Repeat("1 ", 4100)) + "true [1] op";

        var reader = new ContentStreamReader(Encoding.Latin1.GetBytes(content));
        var operands = new List<PdfObject>();

        Assert.True(reader.TryRead(operands, out var name));
        Assert.Equal("op", name);
        Assert.HasCount(4097, operands);
        Assert.Same(PdfBoolean.True, operands[^1]);
        Assert.False(reader.TryRead(operands, out _));
    }

    [Fact]
    public void ContentStreamNestingDepthIsLimited()
    {
        var reader = new ContentStreamReader(Encoding.Latin1.GetBytes("1 m " + new string('[', 300) + new string(']', 300) + " TJ"));
        var operands = new List<PdfObject>();

        Assert.True(reader.TryRead(operands, out _));
        Assert.Throws<PdfResourceLimitException>(() => reader.TryRead(operands, out _));
    }

    [Fact]
    public void ContentStreamPositionIsAfterTheOperator()
    {
        var reader = new ContentStreamReader("1 2 m\n[(a)] TJ BT"u8.ToArray());
        var operands = new List<PdfObject>();

        Assert.True(reader.TryRead(operands, out _));
        Assert.Equal(5, reader.Position);
        Assert.True(reader.TryRead(operands, out _));
        Assert.Equal(14, reader.Position);
        Assert.True(reader.TryRead(operands, out _));
        Assert.Equal(17, reader.Position);
        Assert.False(reader.TryRead(operands, out _));
    }

    private static string ReadContent(string content)
    {
        var reader = new ContentStreamReader(Encoding.Latin1.GetBytes(content));
        var operands = new List<PdfObject>();
        var result = new StringBuilder();
        while (reader.TryRead(operands, out var name))
        {
            foreach (var operand in operands)
            {
                result.Append(Describe(operand)).Append(' ');
            }

            result.Append(name).Append(';');
        }

        if (operands.Count > 0)
        {
            result.Append("rest:").AppendJoin(' ', operands.Select(Describe));
        }

        return result.ToString();
    }

    private static string Describe(PdfObject value) => value switch
    {
        PdfInteger integer => "i" + integer.Value.ToString(CultureInfo.InvariantCulture),
        PdfReal real => "r" + real.Value.ToString("R", CultureInfo.InvariantCulture),
        PdfName name => "/" + name.Key,
        PdfString { IsHexadecimal: true } text => "<" + Convert.ToHexString(text.Span) + ">",
        PdfString text => "(" + Convert.ToHexString(text.Span) + ")",
        PdfArray array => "[" + string.Join(' ', array.Select(Describe)) + "]",
        PdfDictionary dictionary => "<<" + string.Join(' ', dictionary.Select(entry => "/" + entry.Key.Key + " " + Describe(entry.Value))) + ">>",
        PdfBoolean boolean => boolean.Value ? "true" : "false",
        PdfNull => "null",
        _ => value.GetType().Name,
    };

    private static PdfObject Parse(string text)
    {
        var parser = new PdfObjectParser(new PdfLexer(Encoding.Latin1.GetBytes(text)), new PdfIndirectObjectCollection(), 256);
        return parser.ParseObject();
    }
}
