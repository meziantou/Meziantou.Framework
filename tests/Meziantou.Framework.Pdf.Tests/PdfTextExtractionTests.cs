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
            var content = (i < 19 ? $"/X{i + 1} Do /X{i + 1} Do " : "") + "BT /F1 1 Tf (" + new string('x', 2000) + ") Tj ET";
            file.Add(10 + i, HandWrittenPdf.Stream(content, " /Type /XObject /Subtype /Form /Resources 4 0 R"));
        }

        var document = PdfDocument.Open(file.ToArray());

        Assert.Throws<PdfResourceLimitException>(() => document.Pages[0].ExtractText());
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
}
