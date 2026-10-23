using System.Text;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Text;
using Meziantou.Framework.Pdf.Writing;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfObjectTests
{
    [Theory]
    [InlineData("Hello", new byte[] { 0x48, 0x65, 0x6C, 0x6C, 0x6F })]
    [InlineData("\u20AC\u2022\u02D8", new byte[] { 0xA0, 0x80, 0x18 })]
    [InlineData("\u00E9", new byte[] { 0xE9 })]
    [InlineData("\u4E2D", new byte[] { 0xFE, 0xFF, 0x4E, 0x2D })]
    [InlineData("\u00A0", new byte[] { 0xFE, 0xFF, 0x00, 0xA0 })]
    [InlineData("", new byte[0])]
    public void TextStrings(string text, byte[] expected)
    {
        var value = PdfString.FromText(text);

        Assert.Equal(expected, value.Bytes.ToArray());
        Assert.Equal(text, value.ToText());
    }

    [Theory]
    [InlineData(new byte[] { 0xEF, 0xBB, 0xBF, 0xC3, 0xA9 }, "\u00E9")]
    [InlineData(new byte[] { 0xFF, 0xFE, 0x41, 0x00 }, "A")]
    [InlineData(new byte[] { 0xFE, 0xFF, 0x00, 0x41, 0x00 }, "A")]
    [InlineData(new byte[] { 0x7F, 0x9F, 0xAD }, "\uFFFD\uFFFD\uFFFD")]
    public void DecodeTextStrings(byte[] bytes, string expected) => Assert.Equal(expected, PdfString.FromBytes(bytes).ToText());

    [Fact]
    public void EveryPdfDocEncodingCharacterRoundTrips()
    {
        for (var code = 0; code < 256; code++)
        {
            var c = PdfTextStringEncoding.DecodePdfDocEncoding((byte)code);
            if (c == '\uFFFD' || code is < 0x18 and not (9 or 10 or 13))
                continue;

            Assert.True(PdfTextStringEncoding.TryEncodePdfDocEncoding(c, out var encoded));
            Assert.Equal(code, encoded);
        }
    }

    [Theory]
    [InlineData("D:20231023103400+02'00'", 2023, 10, 23, 10, 34, 0, 120)]
    [InlineData("D:20231023103400+02'00", 2023, 10, 23, 10, 34, 0, 120)]
    [InlineData("D:20231023103400-05'30", 2023, 10, 23, 10, 34, 0, -330)]
    [InlineData("D:20231023103400Z", 2023, 10, 23, 10, 34, 0, 0)]
    [InlineData("D:20231023103400Z00'00'", 2023, 10, 23, 10, 34, 0, 0)]
    [InlineData("D:2023", 2023, 1, 1, 0, 0, 0, 0)]
    [InlineData("D:202310", 2023, 10, 1, 0, 0, 0, 0)]
    [InlineData("20231023", 2023, 10, 23, 0, 0, 0, 0)]
    public void Dates(string text, int year, int month, int day, int hour, int minute, int second, int offsetMinutes)
    {
        Assert.True(PdfDate.TryParse(text, out var value));
        Assert.Equal(new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(offsetMinutes)), value);
    }

    [Theory]
    [InlineData("")]
    [InlineData("D:")]
    [InlineData("D:20231323")]
    [InlineData("D:20230230")]
    [InlineData("D:2023102325")]
    [InlineData("garbage")]
    public void InvalidDates(string text) => Assert.False(PdfDate.TryParse(text, out _));

    [Fact]
    public void FormatDates()
    {
        Assert.Equal("D:20240102030405-05'00", PdfDate.Format(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.FromHours(-5))));
        Assert.Equal("D:20240102030405Z", PdfDate.Format(new DateTimeOffset(2024, 1, 2, 3, 4, 5, TimeSpan.Zero)));
    }

    [Theory]
    [InlineData(0.0, "0")]
    [InlineData(1.5, "1.5")]
    [InlineData(-1.25, "-1.25")]
    [InlineData(1e-7, "0")]
    [InlineData(0.1234567, "0.123457")]
    [InlineData(1e15, "1000000000000000")]
    [InlineData(612, "612")]
    public void NumbersAreWrittenWithoutExponent(double value, string expected) => Assert.Equal(expected, PdfNumberFormatter.Format(value));

    [Fact]
    public void NegativeZeroIsWrittenAsZero() => Assert.Equal("0", PdfNumberFormatter.Format(-0.0));

    [Fact]
    public void NamesAreComparedByTheirBytes()
    {
        Assert.Equal(PdfName.Create("Type"), new PdfName("Type"));
        Assert.Same(PdfName.Create("Type"), PdfName.Create("Type"));
        Assert.NotEqual(PdfName.Create("Type"), PdfName.Create("type"));
        Assert.Equal("/A#20B", new PdfName("A B").ToString());
        Assert.Equal("/Caf#C3#A9", new PdfName("Caf\u00E9").ToString());
        Assert.Equal("Caf\u00E9", new PdfName("Caf\u00E9").Value);
    }

    [Fact]
    public void StringsAreWrittenLiterallyOrInHexadecimal()
    {
        Assert.Equal("(a\\(b\\)\\\\c\\n)", PdfString.FromBytes("a(b)\\c\n"u8).ToString());
        Assert.Equal("<FEFF4E2D>", PdfString.FromText("\u4E2D").ToString());
        Assert.Equal("<41>", PdfString.FromBytes("A"u8, hexadecimal: true).ToString());
        Assert.Equal("(\\001xyzw)", PdfString.FromBytes(new byte[] { 1, (byte)'x', (byte)'y', (byte)'z', (byte)'w' }).ToString());
        Assert.Equal("<0178>", PdfString.FromBytes(new byte[] { 1, (byte)'x' }).ToString());
    }

    [Fact]
    public void DictionaryKeepsTheOrderOfItsEntries()
    {
        var dictionary = new PdfDictionary
        {
            [PdfName.Create("B")] = PdfInteger.Get(1),
            [PdfName.Create("A")] = PdfInteger.Get(2),
        };
        dictionary[PdfName.Create("B")] = PdfInteger.Get(3);

        Assert.Equal(["B", "A"], dictionary.Keys.Select(k => k.Value));
        Assert.Equal("<</B 3 /A 2>>", dictionary.ToString());
        dictionary[PdfName.Create("B")] = null;
        Assert.Equal("<</A 2>>", dictionary.ToString());
    }

    [Fact]
    public void ReferencesResolveThroughTheirCollection()
    {
        var objects = new PdfIndirectObjectCollection();
        var reference = objects.Add(PdfString.FromText("value"));
        var holder = new PdfDictionary { [PdfName.Create("Key")] = reference };

        Assert.Equal("value", holder.GetText(PdfName.Create("Key")));
        Assert.Equal("1 0 R", reference.ToString());
        objects.Set(reference, PdfInteger.Get(5));
        Assert.Equal(5, holder.GetInteger(PdfName.Create("Key")));
        Assert.Throws<ArgumentException>(() => objects.Add(reference));
        Assert.Throws<ArgumentException>(() => new PdfIndirectObjectCollection().Resolve(reference));
    }

    [Fact]
    public void RealsMustBeFinite()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfReal.Get(double.NaN));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfArray.FromNumbers(double.PositiveInfinity));
    }

    [Fact]
    public void StreamData()
    {
        var stream = new PdfStream();
        stream.SetData("data"u8);

        Assert.Equal(4, stream.Dictionary.GetInteger(PdfName.Create("Length")));
        Assert.Empty(stream.Filters);
        Assert.Equal("data", Encoding.ASCII.GetString(stream.GetDecodedData()));
    }
}
