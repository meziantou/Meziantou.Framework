using System.Buffers.Binary;
using System.Globalization;
using Meziantou.Framework.Fonts;
using Meziantou.Framework.Pdf.Fonts;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.TestHarness;

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

    [Theory]
    [InlineData("noto-sans/NotoSans-Regular.subset.ttf", "Office AVATAR \u00E9t\u00E9 \u201Cfine\u201D \u2014 1\u20AC")]
    [InlineData("noto-sans-cff/NotoSans-Regular.subset.otf", "Office AVATAR \u00E9t\u00E9 \u201Cfine\u201D \u2014 1\u20AC")]
    [InlineData("noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf", "\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002")]
    [InlineData("noto-naskh-arabic/NotoNaskhArabic-Regular.subset.ttf", "\u0645\u0631\u062D\u0628\u0627 123")]
    [InlineData("noto-sans-devanagari/NotoSansDevanagari-Regular.ttf", "\u0928\u092E\u0938\u094D\u0924\u0947 \u0915\u093F\u0924\u093E\u092C")]
    public void EmbeddedFontTextCanBeExtracted(string fontPath, string text)
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / fontPath);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText(text, font, 12, 50, 800);

        var reopened = PdfDocument.Open(document.ToByteArray());

        Assert.Equal(text, reopened.Pages[0].ExtractText());
    }

    [Theory]
    [InlineData("noto-sans/NotoSans-Regular.subset.ttf", "CIDFontType2", "FontFile2")]
    [InlineData("noto-sans-cff/NotoSans-Regular.subset.otf", "CIDFontType0", "FontFile3")]
    [InlineData("noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf", "CIDFontType0", "FontFile3")]
    public void EmbeddedFontObjects(string fontPath, string cidFontType, string fontFileKey)
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / fontPath);
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Hi!", font, 12, 50, 800);

        var reopened = PdfDocument.Open(document.ToByteArray());
        var type0 = reopened.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!;
        var cidFont = Assert.IsType<PdfDictionary>(type0.Get<PdfArray>(PdfNames.DescendantFonts)!.GetResolved(0));
        var descriptor = cidFont.Get<PdfDictionary>(PdfNames.FontDescriptor)!;

        Assert.Equal(PdfNames.Type0, type0.GetName(PdfNames.Subtype));
        Assert.Equal(PdfNames.IdentityH, type0.GetName(PdfNames.Encoding));
        Assert.Matches("^[A-Z]{6}\\+" + font.PostScriptName + "$", type0.GetName(PdfNames.BaseFont)!.Value);
        Assert.Equal(type0.GetName(PdfNames.BaseFont), descriptor.GetName(PdfNames.FontName));
        Assert.Equal(cidFontType, cidFont.GetName(PdfNames.Subtype)!.Value);
        Assert.NotNull(type0.Get<PdfStream>(PdfNames.ToUnicode));
        Assert.NotEmpty(descriptor.Get<PdfStream>(PdfName.Create(fontFileKey))!.GetDecodedData());
    }

    [Fact]
    public void EmbeddedFontIsShaped()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("fi AV", font, 10, 0, 0);

        var content = System.Text.Encoding.Latin1.GetString(PdfDocument.Open(document.ToByteArray()).Pages[0].GetContentData());

        // The ligature is one glyph, and the kerning of AV is a number of the TJ array: four codes for the five characters
        Assert.Matches("^BT\n/F1 10 Tf\n0 0 Td\n\\[<[0-9A-F]{12}>-?[0-9.]+<[0-9A-F]{4}>\\] TJ\nET\n$", content);
        Assert.True(font.GetTextWidth("AV", 10) < font.GetTextWidth("A", 10) + font.GetTextWidth("V", 10));
    }

    [Fact]
    public void EmbeddedFontWithoutTheGlyph()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A4).CreateContentBuilder();

        Assert.True(font.CanEncode("Caf\u00E9\u200D"));
        Assert.False(font.CanEncode("\u4E2D"));
        Assert.Throws<ArgumentException>(() => builder.DrawText("A\u4E2D", font, 12, 0, 0));
        Assert.Empty(document.Pages[0].GetContentData());
    }

    [Fact]
    public void EmbeddedFontSubsetGrowsWithTheText()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder().DrawText("A", font, 12, 50, 800);
        var first = document.ToByteArray();
        page.CreateContentBuilder().DrawText("B", font, 12, 50, 700);

        var second = PdfDocument.Open(document.ToByteArray());

        Assert.Equal("A", PdfDocument.Open(first).Pages[0].ExtractText());
        Assert.Equal("A\nB", second.Pages[0].ExtractText());
        Assert.Single(second.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!);
    }

    [Fact]
    public void EmbeddedFontsAreNotSharedBetweenDocuments()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var first = new PdfDocument();
        first.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("One", font, 12, 50, 800);
        var second = new PdfDocument();
        second.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawText("Two", font, 12, 50, 800);

        Assert.Equal("One", PdfDocument.Open(first.ToByteArray()).Pages[0].ExtractText());
        Assert.Equal("Two", PdfDocument.Open(second.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void EmbeddedFontInTextArrays()
    {
        var font = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .BeginText()
            .SetFont(font, 12)
            .SetTextLeading(14)
            .MoveTextPosition(50, 800)
            .ShowText(["Ke", 80, "rning"])
            .ShowTextOnNextLine("Next")
            .EndText();

        Assert.Equal("Kerning\nNext", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void ReplacementTextOfMarkedContent()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> /Properties << /P0 << /ActualText (named) >> >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td /Span << /ActualText (Replaced) >> BDC (xyz) Tj EMC ( and ) Tj /Span /P0 BDC (abc) Tj EMC ET"));

        Assert.Equal("Replaced and named", PdfDocument.Open(file.ToArray()).Pages[0].ExtractText());
    }

    [Theory]
    [InlineData("noto-sans/NotoSans-Regular.subset.ttf")]
    [InlineData("noto-sans-cff/NotoSans-Regular.subset.otf")]
    [InlineData("noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf")]
    public void SubsetKeepsTheOutlinesOfTheGlyphs(string fontPath)
    {
        var face = LoadFace(fontPath);
        int[] kept = [.. new[] { face.MapCodePoint('A'), face.MapCodePoint(0xE9), face.MapCodePoint(0x672C) }.Where(glyph => glyph != 0)];
        var removed = face.MapCodePoint('Z');

        // The subset has the tables a PDF reader uses: the other tables of the font are added to read it as a font
        var tables = new SortedDictionary<uint, byte[]>();
        foreach (var tag in face.TableTags)
        {
            Assert.True(face.TryGetTable(tag, out var table));
            tables[tag] = table.ToArray();
        }

        if (face.Outlines == FontOutlineKind.TrueType)
        {
            var program = FontSubsetter.SubsetTrueType(face, kept);
            var count = BinaryPrimitives.ReadUInt16BigEndian(program.AsSpan(4));
            for (var i = 0; i < count; i++)
            {
                var record = program.AsSpan(12 + (i * 16));
                tables[BinaryPrimitives.ReadUInt32BigEndian(record)] = program.AsSpan((int)BinaryPrimitives.ReadUInt32BigEndian(record[8..]), (int)BinaryPrimitives.ReadUInt32BigEndian(record[12..])).ToArray();
            }
        }
        else
        {
            tables[FontTags.Cff] = FontSubsetter.SubsetCff(face, kept, "ABCDEF+Subset");
        }

        var subset = FontSubsetter.WriteSfnt(face.Outlines == FontOutlineKind.TrueType ? 0x00010000u : FontTags.OpenTypeCff, tables);

        var subsetFace = Assert.Single(FontLoader.Load(subset, face.Data.Environment));
        using var scratch = new GlyphScratch(face.Data.Environment.CreateAllocator("test"));

        Assert.Equal(face.GlyphCount, subsetFace.GlyphCount);
        Assert.Equal(face.Outlines, subsetFace.Outlines);
        foreach (var glyph in FontSubsetter.GetClosure(face, kept))
        {
            Assert.Equal(face.GetAdvanceWidth(glyph), subsetFace.GetAdvanceWidth(glyph));
            Assert.Equal(face.TryGetGlyphBounds(glyph, scratch, out var expected), subsetFace.TryGetGlyphBounds(glyph, scratch, out var actual));
            Assert.Equal(expected, actual);
        }

        Assert.True(face.TryGetGlyphBounds(removed, scratch, out _));
        Assert.False(subsetFace.TryGetGlyphBounds(removed, scratch, out _));
    }

    [Fact]
    public void SubsetOfGlyphsThatOverlapIsBounded()
    {
        // The offsets go back and forth: every other glyph is the whole glyf table, and a subset of all of them would be
        // the table times the glyphs
        var face = LoadFace("noto-sans/NotoSans-Regular.subset.ttf");
        var tables = new SortedDictionary<uint, byte[]>();
        foreach (var tag in face.TableTags)
        {
            Assert.True(face.TryGetTable(tag, out var table));
            tables[tag] = table.ToArray();
        }

        var loca = new byte[(face.GlyphCount + 1) * 4];
        for (var glyph = 1; glyph <= face.GlyphCount; glyph += 2)
        {
            BinaryPrimitives.WriteUInt32BigEndian(loca.AsSpan(glyph * 4), (uint)tables[FontTags.Glyf].Length);
        }

        tables[FontTags.Loca] = loca;
        BinaryPrimitives.WriteInt16BigEndian(tables[FontTags.Head].AsSpan(50), 1);
        var overlapping = Assert.Single(FontLoader.Load(FontSubsetter.WriteSfnt(0x00010000, tables), face.Data.Environment));

        Assert.Throws<InvalidFontException>(() => FontSubsetter.SubsetTrueType(overlapping, Enumerable.Range(0, face.GlyphCount)));
    }

    [Fact]
    public void ClosureHasTheComponentsOfCompositeGlyphs()
    {
        var face = LoadFace("noto-sans/NotoSans-Regular.subset.ttf");
        var eacute = face.MapCodePoint(0xE9);

        var closure = FontSubsetter.GetClosure(face, [eacute, -1, face.GlyphCount]);

        Assert.Contains(0, closure);
        Assert.Contains(eacute, closure);
        Assert.DoesNotContain(-1, closure);
        Assert.DoesNotContain(face.GlyphCount, closure);
    }

    [Fact]
    public void ShowGlyphsAtTheirPositions()
    {
        var font = (EmbeddedPdfFont)PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var a = font.Face.MapCodePoint('A');
        var b = font.Face.MapCodePoint('B');
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .BeginText()
            .SetFont(font, 10)
            .ShowGlyphs([new PdfGlyph(a, 0, 0), new PdfGlyph(b, 20, 0), new PdfGlyph(a, 30, 5)], "ABA")
            .EndText();

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];
        var content = System.Text.Encoding.Latin1.GetString(page.GetContentData());
        var widthA = font.GetWidth(a);
        var widthB = font.GetWidth(b);

        // The second glyph is 20 units from the origin, 2000 thousandths of the font size; the third one is raised by 5
        var expected = FormattableString.Invariant($"BT\n/F1 10 Tf\n[<{a:X4}>{widthA - 2000}<{b:X4}>] TJ\n5 Ts\n[{widthB + 2000 - 3000}<{a:X4}>] TJ\n0 Ts\nET\n");
        Assert.Equal(expected, content);

        // The gaps between the glyphs are wider than a space
        Assert.Equal("A B A", page.ExtractText());
    }

    [Fact]
    public void ShowGlyphsOfALigature()
    {
        var font = (EmbeddedPdfFont)PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var shaped = font.Shape("office");
        var glyphs = new List<PdfGlyph>();
        var x = 0d;
        foreach (var glyph in shaped.Glyphs)
        {
            x -= glyph.Adjustment / 100;
            glyphs.Add(new PdfGlyph(glyph.Glyph, x, 0));
            x += font.GetWidth(glyph.Glyph) / 100;
        }

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginText().SetFont(font, 10).ShowGlyphs([.. glyphs], "office").EndText();

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];

        // The ffi ligature maps to its three characters in the ToUnicode CMap: no replacement text
        Assert.HasCountLessThan(6, glyphs);
        Assert.DoesNotContain("ActualText", System.Text.Encoding.Latin1.GetString(page.GetContentData()), StringComparison.Ordinal);
        Assert.Equal("office", page.ExtractText());
    }

    [Fact]
    public void ShowGlyphsOfARightToLeftRun()
    {
        const string Text = "مرحبا";
        var font = (EmbeddedPdfFont)PdfFont.Load(RepositoryPaths.Fonts / "noto-naskh-arabic/NotoNaskhArabic-Regular.subset.ttf");
        var shaped = font.Shape(Text);
        var glyphs = new List<PdfGlyph>();
        var x = 0d;
        foreach (var glyph in shaped.Glyphs)
        {
            x -= glyph.Adjustment / 100;
            glyphs.Add(new PdfGlyph(glyph.Glyph, x, glyph.Rise / 100));
            x += font.GetWidth(glyph.Glyph) / 100;
        }

        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginText().SetFont(font, 10).ShowGlyphs([.. glyphs], Text).EndText();

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];

        Assert.Contains("ActualText", System.Text.Encoding.Latin1.GetString(page.GetContentData()), StringComparison.Ordinal);
        Assert.Equal(Text, page.ExtractText());
    }

    [Fact]
    public void ShowGlyphsWithoutText()
    {
        var font = (EmbeddedPdfFont)PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        document.Pages.Add(PdfSize.A4).CreateContentBuilder().BeginText().SetFont(font, 10).ShowGlyphs([new PdfGlyph(font.Face.MapCodePoint('A'), 0, 0)]).EndText();

        var page = PdfDocument.Open(document.ToByteArray()).Pages[0];

        Assert.Single(page.Resources!.Get<PdfDictionary>(PdfNames.Font)!);
        Assert.Equal("", page.ExtractText().Trim('�'));
    }

    [Fact]
    public void ShowGlyphsChecksTheGlyphsAndTheFont()
    {
        var font = (EmbeddedPdfFont)PdfFont.Load(RepositoryPaths.Fonts / "noto-sans/NotoSans-Regular.subset.ttf");
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var builder = page.CreateContentBuilder();

        Assert.Throws<InvalidOperationException>(() => builder.ShowGlyphs([new PdfGlyph(1, 0, 0)]));
        builder.BeginText().SetFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10);
        Assert.Throws<InvalidOperationException>(() => builder.ShowGlyphs([new PdfGlyph(1, 0, 0)]));
        builder.SetFont(font, 0);
        Assert.Throws<InvalidOperationException>(() => builder.ShowGlyphs([new PdfGlyph(1, 0, 0)]));
        builder.SetFont(font, 10);
        var length = page.GetContentData().Length;
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.ShowGlyphs([new PdfGlyph(1, 0, 0), new PdfGlyph(font.Face.GlyphCount, 0, 0)]));
        Assert.Throws<ArgumentException>(() => builder.ShowGlyphs([new PdfGlyph(1, double.NaN, 0)]));
        Assert.HasCount(length, page.GetContentData());
    }

    private static FontFace LoadFace(string path) => FontLoader.Load(File.ReadAllBytes(RepositoryPaths.Fonts / path), new PooledFontEnvironment(FontResourceLimits.Default, 64 * 1024 * 1024))[0];

    [Fact]
    public void Example_TextMethods()
    {
        using var directory = TemporaryDirectory.Create();
        DrawReceipt(directory / "receipt.pdf");
        DrawTextStyles(directory / "styles.pdf");
        DrawParagraph(directory / "paragraph.pdf");
        DrawEmbeddedText(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf", directory / "embedded.pdf");

        Assert.Equal("Corner Café\nCoffee 3.50 €\nCroissant 2.20 €\nOrange juice 4.10 €\nTotal 9.80 €", PdfDocument.Open(directory / "receipt.pdf").Pages[0].ExtractText());
        Assert.All(PdfDocument.Open(directory / "paragraph.pdf").Pages[0].ExtractText().Split('\n'), line => Assert.True(PdfFont.GetStandardFont(PdfStandardFont.TimesRoman).GetTextWidth(line, 12) <= 260));
        Assert.StartsWith("A text object shows several lines\nCharacter spacing", PdfDocument.Open(directory / "styles.pdf").Pages[0].ExtractText(), StringComparison.Ordinal);
        Assert.Equal("Efficient office\nWAVE AVATAR\nPrice: 12 € — tax included", PdfDocument.Open(directory / "embedded.pdf").Pages[0].ExtractText());
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawReceipt
    public static void DrawReceipt(string outputPath)
    {
        var regular = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var bold = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        (string Name, decimal Price)[] items = [("Coffee", 3.50m), ("Croissant", 2.20m), ("Orange juice", 4.10m)];

        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(220, 170)).CreateContentBuilder();

        // Centered: the text starts half its width before the center of the page
        var title = "Corner Café";
        builder.DrawText(title, bold, 16, (220 - bold.GetTextWidth(title, 16)) / 2, 140);

        // One line per item: the name at the left margin, the price aligned on the right margin
        var y = 110.0;
        foreach (var (name, price) in items)
        {
            var amount = price.ToString("0.00 €", CultureInfo.InvariantCulture);
            builder.DrawText(name, regular, 11, 20, y)
                .DrawText(amount, regular, 11, 200 - regular.GetTextWidth(amount, 11), y);
            y -= 18;
        }

        builder.SetLineWidth(0.5).MoveTo(20, y + 10).LineTo(200, y + 10).Stroke();
        var total = items.Sum(item => item.Price).ToString("0.00 €", CultureInfo.InvariantCulture);
        builder.DrawText("Total", bold, 12, 20, y - 8)
            .DrawText(total, bold, 12, 200 - bold.GetTextWidth(total, 12), y - 8);

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawTextStyles
    public static void DrawTextStyles(string outputPath)
    {
        var font = PdfFont.GetStandardFont(PdfStandardFont.TimesRoman);
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(300, 170)).CreateContentBuilder()
            .BeginText()
            .SetFont(font, 14)
            .SetTextLeading(22) // the distance between two baselines
            .MoveTextPosition(20, 140) // the start of the first line
            .ShowText("A text object shows several lines")
            .SetCharacterSpacing(2).ShowTextOnNextLine("Character spacing").SetCharacterSpacing(0)
            .SetWordSpacing(10).ShowTextOnNextLine("Word spacing between words").SetWordSpacing(0)
            .SetHorizontalScaling(70).ShowTextOnNextLine("Horizontal scaling of 70%").SetHorizontalScaling(100)
            .MoveToNextLine().ShowText("E = mc").SetTextRise(6).SetFont(font, 9).ShowText("2").SetTextRise(0).SetFont(font, 14)
            .SetFont(font, 24).SetTextLeading(32).SetLineWidth(0.5).SetTextRenderingMode(PdfTextRenderingMode.Stroke).ShowTextOnNextLine("Outlined text")
            .EndText();

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawParagraph
    public static void DrawParagraph(string outputPath)
    {
        var font = PdfFont.GetStandardFont(PdfStandardFont.TimesRoman);
        var text = "The content builder shows each text where it is told to. To fill a column, measure the words, and start a new line before the word that does not fit.";
        var fontSize = 12.0;
        var maxWidth = 260.0;

        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 80)).CreateContentBuilder()
            .BeginText()
            .SetFont(font, fontSize)
            .SetTextLeading(16)
            .MoveTextPosition(20, 55);

        var line = "";
        foreach (var word in text.Split(' '))
        {
            var candidate = line.Length == 0 ? word : line + " " + word;
            if (line.Length > 0 && font.GetTextWidth(candidate, fontSize) > maxWidth)
            {
                builder.ShowText(line).MoveToNextLine();
                line = word;
            }
            else
            {
                line = candidate;
            }
        }

        builder.ShowText(line).EndText();
        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawEmbeddedText
    public static void DrawEmbeddedText(string fontPath, string outputPath)
    {
        // A TrueType or OpenType font file: the document embeds the glyphs it shows
        var font = PdfFont.Load(fontPath);

        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(300, 130)).CreateContentBuilder()
            .DrawText("Efficient office", font, 26, 15, 90) // the "ffi" and "ff" ligatures of the font
            .DrawText("WAVE AVATAR", font, 26, 15, 52) // kerning: the pairs "WA", "AV" and "TA" are tightened
            .SetFillRgb(0.4, 0.4, 0.4)
            .DrawText("Price: 12 € — tax included", font, 13, 15, 20);

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_ChooseFont()
    {
        var fontPath = RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf";

        Assert.Equal("Helvetica", ChooseFont("Café", fontPath).PostScriptName);
        Assert.NotEqual("Helvetica", ChooseFont("Łódź", fontPath).PostScriptName);
    }

    private static PdfFont ChooseFont(string text, string fontPath)
    {
        #region Pdf_ChooseFont
        // A standard font shows the characters of WinAnsiEncoding (Latin-1, the euro sign, the typographic quotes...)
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var font = helvetica.CanEncode(text) ? helvetica : PdfFont.Load(fontPath);
        #endregion

        return font;
    }
}
