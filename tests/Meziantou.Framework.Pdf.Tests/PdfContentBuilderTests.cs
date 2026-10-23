using System.Text;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Tests;

public sealed class PdfContentBuilderTests
{
    [Fact]
    public void Operators()
    {
        var (page, builder) = Create();
        builder
            .SaveGraphicsState()
            .Transform(new PdfMatrix(1, 0, 0, 1, 10.5, -3))
            .SetLineWidth(2)
            .SetLineCap(PdfLineCap.Round)
            .SetLineJoin(PdfLineJoin.Bevel)
            .SetMiterLimit(4)
            .SetDashPattern([3, 2], 1)
            .SetFlatness(1)
            .SetRenderingIntent("Perceptual")
            .MoveTo(0, 0)
            .LineTo(10, 0)
            .CurveTo(1, 2, 3, 4, 5, 6)
            .CurveToFromCurrentPoint(1, 2, 3, 4)
            .CurveToEndPoint(1, 2, 3, 4)
            .ClosePath()
            .Rectangle(1, 2, 3, 4)
            .Clip(PdfFillRule.EvenOdd)
            .EndPath()
            .SetStrokeCmyk(0, 0.5, 1, 0)
            .SetFillGray(0.25)
            .SetFillColorSpace("DeviceRGB")
            .SetFillColorComponents(1, 0, 0)
            .Rectangle(0, 0, 1, 1)
            .FillAndStroke(PdfFillRule.EvenOdd)
            .RestoreGraphicsState();

        Assert.Equal(
            """
            q
            1 0 0 1 10.5 -3 cm
            2 w
            1 J
            2 j
            4 M
            [3 2] 1 d
            1 i
            /Perceptual ri
            0 0 m
            10 0 l
            1 2 3 4 5 6 c
            1 2 3 4 v
            1 2 3 4 y
            h
            1 2 3 4 re
            W*
            n
            0 0.5 1 0 K
            0.25 g
            /DeviceRGB cs
            1 0 0 scn
            0 0 1 1 re
            B*
            Q

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            Encoding.ASCII.GetString(page.GetContentData()));
    }

    [Fact]
    public void TextOperators()
    {
        var (page, builder) = Create();
        builder
            .BeginText()
            .SetFont(PdfFont.GetStandardFont(PdfStandardFont.Courier), 12)
            .SetCharacterSpacing(1)
            .SetWordSpacing(2)
            .SetHorizontalScaling(90)
            .SetTextLeading(14)
            .SetTextRenderingMode(PdfTextRenderingMode.Invisible)
            .SetTextRise(3)
            .MoveTextPosition(10, 20)
            .MoveTextPositionAndSetLeading(0, -14)
            .SetTextMatrix(new PdfMatrix(1, 0, 0, 1, 5, 5))
            .MoveToNextLine()
            .ShowText("a(b)")
            .ShowTextOnNextLine("c")
            .ShowText(["A", -120, "V"])
            .EndText();

        Assert.Equal(
            """
            BT
            /F1 12 Tf
            1 Tc
            2 Tw
            90 Tz
            14 TL
            3 Tr
            3 Ts
            10 20 Td
            0 -14 TD
            1 0 0 1 5 5 Tm
            T*
            (a\(b\)) Tj
            (c) '
            [(A) -120 (V)] TJ
            ET

            """.Replace("\r\n", "\n", StringComparison.Ordinal),
            Encoding.ASCII.GetString(page.GetContentData()));
        Assert.Equal("Courier", page.Resources!.Get<PdfDictionary>(PdfName.Create("Font"))!.Get<PdfDictionary>(PdfName.Create("F1"))!.GetName(PdfName.Create("BaseFont"))!.Value);
    }

    [Fact]
    public void MarkedContent()
    {
        var (page, builder) = Create();
        builder
            .BeginMarkedContent("Artifact")
            .EndMarkedContent()
            .BeginMarkedContent("Span", new PdfDictionary { [PdfName.Create("MCID")] = PdfInteger.Get(0) })
            .MarkPoint("Point")
            .MarkPoint("Point", new PdfDictionary { [PdfName.Create("A")] = PdfBoolean.True })
            .EndMarkedContent()
            .AppendRawContent("% raw"u8);

        Assert.Equal("/Artifact BMC\nEMC\n/Span <</MCID 0>> BDC\n/Point MP\n/Point <</A true>> DP\nEMC\n% raw\n", Encoding.ASCII.GetString(page.GetContentData()));
    }

    [Fact]
    public void FontsAreSharedAndNamedOnce()
    {
        var (page, builder) = Create();
        var helvetica = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        builder.DrawText("a", helvetica, 10, 0, 0).DrawText("b", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, 0, 0).DrawText("c", helvetica, 10, 0, 0);

        var fonts = page.Resources!.Get<PdfDictionary>(PdfName.Create("Font"))!;
        Assert.Equal(["F1", "F2"], fonts.Keys.Select(k => k.Value));
        Assert.Contains("/F1 10 Tf\n0 0 Td\n(c)", Encoding.ASCII.GetString(page.GetContentData()), StringComparison.Ordinal);
    }

    [Fact]
    public void StructureIsChecked()
    {
        var (_, builder) = Create();

        Assert.Throws<InvalidOperationException>(() => builder.RestoreGraphicsState());
        Assert.Throws<InvalidOperationException>(() => builder.EndText());
        Assert.Throws<InvalidOperationException>(() => builder.EndMarkedContent());
        Assert.Throws<InvalidOperationException>(() => builder.ShowText("x"));
        builder.BeginText();
        Assert.Throws<InvalidOperationException>(() => builder.BeginText());
        Assert.Throws<InvalidOperationException>(() => builder.ShowText("no font"));
        Assert.Throws<InvalidOperationException>(() => builder.MoveTo(0, 0));
        Assert.Throws<InvalidOperationException>(() => builder.SaveGraphicsState());
        builder.EndText();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetLineWidth(-1));
        Assert.Throws<ArgumentOutOfRangeException>(() => PdfColor.FromRgb(2, 0, 0));
    }

    [Fact]
    public void FontIsPartOfTheGraphicsState()
    {
        var (_, builder) = Create();
        builder.SaveGraphicsState().BeginText().SetFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10).EndText().RestoreGraphicsState().BeginText();

        Assert.Throws<InvalidOperationException>(() => builder.ShowText("x"));
    }

    [Fact]
    public void DrawTextWithACharacterTheFontDoesNotHaveLeavesTheContentUnchanged()
    {
        var (page, builder) = Create();

        Assert.Throws<ArgumentException>(() => builder.DrawText("\u4E2D", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10, 0, 0));
        Assert.Empty(page.GetContentData());
    }

    [Fact]
    public void EllipseIsFourCurves()
    {
        var (page, builder) = Create();
        builder.Circle(0, 0, 10).Fill();

        var content = Encoding.ASCII.GetString(page.GetContentData());
        Assert.Equal(4, content.Split(" c\n").Length - 1);
        Assert.StartsWith("10 0 m\n10 5.522847 5.522847 10 0 10 c", content, StringComparison.Ordinal);
    }

    private static (PdfPage Page, PdfContentBuilder Builder) Create()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        return (page, page.CreateContentBuilder());
    }
}
