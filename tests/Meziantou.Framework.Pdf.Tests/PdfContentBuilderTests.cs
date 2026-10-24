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

    [Fact]
    public void GraphicsStateParameters()
    {
        var (page, builder) = Create();
        var state = new PdfExtGState { FillAlpha = 0.5, StrokeAlpha = 0.25, BlendMode = PdfBlendMode.Multiply, LineWidth = 3, LineCap = PdfLineCap.Round, AlphaIsShape = false, OverprintMode = 1 };
        builder.SetGraphicsState(state).SetGraphicsState(state).SetGraphicsState(new PdfExtGState { SoftMask = PdfSoftMask.None });

        var states = page.Resources!.Get<PdfDictionary>(PdfNames.ExtGState)!;
        var first = states.Get<PdfDictionary>(PdfName.Create("GS1"))!;
        Assert.Equal("/GS1 gs\n/GS1 gs\n/GS2 gs\n", Encoding.ASCII.GetString(page.GetContentData()));
        Assert.Equal(0.5, first.GetNumber(PdfName.Create("ca")));
        Assert.Equal(0.25, first.GetNumber(PdfName.Create("CA")));
        Assert.Equal("Multiply", first.GetName(PdfName.Create("BM"))!.Value);
        Assert.Equal(1, first.GetNumber(PdfName.Create("LC")));
        Assert.False(first.GetBoolean(PdfName.Create("AIS"), defaultValue: true));
        Assert.Equal("None", states.Get<PdfDictionary>(PdfName.Create("GS2"))!.GetName(PdfNames.SMask)!.Value);
        Assert.Throws<InvalidOperationException>(() => builder.SetGraphicsState(new PdfExtGState { FillAlpha = 2 }));
    }

    [Fact]
    public void AxialAndRadialShadings()
    {
        var (page, builder) = Create();
        var axial = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 0, 100, 0, PdfFunction.Exponential(PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 0, 1)), extendStart: true, extendEnd: true);
        var radial = PdfShading.Radial(PdfDeviceColorSpace.Gray, 50, 50, 0, 50, 50, 50, PdfFunction.Gradient((0, PdfColor.White), (0.5, PdfColor.FromGray(0.5)), (1, PdfColor.Black)));
        builder.PaintShading(axial).PaintShading(radial).PaintShading(axial);

        var shadings = page.Resources!.Get<PdfDictionary>(PdfNames.Shading)!;
        var first = shadings.Get<PdfDictionary>(PdfName.Create("Sh1"))!;
        var function = first.Get<PdfDictionary>(PdfName.Create("Function"))!;
        var stitching = shadings.Get<PdfDictionary>(PdfName.Create("Sh2"))!.Get<PdfDictionary>(PdfName.Create("Function"))!;
        Assert.Equal("/Sh1 sh\n/Sh2 sh\n/Sh1 sh\n", Encoding.ASCII.GetString(page.GetContentData()));
        Assert.Equal(2, first.GetNumber(PdfName.Create("ShadingType")));
        Assert.Equal("DeviceRGB", first.GetName(PdfNames.ColorSpace)!.Value);
        Assert.Equal("[0 0 100 0]", first.Get<PdfArray>(PdfName.Create("Coords"))!.ToString());
        Assert.Equal("[true true]", first.Get<PdfArray>(PdfName.Create("Extend"))!.ToString());
        Assert.Equal("[1 0 0]", function.Get<PdfArray>(PdfName.Create("C0"))!.ToString());
        Assert.Equal(3, stitching.GetNumber(PdfName.Create("FunctionType")));
        Assert.Equal("[0.5]", stitching.Get<PdfArray>(PdfName.Create("Bounds"))!.ToString());
        Assert.Equal("[0 1 0 1]", stitching.Get<PdfArray>(PdfName.Create("Encode"))!.ToString());
    }

    [Theory]
    [InlineData(4, 3 * (1 + 8 + 6))]
    [InlineData(5, 4 * (8 + 6))]
    [InlineData(6, 1 + (12 * 8) + (4 * 6))]
    [InlineData(7, 1 + (16 * 8) + (4 * 6))]
    public void MeshShadings(int type, int dataLength)
    {
        var red = PdfColor.FromRgb(1, 0, 0);
        PdfPoint[] boundary = [new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(1, 3), new(2, 3), new(3, 3), new(3, 2), new(3, 1), new(3, 0), new(2, 0), new(1, 0)];
        var shading = type switch
        {
            4 => PdfShading.TriangleMesh(PdfDeviceColorSpace.Rgb, [(new PdfMeshVertex(0, 0, red), new PdfMeshVertex(10, 0, red), new PdfMeshVertex(0, 10, red))]),
            5 => PdfShading.LatticeMesh(PdfDeviceColorSpace.Rgb, 2, [new(0, 0, red), new(10, 0, red), new(0, 10, red), new(10, 10, red)]),
            6 => PdfShading.CoonsPatchMesh(PdfDeviceColorSpace.Rgb, [new PdfPatch(boundary, [red, red, red, red])]),
            _ => PdfShading.TensorPatchMesh(PdfDeviceColorSpace.Rgb, [new PdfPatch(boundary, [red, red, red, red])]),
        };
        var (page, builder) = Create();
        builder.PaintShading(shading);

        var stream = page.Resources!.Get<PdfDictionary>(PdfNames.Shading)!.Get<PdfStream>(PdfName.Create("Sh1"))!;
        Assert.Equal(type, stream.Dictionary.GetNumber(PdfName.Create("ShadingType")));
        Assert.HasCount(dataLength, stream.GetDecodedData());
        Assert.HasCount(10, stream.Dictionary.Get<PdfArray>(PdfNames.Decode)!);
    }

    [Fact]
    public void CoonsPatchOfAGridHasTheInnerPointsOfTheGrid()
    {
        // The boundary of a bilinear grid: the inner points of the equivalent tensor-product patch are the inner points of the grid
        PdfPoint[] boundary = [new(0, 0), new(0, 1), new(0, 2), new(0, 3), new(1, 3), new(2, 3), new(3, 3), new(3, 2), new(3, 1), new(3, 0), new(2, 0), new(1, 0)];
        var patch = new PdfPatch(boundary, [PdfColor.Black, PdfColor.Black, PdfColor.Black, PdfColor.Black]);

        var inner = patch.GetPoints(includeInnerPoints: true).Skip(12).ToArray();

        Assert.Equal([new PdfPoint(1, 1), new PdfPoint(1, 2), new PdfPoint(2, 2), new PdfPoint(2, 1)], inner.Select(point => new PdfPoint(Math.Round(point.X, 9, MidpointRounding.AwayFromZero), Math.Round(point.Y, 9, MidpointRounding.AwayFromZero))));
        Assert.True(patch.InnerPoints.IsEmpty);
    }

    [Fact]
    public void FunctionsAreChecked()
    {
        var rgb = PdfFunction.Exponential(PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 1, 0));

        Assert.Throws<ArgumentException>(() => PdfShading.Axial(PdfDeviceColorSpace.Gray, 0, 0, 1, 0, rgb));
        Assert.Throws<ArgumentException>(() => PdfFunction.Exponential([0], [0, 1]));
        Assert.Throws<ArgumentException>(() => PdfFunction.Stitching([rgb, rgb], [2]));
        Assert.Throws<ArgumentException>(() => PdfFunction.Gradient((0, PdfColor.Black), (1, PdfColor.FromRgb(1, 1, 1))));
        Assert.Throws<ArgumentException>(() => PdfFunction.Sampled([2], 8, [0, 1], [0, 1], [0]));
        Assert.Throws<ArgumentException>(() => PdfFunction.PostScript("dup", [0, 1], [0, 1]));
        Assert.Equal(1, rgb.InputCount);
        Assert.Equal(3, rgb.OutputCount);
    }

    [Fact]
    public void SampledAndPostScriptFunctionsAreStreams()
    {
        var (page, builder) = Create();
        var sampled = PdfFunction.Sampled([2], 8, [0, 1], [0, 1, 0, 1, 0, 1], [255, 0, 0, 0, 255, 0]);
        var postScript = PdfFunction.PostScript("{ exch 0 3 -1 roll }", [0, 1, 0, 1], [0, 1, 0, 1, 0, 1]);
        builder.PaintShading(PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 0, 1, 0, sampled)).PaintShading(PdfShading.FunctionBased(PdfDeviceColorSpace.Rgb, postScript, matrix: new PdfMatrix(100, 0, 0, 100, 0, 0)));

        var shadings = page.Resources!.Get<PdfDictionary>(PdfNames.Shading)!;
        var sampledStream = shadings.Get<PdfDictionary>(PdfName.Create("Sh1"))!.Get<PdfStream>(PdfName.Create("Function"))!;
        var postScriptStream = shadings.Get<PdfDictionary>(PdfName.Create("Sh2"))!.Get<PdfStream>(PdfName.Create("Function"))!;
        Assert.Equal([255, 0, 0, 0, 255, 0], sampledStream.GetDecodedData());
        Assert.Equal("[2]", sampledStream.Dictionary.Get<PdfArray>(PdfName.Create("Size"))!.ToString());
        Assert.Equal("{ exch 0 3 -1 roll }", Encoding.ASCII.GetString(postScriptStream.GetDecodedData()));
        Assert.Equal(4, postScriptStream.Dictionary.GetNumber(PdfName.Create("FunctionType")));
    }

    [Fact]
    public void Patterns()
    {
        var (page, builder) = Create();
        var document = page.Document;
        var colored = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10);
        colored.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 5, 5).Fill();
        var stencil = document.CreateTilingPattern(new PdfRectangle(0, 0, 10, 10), 10, 10, isColored: false);
        stencil.CreateContentBuilder().Rectangle(0, 0, 5, 5).Fill();
        var shading = new PdfShadingPattern(PdfShading.Axial(PdfDeviceColorSpace.Gray, 0, 0, 1, 0, PdfFunction.Exponential([0], [1])), new PdfMatrix(100, 0, 0, 100, 0, 0));
        builder.SetFillPattern(colored).SetStrokePattern(shading).SetFillPattern(stencil, PdfColor.FromRgb(0, 0, 1)).SetStrokePattern(stencil, PdfColor.FromRgb(0, 1, 0));

        Assert.Equal("/Pattern cs\n/P1 scn\n/Pattern CS\n/P2 SCN\n/CS1 cs\n0 0 1 /P3 scn\n/CS1 CS\n0 1 0 /P3 SCN\n", Encoding.ASCII.GetString(page.GetContentData()));
        Assert.Equal("[/Pattern /DeviceRGB]", page.Resources!.Get<PdfDictionary>(PdfNames.ColorSpace)!.Get<PdfArray>(PdfName.Create("CS1"))!.ToString());
        var stencilStream = page.Resources.Get<PdfDictionary>(PdfNames.Pattern)!.Get<PdfStream>(PdfName.Create("P3"))!;
        Assert.Equal(2, stencilStream.Dictionary.GetNumber(PdfName.Create("PaintType")));
        Assert.Equal("0 0 5 5 re\nf\n", Encoding.ASCII.GetString(stencilStream.GetDecodedData()));
        Assert.Throws<ArgumentException>(() => builder.SetFillPattern(stencil));
        Assert.Throws<ArgumentException>(() => builder.SetFillPattern(colored, PdfColor.Black));
        Assert.Throws<ArgumentException>(() => builder.SetFillPattern(new PdfDocument().CreateTilingPattern(new PdfRectangle(0, 0, 1, 1), 1, 1)));
    }

    [Fact]
    public void FormsAndSoftMasks()
    {
        var (page, builder) = Create();
        var document = page.Document;
        var form = document.CreateForm(new PdfRectangle(0, 0, 50, 50), new PdfMatrix(2, 0, 0, 2, 0, 0));
        form.CreateContentBuilder().SetFillRgb(0, 1, 0).Rectangle(0, 0, 50, 50).Fill();
        var group = document.CreateForm(new PdfRectangle(0, 0, 100, 100), group: new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Gray, Isolated = true, Knockout = true });
        group.CreateContentBuilder().SetFillGray(1).Rectangle(0, 0, 50, 100).Fill();
        builder.DrawForm(form).SetGraphicsState(new PdfExtGState { SoftMask = PdfSoftMask.FromLuminosity(group, PdfColor.Black) }).DrawForm(form);

        var xobjects = page.Resources!.Get<PdfDictionary>(PdfNames.XObject)!;
        var mask = page.Resources.Get<PdfDictionary>(PdfNames.ExtGState)!.Get<PdfDictionary>(PdfName.Create("GS1"))!.Get<PdfDictionary>(PdfNames.SMask)!;
        var groupDictionary = group.Reference.Resolve() is PdfStream stream ? stream.Dictionary.Get<PdfDictionary>(PdfNames.Group)! : null;
        Assert.Equal("/Fm1 Do\n/GS1 gs\n/Fm1 Do\n", Encoding.ASCII.GetString(page.GetContentData()));
        Assert.Single(xobjects);
        Assert.Equal("[2 0 0 2 0 0]", xobjects.Get<PdfStream>(PdfName.Create("Fm1"))!.Dictionary.Get<PdfArray>(PdfNames.Matrix)!.ToString());
        Assert.Equal("Luminosity", mask.GetName(PdfNames.S)!.Value);
        Assert.Same(group.Reference, mask[PdfNames.G]);
        Assert.Equal("[0]", mask.Get<PdfArray>(PdfName.Create("BC"))!.ToString());
        Assert.Equal("Transparency", groupDictionary!.GetName(PdfNames.S)!.Value);
        Assert.True(groupDictionary.GetBoolean(PdfNames.I));
        Assert.True(groupDictionary.GetBoolean(PdfNames.K));
        Assert.Throws<ArgumentException>(() => PdfSoftMask.FromLuminosity(form));
        Assert.Throws<ArgumentException>(() => builder.DrawForm(new PdfDocument().CreateForm(new PdfRectangle(0, 0, 1, 1))));
    }

    private static (PdfPage Page, PdfContentBuilder Builder) Create()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        return (page, page.CreateContentBuilder());
    }
}
