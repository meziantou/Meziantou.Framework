using Meziantou.Framework.Pdf.Fonts;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfFontTests
{
    [Fact]
    public void StandardFontMetrics()
    {
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);

        // The widths of H, e, l, l, o in Helvetica.afm: 722 + 556 + 222 + 222 + 556
        Assert.Equal(22.78, helvetica.GetTextWidth("Hello", 10), 10);
        Assert.Equal("Helvetica", helvetica.PostScriptName);
        Assert.Equal(718, helvetica.Ascent);
        Assert.Equal(-207, helvetica.Descent);
        Assert.Equal(718, helvetica.CapHeight);
    }

    [Fact]
    public void CourierIsMonospaced()
    {
        var courier = PdfFont.GetStandardFont(PdfStandardFont.Courier);

        Assert.Equal(courier.GetTextWidth("iiii", 12), courier.GetTextWidth("WWWW", 12));
        Assert.Equal(4 * 600 * 12 / 1000.0, courier.GetTextWidth("WWWW", 12), 10);
    }

    [Theory]
    [InlineData(PdfStandardFont.TimesRoman, "Times-Roman")]
    [InlineData(PdfStandardFont.TimesBold, "Times-Bold")]
    [InlineData(PdfStandardFont.TimesItalic, "Times-Italic")]
    [InlineData(PdfStandardFont.TimesBoldItalic, "Times-BoldItalic")]
    [InlineData(PdfStandardFont.Helvetica, "Helvetica")]
    [InlineData(PdfStandardFont.HelveticaBold, "Helvetica-Bold")]
    [InlineData(PdfStandardFont.HelveticaOblique, "Helvetica-Oblique")]
    [InlineData(PdfStandardFont.HelveticaBoldOblique, "Helvetica-BoldOblique")]
    [InlineData(PdfStandardFont.Courier, "Courier")]
    [InlineData(PdfStandardFont.CourierBold, "Courier-Bold")]
    [InlineData(PdfStandardFont.CourierOblique, "Courier-Oblique")]
    [InlineData(PdfStandardFont.CourierBoldOblique, "Courier-BoldOblique")]
    [InlineData(PdfStandardFont.Symbol, "Symbol")]
    [InlineData(PdfStandardFont.ZapfDingbats, "ZapfDingbats")]
    public void EveryStandardFontHasItsMetrics(PdfStandardFont font, string name)
    {
        var pdfFont = PdfFont.GetStandardFont(font);

        Assert.Equal(name, pdfFont.PostScriptName);
        Assert.True(pdfFont.Ascent > 0);
        Assert.True(pdfFont.Descent < 0);
    }

    [Fact]
    public void LatinFontsEncodeWinAnsi()
    {
        var times = PdfFont.GetStandardFont(PdfStandardFont.TimesRoman);

        Assert.True(times.CanEncode("Caf\u00E9 \u20AC \u0152uvre \u2014 \u201Cquoted\u201D \u00A0\u00AD"));
        Assert.False(times.CanEncode("\u4E2D"));
        Assert.False(times.CanEncode("\u0100"));
        Assert.Equal([0x43, 0x80, 0x97, 0xA0], times.Encode("C\u20AC\u2014\u00A0"));
        Assert.Throws<ArgumentException>(() => times.Encode("\u4E2D"));
    }

    [Fact]
    public void SymbolFontsUseTheirOwnEncoding()
    {
        var symbol = PdfFont.GetStandardFont(PdfStandardFont.Symbol);
        var dingbats = PdfFont.GetStandardFont(PdfStandardFont.ZapfDingbats);

        Assert.Equal([0x61, 0x62], symbol.Encode("\u03B1\u03B2"));
        Assert.False(symbol.CanEncode("A\u4E2D"));
        Assert.Equal([0x21], dingbats.Encode("\u2701"));
    }

    [Theory]
    [InlineData("A", "A")]
    [InlineData("eacute", "\u00E9")]
    [InlineData("uni20AC", "\u20AC")]
    [InlineData("uni0041uni0042", null)]
    [InlineData("uni00410042", "AB")]
    [InlineData("u1F600", "\U0001F600")]
    [InlineData("f_f_i", "ffi")]
    [InlineData("a.sc", "a")]
    [InlineData("g42", null)]
    [InlineData("uniD800", null)]
    [InlineData("dalethatafpatah", "\u05D3\u05B2")]
    public void GlyphNames(string name, string? expected) => Assert.Equal(expected, GlyphList.ToUnicode(name));

    [Fact]
    public void ZapfDingbatsGlyphNames()
    {
        Assert.Equal("\u2701", GlyphList.ToUnicode("a1", zapfDingbats: true));
        Assert.Null(GlyphList.ToUnicode("a1"));
    }

    [Fact]
    public void Encodings()
    {
        Assert.Equal("quoteright", FontEncodings.Standard[39]);
        Assert.Equal("quotesingle", FontEncodings.WinAnsi[39]);
        Assert.Equal("Euro", FontEncodings.WinAnsi[0x80]);
        Assert.Equal("Adieresis", FontEncodings.MacRoman[0x80]);
        Assert.Equal("alpha", FontEncodings.Symbol[0x61]);
        Assert.Null(FontEncodings.Standard[0x80]);
    }

    [Theory]
    [InlineData("Arial", "Helvetica")]
    [InlineData("Arial,Bold", "Helvetica-Bold")]
    [InlineData("ABCDEF+Helvetica", "Helvetica")]
    [InlineData("TimesNewRomanPSMT", "Times-Roman")]
    [InlineData("CourierNew,BoldItalic", "Courier-BoldOblique")]
    [InlineData("Unknown", null)]
    public void StandardFontAliases(string baseFont, string? expected) => Assert.Equal(expected, Fonts.StandardFontMetrics.GetStandardName(baseFont));
}
