using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Fonts;
using Meziantou.Framework.Pdf.Text;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfTextExtractionTests
{
    [Theory]
    [InlineData("BT /F1 12 Tf 72 720 Td (Hello) Tj ET", "Hello")]
    [InlineData("BT /F1 12 Tf 72 720 Td [(Hel) 0 (lo)] TJ ET", "Hello")]
    [InlineData("BT /F1 12 Tf 72 720 Td [(Hello) -2000 (World)] TJ ET", "Hello World")]
    [InlineData("BT /F1 12 Tf 72 720 Td (Hello) Tj 0 -14 Td (World) Tj ET", "Hello\nWorld")]
    [InlineData("BT /F1 12 Tf 14 TL 72 720 Td (Hello) Tj T* (World) Tj ET", "Hello\nWorld")]
    [InlineData("BT /F1 12 Tf 14 TL 72 720 Td (Hello) Tj (World) ' ET", "Hello\nWorld")]
    [InlineData("BT /F1 12 Tf 72 720 Td (Hello) Tj ET BT /F1 12 Tf 200 720 Td (World) Tj ET", "Hello World")]
    [InlineData("BT /F1 12 Tf 72 720 Td (A) Tj ET BT /F1 12 Tf 72 100 Td (B) Tj ET", "A\nB")]
    [InlineData("2 0 0 2 0 0 cm BT /F1 6 Tf 36 360 Td (Hello) Tj ( World) Tj ET", "Hello World")]
    [InlineData("BT /F1 12 Tf 0 1 -1 0 300 300 Tm (Up) Tj ET", "Up")]
    [InlineData("BT /F1 12 Tf 72 720 Td (\\(\\)\\\\) Tj ET", "()\\")]
    [InlineData("BT /F2 12 Tf 72 720 Td (abg) Tj ET", "\u03B1\u03B2\u03B3")]
    [InlineData("BT /F3 12 Tf 72 720 Td (\\001\\002) Tj ET", "\uFB01\u00E9")]
    [InlineData("BT /F4 12 Tf 72 720 Td <00410042> Tj ET", "AB")]
    [InlineData("BT /F4 12 Tf 72 720 Td <0001> Tj ET", "\u4E2D\u6587")]
    [InlineData("q BT /F1 12 Tf 72 720 Td (In) Tj ET Q BT /F1 12 Tf 72 700 Td (Out) Tj ET", "In\nOut")]
    [InlineData("BT /F1 12 Tf 72 720 Td (No font) Tj /Missing 12 Tf (x) Tj ET", "No font")]
    [InlineData("/Form Do", "In a form")]
    [InlineData("BI /W 1 /H 1 /CS /G /BPC 8 ID \u00FF EI BT /F1 12 Tf 72 720 Td (After image) Tj ET", "After image")]
    public void Extract(string content, string expected)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R /F2 6 0 R /F3 7 0 R /F4 8 0 R >> /XObject << /Form 11 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream(content))
            .Add(6, "<< /Type /Font /Subtype /Type1 /BaseFont /Symbol >>")
            .Add(7, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding << /Differences [1 /fi /eacute] >> >>")
            .Add(8, "<< /Type /Font /Subtype /Type0 /BaseFont /Custom /Encoding /Identity-H /DescendantFonts [9 0 R] /ToUnicode 10 0 R >>")
            .Add(9, "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Custom /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 1000 >>")
            .Add(10, HandWrittenPdf.Stream("/CIDInit /ProcSet findresource begin 12 dict begin begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 2 beginbfchar <0041> <0041> <0001> <4E2D6587> endbfchar 1 beginbfrange <0042> <0043> <0042> endbfrange endcmap end end"))
            .Add(11, HandWrittenPdf.Stream("BT /F1 10 Tf 0 0 Td (In a form) Tj ET", " /Type /XObject /Subtype /Form /BBox [0 0 100 100]"));

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal(expected, document.Pages[0].ExtractText());
    }

    [Fact]
    public void RecursiveFormsStop()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources 4 0 R /Contents 5 0 R >>")
            .Add(4, "<< /XObject << /X 5 0 R >> >>")
            .Add(5, HandWrittenPdf.Stream("/X Do", " /Type /XObject /Subtype /Form /Resources 4 0 R"));

        Assert.Equal("", PdfDocument.Open(file.ToArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void FormsThatPaintFormsSeveralTimesAreBounded()
    {
        // Each form paints the next one twice: the work doubles at each of the 16 levels
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources 4 0 R /Contents 10 0 R >>")
            .Add(4, "<< /Font << /F1 5 0 R >> /XObject << " + string.Concat(Enumerable.Range(0, 20).Select(i => $"/X{i} {10 + i} 0 R ")) + ">> >>")
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for (var i = 0; i < 20; i++)
        {
            var content = (i < 19 ? $"/X{i + 1} Do /X{i + 1} Do " : "") + "BT /F1 1 Tf (" + new string('x', 5000) + ") Tj ET";
            file.Add(10 + i, HandWrittenPdf.Stream(content, " /Type /XObject /Subtype /Form /Resources 4 0 R"));
        }

        var document = PdfDocument.Open(file.ToArray());

        Assert.Throws<PdfResourceLimitException>(() => document.Pages[0].ExtractText());
    }

    [Fact]
    public void GlyphsKeptInMemoryAreBounded()
    {
        // Few operators but many glyphs: the glyphs have a limit of their own
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources 4 0 R /Contents 10 0 R >>")
            .Add(4, "<< /Font << /F1 5 0 R >> /XObject << " + string.Concat(Enumerable.Range(0, 20).Select(i => $"/X{i} {10 + i} 0 R ")) + ">> >>")
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>");
        for (var i = 0; i < 20; i++)
        {
            var content = (i < 19 ? $"/X{i + 1} Do /X{i + 1} Do " : "") + "BT /F1 1 Tf (" + new string('x', 100) + ") Tj ET";
            file.Add(10 + i, HandWrittenPdf.Stream(content, " /Type /XObject /Subtype /Form /Resources 4 0 R"));
        }

        var document = PdfDocument.Open(file.ToArray());

        var exception = Assert.Throws<PdfResourceLimitException>(() => document.Pages[0].ExtractText());
        Assert.Contains("shows more than", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReplacementTextsPaintedManyTimesAreBounded()
    {
        // Each form paints the next one twice, and each has a long replacement text in its resources
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources 4 0 R /Contents 10 0 R >>")
            .Add(4, "<< /Properties << /P0 << /ActualText (" + new string('x', 100_000) + ") >> >> /XObject << " + string.Concat(Enumerable.Range(0, 20).Select(i => $"/X{i} {10 + i} 0 R ")) + ">> >>");
        for (var i = 0; i < 20; i++)
        {
            var content = (i < 19 ? $"/X{i + 1} Do /X{i + 1} Do " : "") + "/Span /P0 BDC EMC";
            file.Add(10 + i, HandWrittenPdf.Stream(content, " /Type /XObject /Subtype /Form /Resources 4 0 R"));
        }

        var document = PdfDocument.Open(file.ToArray());

        var exception = Assert.Throws<PdfResourceLimitException>(() => document.Pages[0].ExtractText());
        Assert.Contains("characters", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void CMapWithManyRangesIsBounded()
    {
        var ranges = string.Concat(Enumerable.Range(0, 100_000).Select(i => $"<{i:X4}> <{i:X4}> "));
        var toUnicode = "1 begincodespacerange " + ranges + "endcodespacerange 1 beginbfrange " + string.Concat(Enumerable.Range(0, 100_000).Select(i => $"<{i:X4}> <{i:X4}> <0041> ")) + "endbfrange";
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type0 /BaseFont /X /Encoding /Identity-H /DescendantFonts [<< /Type /Font /Subtype /CIDFontType2 /BaseFont /X /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> >>] /ToUnicode 6 0 R >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf <0000 0001 FFFF> Tj ET"))
            .Add(6, HandWrittenPdf.Stream(toUnicode));

        var document = PdfDocument.Open(file.ToArray());

        Assert.Equal("AA", document.Pages[0].ExtractText());
    }

    [Fact]
    public void TextWrittenByTheBuilder()
    {
        var document = new PdfDocument();
        var font = PdfFont.GetStandardFont(PdfStandardFont.TimesRoman);
        document.Pages.Add(PdfSize.A4).CreateContentBuilder()
            .BeginText()
            .SetFont(font, 11)
            .MoveTextPosition(50, 800)
            .ShowText("Caf\u00E9 \u2014 \u20AC5")
            .ShowText(["Ke", 80, "rning"])
            .EndText()
            .DrawText("\u03C0 \u2248 3.14", PdfFont.GetStandardFont(PdfStandardFont.Symbol), 11, 50, 780);

        var text = PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText();

        Assert.Equal("Caf\u00E9 \u2014 \u20AC5Kerning\n\u03C0 \u2248 3.14", text);
    }

    [Fact]
    public void CodesOfDifferentLengthsWithTheSameValueAreDistinct()
    {
        // <41> and <0041> are two codes of a mixed-length code space: each has its own text and its own CID
        var encoding = "begincmap 2 begincodespacerange <41> <41> <0000> <00FF> endcodespacerange 2 begincidchar <41> 1 <0041> 2 endcidchar endcmap";
        var toUnicode = "begincmap 2 begincodespacerange <41> <41> <0000> <00FF> endcodespacerange 2 beginbfchar <41> <0058> <0041> <0059> endbfchar endcmap";
        var document = OpenComposite("BT /F1 10 Tf 72 720 Td <41004141> Tj ET", "/Encoding 6 0 R", "/W [1 [500] 2 [1000]]", (6, HandWrittenPdf.Stream(encoding)), (7, HandWrittenPdf.Stream(toUnicode)));

        Assert.Equal("XYX", document.Pages[0].ExtractText());
        var glyphs = document.Pages[0].ExtractTextLayout().Lines.Single().Glyphs;
        Assert.Equal([72, 77, 87], glyphs.Select(glyph => Math.Round(glyph.Origin.X, 6, MidpointRounding.AwayFromZero)));

        var font = document.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!;
        var decoder = (CompositeFontDecoder)PdfFontDecoder.Create(font);
        var (shortCode, shortLength) = decoder.ReadCode([0x41]);
        var (longCode, longLength) = decoder.ReadCode([0x00, 0x41]);
        Assert.Equal((1, 2), (shortLength, longLength));
        Assert.Equal((1, 2), (decoder.GetCid(shortCode), decoder.GetCid(longCode)));
    }

    [Fact]
    public void CMapCodeSpacesMatchEachByte()
    {
        // GB 18030: <8130> is not a two-byte code (its second byte is below 0x40), it starts a four-byte code
        var document = OpenComposite("BT /F1 10 Tf 72 720 Td <4181308130D6D0> Tj ET", "/Encoding /GBK2K-H");

        Assert.Equal("A\u0080\u4E2D", document.Pages[0].ExtractText());
    }

    [Fact]
    public void WideRangeBeforeManyOverlappingRanges()
    {
        // The wide range contains the code; the ten ranges after it start before the code and do not contain it
        var ranges = "<0000> <00FF> <0041> " + string.Concat(Enumerable.Range(0, 10).Select(i => $"<{0x10 + i:X4}> <{0x10 + i:X4}> <0061> "));
        var document = OpenComposite("BT /F1 10 Tf 72 720 Td <00F0 0012 0030> Tj ET", toUnicode: "begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 11 beginbfrange " + ranges + "endbfrange endcmap");

        Assert.Equal("\u0131aq", document.Pages[0].ExtractText());
    }

    [Fact]
    public void LaterMappingsReplaceEarlierOnes()
    {
        var toUnicode = "begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfchar <0020> <0051> endbfchar 2 beginbfrange <0000> <00FF> <0041> <0010> <0010> <007A> endbfrange 1 beginbfchar <0030> <0052> endbfchar endcmap";
        var document = OpenComposite("BT /F1 10 Tf 72 720 Td <0010 0020 0030 0011> Tj ET", toUnicode: toUnicode);

        Assert.Equal("zaRR", document.Pages[0].ExtractText());
    }

    [Theory]
    [InlineData("<0001> <0003> <D835DC00>", "<000100020003>", "\U0001D400\U0001D401\U0001D402")]
    [InlineData("<0010> <0011> <D83DDFFF>", "<00100011>", "\U0001F7FF\U0001F800")]
    [InlineData("<0020> <0022> <0041D7FE>", "<002000210022>", "A\uD7FEA\uD7FF")]
    [InlineData("<0030> <0031> <DBFFDFFF>", "<00300031>", "\U0010FFFF")]
    public void UnicodeRangesWithSupplementaryCharacters(string range, string codes, string expected)
    {
        var document = OpenComposite($"BT /F1 10 Tf 72 720 Td {codes} Tj ET", toUnicode: "begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange " + range + " endbfrange endcmap");

        Assert.Equal(expected, document.Pages[0].ExtractText());
    }

    [Theory]
    [InlineData("A", 1u, "B")]
    [InlineData("\uD7FF", 1u, null)]
    [InlineData("\uDBFF\uDFFF", 1u, null)]
    [InlineData("\uFFFE", 1u, "\uFFFF")]
    [InlineData("\uFFFF", 1u, null)]
    [InlineData("", 1u, null)]
    public void UnicodeRangeIncrement(string start, uint offset, string? expected) => Assert.Equal(expected, CMap.GetRangeText(start, offset));

    [Theory]
    [InlineData("/D [1 0 ID \u00FF EI ")]
    [InlineData("/D [[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[[ ID \u00FF EI ")]
    [InlineData("/D << /A ID \u00FF EI ")]
    [InlineData("/W ID \u00FF EI ")]
    public void MalformedInlineImageDictionaryKeepsTheText(string image)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (Before) Tj ET BI " + image + "BT /F1 12 Tf 72 700 Td (After) Tj ET"));

        Assert.Equal("Before\nAfter", PdfDocument.Open(file.ToArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void VerticalMetricsOfTheCIDFont()
    {
        // DW2: displacement -500; W2: CID 3 moves by -2000. Tc is added to the vertical displacement (9.4.4)
        var document = OpenComposite("BT /F1 10 Tf 100 700 Td <0001000200030001> Tj 1 Tc <0001 0001> Tj ET", "/Encoding /Identity-V", "/DW2 [880 -500] /W2 [3 [-2000 250 880]]", toUnicode: "begincmap 1 begincodespacerange <0000> <FFFF> endcodespacerange 1 beginbfrange <0001> <0003> <0041> endbfrange endcmap");

        var layout = document.Pages[0].ExtractTextLayout();

        Assert.Equal("ABCAAA", layout.Text);
        var glyphs = layout.Lines.Single().Glyphs;
        Assert.Equal([700, 695, 690, 670, 665, 661], glyphs.Select(glyph => Math.Round(glyph.Origin.Y, 6, MidpointRounding.AwayFromZero)));
        Assert.All(glyphs, glyph => Assert.Equal(100, glyph.Origin.X, 6));
        Assert.All(glyphs, glyph => Assert.True(glyph.IsVertical));
        Assert.All(glyphs, glyph => Assert.Equal(-90, glyph.Angle, 6));

        // The box of the glyph: as wide as the glyph (DW 1000), centered on the vertical origin by the position vector,
        // except CID 3 whose position vector moves it 2.5 points to the left of its 10 points
        Assert.Equal(new PdfRectangle(95, 695, 105, 700), Round(glyphs[0].Bounds));
        Assert.Equal(new PdfRectangle(97.5, 670, 107.5, 690), Round(glyphs[2].Bounds));
    }

    [Theory]
    [InlineData("/90ms-RKSJ-H", "<93FA967B8CEA41B1>", "\u65E5\u672C\u8A9EA\uFF71")]
    [InlineData("/90ms-RKSJ-V", "<93FA967B>", "\u65E5\u672C")]
    [InlineData("/EUC-H", "<C6FCCBDC>", "\u65E5\u672C")]
    [InlineData("/H", "<467C4B5C>", "\u65E5\u672C")]
    [InlineData("/UniJIS-UCS2-H", "<65E5672C>", "\u65E5\u672C")]
    [InlineData("/UniJIS-UCS2-V", "<65E5672C>", "\u65E5\u672C")]
    [InlineData("/UniJIS-UTF16-H", "<65E5D840DC0B>", "\u65E5\U0002000B")]
    [InlineData("/UniJIS-UTF32-H", "<000065E50002000B>", "\u65E5\U0002000B")]
    [InlineData("/UniJIS-UTF8-H", "<E697A541>", "\u65E5A")]
    [InlineData("/GBK-EUC-H", "<D6D0CEC4>", "\u4E2D\u6587")]
    [InlineData("/GB-EUC-V", "<D6D0CEC4>", "\u4E2D\u6587")]
    [InlineData("/UniGB-UCS2-H", "<4E2D6587>", "\u4E2D\u6587")]
    [InlineData("/ETen-B5-H", "<A4A4A4E5>", "\u4E2D\u6587")]
    [InlineData("/UniCNS-UTF16-V", "<4E2D6587>", "\u4E2D\u6587")]
    [InlineData("/KSCms-UHC-H", "<C7D1B1DB>", "\uD55C\uAE00")]
    [InlineData("/UniKS-UCS2-H", "<D55CAE00>", "\uD55C\uAE00")]
    public void PredefinedCMapsWithoutToUnicode(string encoding, string codes, string expected)
    {
        var document = OpenComposite($"BT /F1 10 Tf 100 700 Td {codes} Tj ET", "/Encoding " + encoding);

        var layout = document.Pages[0].ExtractTextLayout();

        Assert.Equal(expected, layout.Text);
        Assert.All(layout.Lines.SelectMany(line => line.Glyphs), glyph => Assert.Equal(encoding.EndsWith('V', StringComparison.Ordinal), glyph.IsVertical));
    }

    [Fact]
    public void PredefinedCMapsSplitMixedLengthCodes()
    {
        var cmap = CMap.TryGetPredefined("90ms-RKSJ-H")!;

        Assert.Equal((0x41u, 1), cmap.ReadCode([0x41, 0x93, 0xFA]));
        Assert.Equal((0x93FAu, 2), cmap.ReadCode([0x93, 0xFA, 0x41]));
        Assert.Equal((0xB1u, 1), cmap.ReadCode([0xB1, 0x93]));
        Assert.Null(cmap.GetCid(0x93FA, 2));
        Assert.False(cmap.IsVertical);
        Assert.True(CMap.TryGetPredefined("90ms-RKSJ-V")!.IsVertical);
        Assert.True(CMap.TryGetPredefined("V")!.IsVertical);
        Assert.Null(CMap.TryGetPredefined("Unknown-H"));
    }

    [Fact]
    public void LigaturesAreExpandedOnRequest()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /Encoding << /Differences [1 /fi /ffl /f_f] >> >>")
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Times-Roman /ToUnicode 7 0 R >>")
            .Add(6, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (o\\001ce wa\\002e \\003) Tj /F2 12 Tf (\\001) Tj ET"))
            .Add(7, HandWrittenPdf.Stream("begincmap 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <01> <FB03> endbfchar endcmap"));
        var page = PdfDocument.Open(file.ToArray()).Pages[0];

        Assert.Equal("o\uFB01ce wa\uFB04e ff\uFB03", page.ExtractText());
        Assert.Equal("o\uFB01ce wa\uFB04e ff\uFB03", page.ExtractText(PdfTextExtractionOptions.Default));
        var options = new PdfTextExtractionOptions { ExpandLigatures = true };
        Assert.Equal("ofice waffle ffffi", page.ExtractText(options));
        Assert.Equal("ofice waffle ffffi", page.ExtractTextLayout(options).Text);
    }

    [Fact]
    public void LayoutOfHorizontalText()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R /F2 5 0 R >> >> /Contents 6 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, "<< /Type /Font /Subtype /Type1 /BaseFont /Custom /FirstChar 65 /Widths [500] /FontDescriptor << /Type /FontDescriptor /FontName /Custom /Flags 32 /Ascent 700 /Descent -300 >> >>")
            .Add(6, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (Hi you) Tj ET BT 2 0 0 2 0 0 Tm /F2 10 Tf 50 300 Td (AA) Tj ET"));

        var layout = PdfDocument.Open(file.ToArray()).Pages[0].ExtractTextLayout();

        Assert.Equal("Hi you\nAA", layout.Text);
        Assert.Equal(["Hi you", "AA"], layout.Lines.Select(line => line.Text));
        var first = layout.Lines[0];
        Assert.Equal(["Hi", "you"], first.Words.Select(word => word.Text));
        Assert.Equal(6, first.Glyphs.Count);

        // H is 722 units wide; Helvetica has no font descriptor: an ascent of 0.8 and a descent of -0.2
        var h = first.Glyphs[0];
        Assert.Equal("H", h.Text);
        Assert.Equal("Helvetica", h.FontName);
        Assert.Equal(12, h.FontSize, 6);
        Assert.Equal(0, h.Angle, 6);
        Assert.Equal(new PdfPoint(72, 720), h.Origin);
        Assert.Equal(new PdfRectangle(72, 717.6, 80.664, 729.6), Round(h.Bounds));
        Assert.Equal(80.664, Math.Round(h.End.X, 6, MidpointRounding.AwayFromZero));
        Assert.Equal(Round(first.Words[0].Bounds), new PdfRectangle(72, 717.6, Math.Round(first.Glyphs[1].Bounds.Right, 6, MidpointRounding.AwayFromZero), 729.6));

        // The text matrix scales by 2: the font size is 20 in user space, the descriptor gives the ascent and descent
        var a = layout.Lines[1].Glyphs[0];
        Assert.Equal(20, a.FontSize, 6);
        Assert.Equal(new PdfRectangle(100, 594, 110, 614), Round(a.Bounds));
        Assert.Equal(new PdfRectangle(100, 594, 120, 614), Round(layout.Lines[1].Bounds));
    }

    [Fact]
    public void LayoutOfRotatedText()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 0 1 -1 0 300 300 Tm (Up) Tj ET q 0 -1 1 0 0 0 cm BT /F1 10 Tf -100 50 Td (D) Tj ET Q"));

        var layout = PdfDocument.Open(file.ToArray()).Pages[0].ExtractTextLayout();

        Assert.Equal("Up\nD", layout.Text);
        var u = layout.Lines[0].Glyphs[0];
        Assert.Equal(90, u.Angle, 6);
        Assert.Equal(new PdfPoint(300, 300), Round(u.Origin));
        Assert.Equal(new PdfRectangle(290.4, 300, 302.4, 308.664), Round(u.Bounds));

        // The transformation matrix turns clockwise: the text goes down
        var d = layout.Lines[1].Glyphs[0];
        Assert.Equal(-90, d.Angle, 6);
        Assert.Equal(new PdfPoint(50, 100), Round(d.Origin));
        Assert.Equal(new PdfRectangle(48, 92.78, 58, 100), Round(d.Bounds));
    }

    [Fact]
    public void LayoutOfReplacementText()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Courier >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 10 Tf 100 700 Td /Span << /ActualText (xyz) >> BDC (ab) Tj EMC ET"));

        var layout = PdfDocument.Open(file.ToArray()).Pages[0].ExtractTextLayout();

        var glyph = layout.Lines.Single().Glyphs.Single();
        Assert.Equal("xyz", glyph.Text);
        Assert.Equal(new PdfRectangle(100, 698, 112, 708), Round(glyph.Bounds));
    }

    [Fact]
    public void ContentOrderHasOneBlock()
    {
        var layout = ExtractWithHelvetica("BT /F1 10 Tf 72 700 Td (One) Tj 0 -12 Td (Two) Tj ET");

        Assert.Equal("One\nTwo", Assert.Single(layout.Blocks).Text);
        Assert.Empty(ExtractWithHelvetica("").Blocks);
    }

    [Fact]
    public void LayoutOrderReadsTheColumnsOneAfterTheOther()
    {
        // The content paints the lines of both columns from left to right, between a title and a footer that cross the gutter
        const string Content = """
            BT /F1 16 Tf 72 740 Td (A title that spans the two columns of the page) Tj ET
            BT /F1 10 Tf 72 700 Td (Left one) Tj 248 0 Td (Right one) Tj -248 -12 Td (Left two) Tj 248 0 Td (Right two) Tj -248 -12 Td (Left three) Tj ET
            BT /F1 10 Tf 72 600 Td (The footer of the page goes from the left margin to the right column.) Tj ET
            """;

        Assert.Equal("A title that spans the two columns of the page\nLeft one Right one\nLeft two Right two\nLeft three\nThe footer of the page goes from the left margin to the right column.", ExtractWithHelvetica(Content).Text);

        var options = new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout };
        var layout = ExtractWithHelvetica(Content, options);
        Assert.Equal("A title that spans the two columns of the page\nLeft one\nLeft two\nLeft three\nRight one\nRight two\nThe footer of the page goes from the left margin to the right column.", layout.Text);
        Assert.Equal(["A title that spans the two columns of the page", "Left one\nLeft two\nLeft three", "Right one\nRight two", "The footer of the page goes from the left margin to the right column."], layout.Blocks.Select(block => block.Text));
        Assert.Equal(layout.Lines, layout.Blocks.SelectMany(block => block.Lines));
        Assert.Equal(["Right", "one"], layout.Lines[4].Words.Select(word => word.Text));
        Assert.Equal(320, layout.Blocks[2].Bounds.Left);
        Assert.Equal(686, layout.Blocks[2].Bounds.Bottom, 6);
        Assert.Equal(708, layout.Blocks[2].Bounds.Top, 6);
        Assert.Equal(layout.Text, OpenWithHelvetica(Content).ExtractText(options));
    }

    [Fact]
    public void LayoutOrderJoinsTheColumnsCutByAlignedParagraphGaps()
    {
        // The gap between the paragraphs is at the same height in both columns: it crosses the page from left to right
        const string Content = """
            BT /F1 16 Tf 72 740 Td (A title that spans the two columns of the page) Tj ET
            BT /F1 10 Tf 72 700 Td (L1) Tj 248 0 Td (R1) Tj -248 -12 Td (L2) Tj 248 0 Td (R2) Tj -248 -28 Td (L3) Tj 248 0 Td (R3) Tj -248 -12 Td (L4) Tj 248 0 Td (R4) Tj ET
            """;

        var layout = ExtractWithHelvetica(Content, new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout });

        Assert.Equal(["A title that spans the two columns of the page", "L1\nL2", "L3\nL4", "R1\nR2", "R3\nR4"], layout.Blocks.Select(block => block.Text));
    }

    [Fact]
    public void LayoutOrderKeepsTheWideGapsOfALine()
    {
        // A justified line has a gap wider than a column gap, but the next line crosses it: it is not a gutter
        const string Content = "BT /F1 10 Tf 72 700 Td [(Some words) -3000 (more words)] TJ 0 -12 Td (A second line of text that is long) Tj 0 -12 Td [(Last) -2500 (line)] TJ ET";

        var layout = ExtractWithHelvetica(Content, new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout });

        Assert.Equal("Some words more words\nA second line of text that is long\nLast line", layout.Text);
        Assert.Single(layout.Blocks);
        Assert.Equal(["Some", "words", "more", "words"], layout.Lines[0].Words.Select(word => word.Text));
    }

    [Fact]
    public void LayoutOrderFollowsTheRotationOfThePage()
    {
        // The page turns by 90 degrees: the text painted upward is horizontal on the displayed page, and the user space
        // y axis goes from left to right
        const string Content = """
            BT /F1 10 Tf
            0 1 -1 0 100 72 Tm (Left one) Tj 0 1 -1 0 100 320 Tm (Right one) Tj
            0 1 -1 0 112 72 Tm (Left two) Tj 0 1 -1 0 112 320 Tm (Right two) Tj
            ET
            """;

        var layout = ExtractWithHelvetica(Content, new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout }, rotate: 90);

        Assert.Equal("Left one\nLeft two\nRight one\nRight two", layout.Text);
        Assert.Equal(2, layout.Blocks.Count);
    }

    [Fact]
    public void LayoutOrderKeepsTheContentOrderOfVerticalText()
    {
        const string Content = "BT /F1 12 Tf 0 1 -1 0 300 300 Tm (Up) Tj ET BT /F1 12 Tf 0 1 -1 0 320 300 Tm (Next) Tj ET";

        var layout = ExtractWithHelvetica(Content, new PdfTextExtractionOptions { ReadingOrder = PdfTextReadingOrder.Layout });

        Assert.Equal("Up\nNext", layout.Text);
    }

    // The codes a, b, c and d show the Hebrew letters alef, bet, gimel and dalet
    private const string Alef = "\u05D0";
    private const string Bet = "\u05D1";
    private const string Gimel = "\u05D2";
    private const string Dalet = "\u05D3";

    private static PdfPage OpenWithHebrewLetters(string content)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding /ToUnicode 6 0 R >>")
            .Add(5, HandWrittenPdf.Stream(content))
            .Add(6, HandWrittenPdf.Stream("begincmap 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfrange <61> <64> <05D0> endbfrange endcmap"));
        return PdfDocument.Open(file.ToArray()).Pages[0];
    }

    [Theory]
    [InlineData("[(a) 1112 (b) 1112 (c)] TJ", Alef + Bet + Gimel)] // In the order of the text, each glyph to the left of the previous one
    [InlineData("(cba) Tj", Alef + Bet + Gimel)] // In the order of the display, from left to right
    [InlineData("(ba) Tj -50 0 Td (dc) Tj", Alef + Bet + " " + Gimel + Dalet)] // Words written from right to left, on one line
    [InlineData("(12 ba) Tj", Alef + Bet + " 12")] // Numbers are written from left to right in right-to-left text
    [InlineData("(XY cba) Tj", Alef + Bet + Gimel + " XY")]
    [InlineData("(\\(a\\)) Tj", "(" + Alef + ")")] // The brackets are mirrored
    public void RightToLeftTextIsExtractedInTheOrderOfTheText(string operators, string expected)
    {
        var page = OpenWithHebrewLetters("BT /F1 10 Tf 150 700 Td " + operators + " ET");

        Assert.Equal(expected, page.ExtractText());
        Assert.Equal(expected, page.ExtractTextLayout().Text);
    }

    [Fact]
    public void RightToLeftLinesKeepTheirWordsInTheOrderOfTheText()
    {
        var page = OpenWithHebrewLetters("BT /F1 10 Tf 150 700 Td (ba) Tj -50 0 Td (dc) Tj ET BT /F1 10 Tf 100 680 Td (Next line) Tj ET");

        var layout = page.ExtractTextLayout();

        Assert.Equal(Alef + Bet + " " + Gimel + Dalet + "\nNext line", layout.Text);
        Assert.Equal([Alef + Bet, Gimel + Dalet], layout.Lines[0].Words.Select(word => word.Text));
        Assert.Equal([Alef, Bet, Gimel, Dalet], layout.Lines[0].Glyphs.Where(glyph => glyph.Text != " ").Select(glyph => glyph.Text));
        Assert.Equal(["Next", "line"], layout.Lines[1].Words.Select(word => word.Text));
    }

    private static PdfTextLayout ExtractWithHelvetica(string content, PdfTextExtractionOptions? options = null, int rotate = 0) => OpenWithHelvetica(content, rotate).ExtractTextLayout(options);

    private static PdfPage OpenWithHelvetica(string content, int rotate = 0)
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, $"<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Rotate {rotate} /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /Encoding /WinAnsiEncoding >>")
            .Add(5, HandWrittenPdf.Stream(content.ReplaceLineEndings("\n")));

        return PdfDocument.Open(file.ToArray()).Pages[0];
    }

    [Fact]
    public void DecodersAreSharedByThePagesAndFollowTheChangesOfTheFont()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R 6 0 R] /Count 2 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica /ToUnicode 7 0 R >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720 Td (AB) Tj ET"))
            .Add(6, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(7, HandWrittenPdf.Stream("begincmap 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <0058> endbfchar endcmap"));
        var document = PdfDocument.Open(file.ToArray());
        var font = document.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.Font)!.Get<PdfDictionary>(PdfName.Create("F1"))!;

        Assert.Equal("XB", document.Pages[0].ExtractText());
        var decoder = document.TextDecoders.Get(font);
        Assert.Equal("XB", document.Pages[1].ExtractText());
        Assert.Same(decoder, document.TextDecoders.Get(font));

        // The data of the ToUnicode CMap changes
        font.Get<PdfStream>(PdfNames.ToUnicode)!.SetData("begincmap 1 begincodespacerange <00> <FF> endcodespacerange 1 beginbfchar <41> <0059> endbfchar endcmap"u8);
        Assert.Equal("YB", document.Pages[1].ExtractText());

        // An entry of the font dictionary changes
        font[PdfNames.ToUnicode] = null;
        Assert.Equal("AB", document.Pages[0].ExtractText());
        font[PdfNames.Encoding] = new PdfDictionary { [PdfNames.Differences] = new PdfArray(PdfInteger.Get(65), PdfName.Create("Z")) };
        Assert.Equal("ZB", document.Pages[0].ExtractText());
        font.Get<PdfDictionary>(PdfNames.Encoding)!.Get<PdfArray>(PdfNames.Differences)![1] = PdfName.Create("W");
        Assert.Equal("WB", document.Pages[1].ExtractText());
    }

    [Fact]
    public void ContentSplitInStreamsIsOneContent()
    {
        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /Resources << /Font << /F1 4 0 R >> >> /Contents [5 0 R 6 0 R 7 0 R] >>")
            .Add(4, "<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>")
            .Add(5, HandWrittenPdf.Stream("BT /F1 12 Tf 72 720"))
            .Add(6, HandWrittenPdf.Stream("Td (One) Tj 0 -20 Td", " /Filter /Unknown"))
            .Add(7, HandWrittenPdf.Stream("(Two) Tj ET"));

        Assert.Equal("Two", PdfDocument.Open(file.ToArray()).Pages[0].ExtractText());
    }

    [Fact]
    public void TrueTypeCmapFormat2()
    {
        // The high byte 0x81 starts two-byte codes (subheader 1); the other bytes are characters (subheader 0)
        var data = new List<byte>();
        AddUInt16(data, 2);
        AddUInt16(data, 0);
        AddUInt16(data, 0);
        for (var i = 0; i < 256; i++)
        {
            AddUInt16(data, i == 0x81 ? 8 : 0);
        }

        // Subheader 0: codes 0x41 and 0x42, glyphs 5 and 6; subheader 1: low bytes 0x40 and 0x41, glyphs 1 and 2 plus 10
        AddSubHeader(data, firstCode: 0x41, entryCount: 2, delta: 0, rangeOffset: 2 + 8);
        AddSubHeader(data, firstCode: 0x40, entryCount: 2, delta: 10, rangeOffset: 2 + 4);
        foreach (var glyph in new[] { 5, 6, 1, 2 })
        {
            AddUInt16(data, glyph);
        }

        var subtable = TrueTypeCmapSubtable.Read([.. data], 0)!;

        Assert.Equal(5, subtable.Map(0x41));
        Assert.Equal(6, subtable.Map(0x42));
        Assert.Equal(0, subtable.Map(0x43));
        Assert.Equal(0, subtable.Map(0x81));
        Assert.Equal(11, subtable.Map(0x8140));
        Assert.Equal(12, subtable.Map(0x8141));
        Assert.Equal(0, subtable.Map(0x8142));
        Assert.Equal(0, subtable.Map(0x4140));
        Assert.Equal(0, subtable.Map(0x10000));

        static void AddSubHeader(List<byte> data, int firstCode, int entryCount, int delta, int rangeOffset)
        {
            AddUInt16(data, firstCode);
            AddUInt16(data, entryCount);
            AddUInt16(data, delta);
            AddUInt16(data, rangeOffset);
        }
    }

    [Fact]
    public void TrueTypeCmapFormats12And13()
    {
        var format12 = TrueTypeCmapSubtable.Read(CreateGroups(12, (0x41, 0x43, 10), (0x4E00, 0x9FFF, 100), (0x10000, 0x10FFFF, 30000)), 0)!;
        var format13 = TrueTypeCmapSubtable.Read(CreateGroups(13, (0x41, 0x43, 10), (0x4E00, 0x9FFF, 100)), 0)!;

        Assert.Equal([10, 11, 12, 0], new[] { 0x41, 0x42, 0x43, 0x44 }.Select(format12.Map));
        Assert.Equal(100 + 0x5000 - 0x4E00, format12.Map(0x5000));
        Assert.Equal(30005, format12.Map(0x10005));
        Assert.Equal(0, format12.Map(0x10000 + 65536));
        Assert.Equal([10, 10, 10, 0], new[] { 0x41, 0x42, 0x43, 0x44 }.Select(format13.Map));
        Assert.Equal(100, format13.Map(0x9FFF));

        // Overlapping groups: the first one wins
        var overlapping = TrueTypeCmapSubtable.Read(CreateGroups(12, (0x50, 0x60, 1), (0x41, 0x55, 100)), 0)!;
        Assert.Equal([100, 1, 6], new[] { 0x41, 0x50, 0x55 }.Select(overlapping.Map));

        // A table shorter than its count of groups: the complete groups
        var truncated = CreateGroups(12, (0x41, 0x41, 7), (0x42, 0x42, 8));
        Assert.Equal(7, TrueTypeCmapSubtable.Read(truncated[..^4], 0)!.Map(0x41));
        Assert.Equal(0, TrueTypeCmapSubtable.Read(truncated[..^4], 0)!.Map(0x42));
        Assert.Null(TrueTypeCmapSubtable.Read(truncated[..10], 0));
        Assert.Null(TrueTypeCmapSubtable.Read([0, 99, 0, 0], 0));

        static byte[] CreateGroups(int format, params (int Start, int End, int Glyph)[] groups)
        {
            var data = new List<byte>();
            AddUInt16(data, format);
            AddUInt16(data, 0);
            AddUInt32(data, (uint)(16 + (groups.Length * 12)));
            AddUInt32(data, 0);
            AddUInt32(data, (uint)groups.Length);
            foreach (var (start, end, glyph) in groups)
            {
                AddUInt32(data, (uint)start);
                AddUInt32(data, (uint)end);
                AddUInt32(data, (uint)glyph);
            }

            return [.. data];
        }
    }

    [Fact]
    public void TrueTypeCmapFormat4()
    {
        var sorted = TrueTypeCmapSubtable.Read(CreateSegments((0x41, 0x43, 10 - 0x41), (0x61, 0x62, 20 - 0x61), (0xFFFF, 0xFFFF, 1)), 0)!;
        var unsorted = TrueTypeCmapSubtable.Read(CreateSegments((0x61, 0x62, 20 - 0x61), (0x41, 0x63, 10 - 0x41), (0xFFFF, 0xFFFF, 1)), 0)!;

        Assert.Equal([0, 10, 12, 0, 20, 21, 0, 0], new[] { 0x40, 0x41, 0x43, 0x44, 0x61, 0x62, 0x63, 0xFFFF }.Select(sorted.Map));
        Assert.Equal([10, 12, 20, 21, 10 - 0x41 + 0x63], new[] { 0x41, 0x43, 0x61, 0x62, 0x63 }.Select(unsorted.Map));

        static byte[] CreateSegments(params (int Start, int End, int Delta)[] segments)
        {
            var data = new List<byte>();
            AddUInt16(data, 4);
            AddUInt16(data, 16 + (segments.Length * 8));
            AddUInt16(data, 0);
            AddUInt16(data, segments.Length * 2);
            AddUInt16(data, 0);
            AddUInt16(data, 0);
            AddUInt16(data, 0);
            foreach (var segment in segments)
            {
                AddUInt16(data, segment.End);
            }

            AddUInt16(data, 0);
            foreach (var segment in segments)
            {
                AddUInt16(data, segment.Start);
            }

            foreach (var segment in segments)
            {
                AddUInt16(data, segment.Delta & 0xFFFF);
            }

            foreach (var segment in segments)
            {
                AddUInt16(data, 0);
            }

            return [.. data];
        }
    }

    private static void AddUInt16(List<byte> data, int value)
    {
        data.Add((byte)(value >> 8));
        data.Add((byte)value);
    }

    private static void AddUInt32(List<byte> data, uint value)
    {
        AddUInt16(data, (int)(value >> 16));
        AddUInt16(data, (int)(value & 0xFFFF));
    }

    private static PdfRectangle Round(PdfRectangle rectangle) => new(Math.Round(rectangle.Left, 6, MidpointRounding.AwayFromZero), Math.Round(rectangle.Bottom, 6, MidpointRounding.AwayFromZero), Math.Round(rectangle.Right, 6, MidpointRounding.AwayFromZero), Math.Round(rectangle.Top, 6, MidpointRounding.AwayFromZero));

    private static PdfPoint Round(PdfPoint point) => new(Math.Round(point.X, 6, MidpointRounding.AwayFromZero), Math.Round(point.Y, 6, MidpointRounding.AwayFromZero));

    /// <summary>Opens a page that shows text with a composite font F1 (a CIDFontType2 that is not embedded, DW 1000).</summary>
    private static PdfDocument OpenComposite(string content, string encoding = "/Encoding /Identity-H", string cidFontEntries = "", (int Number, string Body)? encodingStream = null, (int Number, string Body)? toUnicodeStream = null, string? toUnicode = null)
    {
        if (toUnicode is not null)
        {
            toUnicodeStream = (7, HandWrittenPdf.Stream(toUnicode));
        }

        var file = new HandWrittenPdf()
            .Add(1, "<< /Type /Catalog /Pages 2 0 R >>")
            .Add(2, "<< /Type /Pages /Kids [3 0 R] /Count 1 >>")
            .Add(3, "<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Resources << /Font << /F1 4 0 R >> >> /Contents 5 0 R >>")
            .Add(4, "<< /Type /Font /Subtype /Type0 /BaseFont /Custom " + encoding + " /DescendantFonts [8 0 R]" + (toUnicodeStream is not null ? " /ToUnicode 7 0 R" : "") + " >>")
            .Add(5, HandWrittenPdf.Stream(content))
            .Add(8, "<< /Type /Font /Subtype /CIDFontType2 /BaseFont /Custom /CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> /DW 1000 " + cidFontEntries + " >>");
        if (encodingStream is { } e)
        {
            file.Add(e.Number, e.Body);
        }

        if (toUnicodeStream is { } t)
        {
            file.Add(t.Number, t.Body);
        }

        return PdfDocument.Open(file.ToArray());
    }
}
