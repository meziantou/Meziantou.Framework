using System.Globalization;
using System.Text;
using Meziantou.Framework.Imaging;
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

    [Fact]
    public void Example_TextBox()
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        var text = "The quick brown fox jumps over the lazy dog. " + string.Concat(Enumerable.Repeat("Lorem ipsum dolor sit amet. ", 20));

        #region Pdf_TextBox
        // The text is broken into lines that fit the width of the box; what does not fit in its height is returned
        builder.DrawTextBox(text, font, 10, new PdfRectangle(50, 400, 250, 550), new PdfTextBoxOptions { Alignment = PdfTextAlignment.Justify }, out var remaining);
        if (remaining.Length > 0)
        {
            builder.DrawTextBox(remaining, font, 10, new PdfRectangle(50, 100, 250, 380));
        }
        #endregion

        Assert.NotEmpty(remaining);
        Assert.StartsWith("The quick brown fox", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(PdfTextAlignment.Left, "10")]
    [InlineData(PdfTextAlignment.Center, "22")]
    [InlineData(PdfTextAlignment.Right, "34")]
    [InlineData(PdfTextAlignment.Justify, "10")]
    public void TextBoxLines(PdfTextAlignment alignment, string x)
    {
        // Courier: every glyph is 600 units wide, 6 points at 10 points; the box is 10 characters wide
        var (page, builder) = Create();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Courier);
        builder.DrawTextBox("aaaaaa bbbbbbbbbbbbbbbbbbbbbbbbb c\nd", font, 10, new PdfRectangle(10, 0, 70, 200), new PdfTextBoxOptions { Alignment = alignment, LineHeight = 2 }, out var remaining);

        Assert.Equal("", remaining);
        var content = Encoding.ASCII.GetString(page.GetContentData());
        var lines = content.Split('\n').Where(line => line.EndsWith(" Tj", StringComparison.Ordinal)).ToList();
        Assert.Equal(["(aaaaaa) Tj", "(bbbbbbbbbb) Tj", "(bbbbbbbbbb) Tj", "(bbbbb c) Tj", "(d) Tj"], lines);

        // The first baseline is below the top by the ascent (629 units), the next ones 20 points lower; the lines of one word are not justified
        Assert.Contains("1 0 0 1 " + x + " 193.71 Tm", content, StringComparison.Ordinal);
        Assert.Contains("1 0 0 1 10 173.71 Tm", content, StringComparison.Ordinal);
    }

    [Fact]
    public void TextBoxReturnsWhatDoesNotFit()
    {
        var (page, builder) = Create();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Courier);

        // 12 points high: one line of 10 points (ascent 6.29 + descent 1.57) fits, not two; the baseline is 12 - 6.29
        builder.DrawTextBox("one two three", font, 10, new PdfRectangle(0, 0, 30, 12), options: null, out var remaining);
        Assert.Equal("two three", remaining);
        Assert.Throws<ArgumentException>(() => builder.DrawTextBox("\u4E2D", font, 10, new PdfRectangle(0, 0, 30, 12)));
        builder.DrawTextBox("x", font, 10, new PdfRectangle(0, 0, 30, 2), options: null, out remaining);
        Assert.Equal("x", remaining);

        Assert.Equal("BT\n/F1 10 Tf\n1 0 0 1 0 5.71 Tm\n(one) Tj\nET\n", Encoding.ASCII.GetString(page.GetContentData()));
    }

    [Fact]
    public void JustifiedTextBoxesStretchTheSpaces()
    {
        var font = PdfFont.Load(File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf"));
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder();
        builder.DrawTextBox("Office AVATAR Office AVATAR Office", font, 12, new PdfRectangle(0, 0, 120, 100), new PdfTextBoxOptions { Alignment = PdfTextAlignment.Justify });

        var text = PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText();
        Assert.Contains("Office", text, StringComparison.Ordinal);
        Assert.Contains("AVATAR", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("extra\u00ADordinary word", new[] { "(extra-) Tj", "(ordinary) Tj", "(word) Tj" })]
    [InlineData("a\u00ADb", new[] { "(ab) Tj" })]
    [InlineData("one\u2029two", new[] { "(one) Tj", "(two) Tj" })]
    [InlineData("well-knowing words", new[] { "(well-) Tj", "(knowing) Tj", "(words) Tj" })]
    public void TextBoxBreaksAtUnicodeBreakOpportunities(string text, string[] expected)
    {
        // Courier: 6 points per character at 10 points; the box is 10 characters wide
        var (page, builder) = Create();
        builder.DrawTextBox(text, PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, new PdfRectangle(0, 0, 60, 200));

        var lines = Encoding.ASCII.GetString(page.GetContentData()).Split('\n').Where(line => line.EndsWith(" Tj", StringComparison.Ordinal)).ToArray();
        Assert.Equal(expected, lines);
    }

    [Fact]
    public void TextBoxHyphenatesTheWordThatDoesNotFit()
    {
        var (page, builder) = Create();
        var font = PdfFont.GetStandardFont(PdfStandardFont.Courier);
        var options = new PdfTextBoxOptions { Hyphenator = new PdfPatternHyphenator(Hyphenator.Parse("hy3ph he2n hena4 hen5at 1na n2at 1tio 2io")) };

        // hy-phen-ation: "the hyphen-" is 11 characters, "the hy-" fits in 10
        builder.DrawTextBox("the hyphenation", font, 10, new PdfRectangle(0, 0, 60, 12), options, out var remaining);

        Assert.Equal("phenation", remaining);
        Assert.Equal("BT\n/F1 10 Tf\n1 0 0 1 0 5.71 Tm\n(the hy-) Tj\nET\n", Encoding.ASCII.GetString(page.GetContentData()));
    }

    [Fact]
    public void TextBoxUsesACustomHyphenator()
    {
        var (page, builder) = Create();
        builder.DrawTextBox("abcdefghijklmnop", PdfFont.GetStandardFont(PdfStandardFont.Courier), 10, new PdfRectangle(0, 0, 60, 200), new PdfTextBoxOptions { Hyphenator = new EveryFourLettersHyphenator() });

        var lines = Encoding.ASCII.GetString(page.GetContentData()).Split('\n').Where(line => line.EndsWith(" Tj", StringComparison.Ordinal)).ToArray();
        Assert.Equal(["(abcdefgh-) Tj", "(ijklmnop) Tj"], lines);
    }

    [Fact]
    public void TextBoxBreaksBetweenIdeographs()
    {
        var font = PdfFont.Load(File.ReadAllBytes(TestHarness.RepositoryPaths.Fonts / "noto-sans-cjk" / "NotoSansCJKjp-Regular.subset.otf"));
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder();

        // The glyphs are 10 points wide: two per line, but the ideographic full stop cannot start a line
        builder.DrawTextBox("\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002", font, 10, new PdfRectangle(20, 100, 45, 500));

        var lines = PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractTextLayout().Lines.Select(line => line.Text).ToArray();
        Assert.Equal(["\u65E5\u672C", "\u8A9E\u306E", "\u30C6\u30AD", "\u30B9", "\u30C8\u3002"], lines);
    }

    [Fact]
    public void TableColumnWidths()
    {
        var table = new PdfTable(
            new PdfTableColumn(PdfTableColumnWidth.Fixed(100)),
            new PdfTableColumn(PdfTableColumnWidth.Relative()),
            new PdfTableColumn(PdfTableColumnWidth.Relative(3)),
            new PdfTableColumn())
        {
            Font = PdfFont.GetStandardFont(PdfStandardFont.Courier),
        };
        table.AddRow("a", "b", "c", "abc");

        // The automatic column is as wide as "abc" (18 points) and the padding (2 x 4); the relative columns share the 374 points left
        Assert.Equal([100, 93.5, 280.5, 26], table.GetColumnWidths(500));
    }

    [Fact]
    public void TableAutomaticColumnsShrinkDownToTheirWidestWord()
    {
        var table = new PdfTable(new PdfTableColumn(), new PdfTableColumn()) { Font = PdfFont.GetStandardFont(PdfStandardFont.Courier) };
        table.AddRow("aaaa bbbb", "cccc dddd");

        // Widest paragraph 54 + 8, widest word 24 + 8: the 36 points over the minimum are shared in proportion
        Assert.Equal([50, 50], table.GetColumnWidths(100));
        Assert.Equal([62, 62], table.GetColumnWidths(500));
        Assert.Equal([10, 10], table.GetColumnWidths(20));
    }

    [Fact]
    public void TableCellsArePlacedInTheirColumns()
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A4).CreateContentBuilder();
        var table = new PdfTable(
            new PdfTableColumn(PdfTableColumnWidth.Fixed(100)),
            new PdfTableColumn(PdfTableColumnWidth.Fixed(100), PdfTextAlignment.Center),
            new PdfTableColumn(PdfTableColumnWidth.Fixed(100), PdfTextAlignment.Right))
        {
            Font = PdfFont.GetStandardFont(PdfStandardFont.Courier),
        };
        table.AddRow("one", "two", "abc");
        table.AddRow("wide").Cells[0].ColumnSpan = 3;

        var result = builder.DrawTable(table, new PdfRectangle(50, 100, 400, 700));

        // A row is the height of a line (ascent 6.29 + descent 1.57) and the padding (2 x 4)
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.RowCount);
        Assert.Equal(700 - (2 * 15.86), result.Bottom, 6);
        var words = PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractTextLayout().Lines.SelectMany(line => line.Words).ToDictionary(word => word.Text, word => word.Bounds.Left, StringComparer.Ordinal);
        Assert.Equal(54, words["one"], 3);
        Assert.Equal(150 + 50 - 9, words["two"], 3);
        Assert.Equal(350 - 4 - 18, words["abc"], 3);
        Assert.Equal(54, words["wide"], 3);
    }

    [Fact]
    public void TableRowsContinueOnTheNextPagesWithTheirHeader()
    {
        var document = new PdfDocument();
        var table = new PdfTable(new PdfTableColumn(PdfTableColumnWidth.Fixed(100)), new PdfTableColumn(PdfTableColumnWidth.Relative()))
        {
            Font = PdfFont.GetStandardFont(PdfStandardFont.Courier),
            CellPadding = 2,
        };
        table.AddHeaderRow("Header", "Value");
        for (var i = 0; i < 30; i++)
        {
            table.AddRow("Row" + i.ToString(CultureInfo.InvariantCulture), "x");
        }

        // A row is 7.86 + 4 points high: 8 rows fit in 100 points, the header and 7 rows
        var area = new PdfRectangle(50, 600, 300, 700);
        var result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area);
        var rowCounts = new List<int> { result.RowCount };
        while (!result.IsComplete)
        {
            result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area, result);
            rowCounts.Add(result.RowCount);
        }

        Assert.Equal([8, 7, 7, 7, 2], rowCounts);
        var reloaded = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(5, reloaded.Pages.Count);
        var rows = new List<string>();
        foreach (var page in reloaded.Pages)
        {
            var lines = page.ExtractTextLayout().Lines;
            Assert.Equal("Header", lines[0].Words[0].Text);
            Assert.Equal(700 - 2 - 6.29, lines[0].Words[0].Glyphs[0].Origin.Y, 3);
            rows.AddRange(lines.Skip(1).Select(line => line.Words[0].Text));
        }

        Assert.Equal(Enumerable.Range(0, 30).Select(i => "Row" + i.ToString(CultureInfo.InvariantCulture)), rows);
    }

    [Fact]
    public void TableRowTallerThanThePageIsSplit()
    {
        var document = new PdfDocument();
        var table = new PdfTable(new PdfTableColumn(PdfTableColumnWidth.Fixed(60)), new PdfTableColumn(PdfTableColumnWidth.Fixed(60)))
        {
            Font = PdfFont.GetStandardFont(PdfStandardFont.Courier),
            CellPadding = 0,
        };
        table.AddRow(string.Join(' ', Enumerable.Repeat("word", 20)), "short");

        // 10 lines of "word word" 12 points apart; 4 fit in 50 points (7.86 + 3 x 12)
        var area = new PdfRectangle(0, 650, 120, 700);
        var result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area);
        Assert.False(result.IsComplete);
        Assert.Equal(650, result.Bottom);
        while (!result.IsComplete)
        {
            result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area, result);
        }

        var reloaded = PdfDocument.Open(document.ToByteArray());
        Assert.Equal(3, reloaded.Pages.Count);
        Assert.Equal([8, 8, 4], reloaded.Pages.Select(page => page.ExtractText().Split((char[])[' ', '\n'], StringSplitOptions.RemoveEmptyEntries).Count(word => word == "word")));
        Assert.Contains("short", reloaded.Pages[0].ExtractText(), StringComparison.Ordinal);
        Assert.DoesNotContain("short", reloaded.Pages[1].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void TableStartsOnTheNextPageWhenNoRowFits()
    {
        var document = new PdfDocument();
        var table = new PdfTable(new PdfTableColumn(PdfTableColumnWidth.Fixed(100)));
        table.AddHeaderRow("Header");
        table.AddRow("Row");

        // The header (9.25 + 8 points high) fits, not the row after it: nothing is drawn
        var page = document.Pages.Add(PdfSize.A4);
        var result = page.CreateContentBuilder().DrawTable(table, new PdfRectangle(0, 0, 100, 25));
        Assert.False(result.IsComplete);
        Assert.Equal(0, result.RowCount);
        Assert.Equal(25, result.Bottom);
        Assert.Empty(page.GetContentData());

        Assert.Throws<InvalidOperationException>(() => page.CreateContentBuilder().DrawTable(table, new PdfRectangle(0, 0, 100, 25), result));
        Assert.Throws<InvalidOperationException>(() => page.CreateContentBuilder().DrawTable(table, new PdfRectangle(0, 0, 100, 10)));

        result = page.CreateContentBuilder().DrawTable(table, new PdfRectangle(0, 0, 100, 500), result);
        Assert.True(result.IsComplete);
        Assert.Equal(2, result.RowCount);
        Assert.Throws<ArgumentException>(() => page.CreateContentBuilder().DrawTable(table, new PdfRectangle(0, 0, 100, 500), result));
    }

    [Fact]
    public void TableErrorsLeaveTheContentUnchanged()
    {
        var (page, builder) = Create();
        var table = new PdfTable(new PdfTableColumn(), new PdfTableColumn());
        table.AddRow("a", "b").AddCell("c");
        Assert.Throws<ArgumentException>(() => builder.DrawTable(table, new PdfRectangle(0, 0, 100, 100)));

        table.Rows.Clear();
        table.AddRow("\u4E2D");
        Assert.Throws<ArgumentException>(() => builder.DrawTable(table, new PdfRectangle(0, 0, 100, 100)));
        Assert.Throws<InvalidOperationException>(() => builder.DrawTable(new PdfTable(), new PdfRectangle(0, 0, 100, 100)));
        Assert.Empty(page.GetContentData());
    }

    [Fact]
    public void Example_Table()
    {
        using var directory = TemporaryDirectory.Create();
        var outputPath = directory / "invoice.pdf";

        #region Pdf_Table
        var document = new PdfDocument();
        var bold = PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold);
        var table = new PdfTable(
            new PdfTableColumn(PdfTableColumnWidth.Relative()), // the rest of the width
            new PdfTableColumn(PdfTableColumnWidth.Auto, PdfTextAlignment.Right), // the width of the widest cell
            new PdfTableColumn(PdfTableColumnWidth.Fixed(80), PdfTextAlignment.Right))
        {
            FontSize = 10,
            CellPadding = 5,
            BorderColor = PdfColor.FromGray(0.6),
        };

        // The header rows are drawn again at the top of each page
        var header = table.AddHeaderRow("Description", "Quantity", "Amount");
        header.Font = bold;
        header.Background = PdfColor.FromGray(0.9);

        for (var i = 1; i <= 80; i++)
        {
            table.AddRow($"Item {i}: the text of a cell is broken into lines that fit the width of its column", "1", "10.00 €");
        }

        var total = table.AddRow("Total", "", "800.00 €");
        total.Font = bold;
        total.Cells[0].ColumnSpan = 2;
        total.Cells.RemoveAt(1);

        // Draw the rows that fit in the area of the page, then continue on new pages
        var area = new PdfRectangle(50, 50, PdfSize.A4.Width - 50, PdfSize.A4.Height - 50);
        var result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area);
        while (!result.IsComplete)
        {
            result = document.Pages.Add(PdfSize.A4).CreateContentBuilder().DrawTable(table, area, result);
        }

        document.Save(outputPath);
        #endregion

        var reloaded = PdfDocument.Open(outputPath);
        Assert.True(reloaded.Pages.Count > 1);
        Assert.All(reloaded.Pages, page => Assert.StartsWith("Description", page.ExtractText(), StringComparison.Ordinal));
        Assert.Contains("800.00 €", reloaded.Pages[^1].ExtractText(), StringComparison.Ordinal);
    }

    [Fact]
    public void Example_TextBoxHyphenation()
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder();
        var font = PdfFont.GetStandardFont(PdfStandardFont.TimesRoman);

        #region Pdf_TextBoxHyphenation
        // The hyphenation patterns of a language, in the format of TeX: Hyphenator.Load reads them from files
        var hyphenator = Hyphenator.Parse("hy3ph he2n hena4 hen5at 1na n2at 1tio 2io");
        var options = new PdfTextBoxOptions
        {
            Alignment = PdfTextAlignment.Justify,
            Hyphenator = new PdfPatternHyphenator(hyphenator),
        };

        // A word that does not fit at the end of a line is broken at a hyphenation point, and a hyphen is drawn
        builder.DrawTextBox("A narrow column needs hyphenation", font, 12, new PdfRectangle(50, 400, 180, 550), options);
        #endregion

        Assert.Contains("hy", PdfDocument.Open(document.ToByteArray()).Pages[0].ExtractText(), StringComparison.Ordinal);
    }

    private sealed class EveryFourLettersHyphenator : IPdfHyphenator
    {
        public int Hyphenate(ReadOnlySpan<char> word, Span<int> breakPositions)
        {
            var count = 0;
            for (var i = 4; i < word.Length; i += 4)
            {
                breakPositions[count++] = i;
            }

            return count;
        }
    }

    [Fact]
    public void AFailedOperatorLeavesTheContentUnchanged()
    {
        var (page, builder) = Create();
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetDashPattern([1, -1]));
        Assert.Throws<ArgumentOutOfRangeException>(() => builder.SetDashPattern([double.NaN]));
        builder.BeginText().SetFont(PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 10);
        Assert.Throws<ArgumentException>(() => builder.ShowText(PdfTextSegment.FromText("ok"), PdfTextSegment.FromAdjustment(-100), PdfTextSegment.FromText("\u4E2D")));
        Assert.Throws<ArgumentException>(() => builder.ShowText("\u4E2D"));
        builder.ShowText(PdfTextSegment.FromText("A"), PdfTextSegment.FromAdjustment(-100), PdfTextSegment.FromText("B")).EndText();

        Assert.Equal("BT\n/F1 10 Tf\n[(A) -100 (B)] TJ\nET\n", Encoding.ASCII.GetString(page.GetContentData()));
    }

    [Fact]
    public void CompleteChecksThatTheContentIsBalanced()
    {
        var (_, builder) = Create();
        Assert.Equal(0, builder.MarkedContentDepth);
        Assert.False(builder.IsInTextObject);

        builder.SaveGraphicsState().BeginMarkedContent("Span").BeginText();
        Assert.True(builder.IsInTextObject);
        Assert.Equal(1, builder.MarkedContentDepth);
        Assert.Contains("text object", Assert.Throws<InvalidOperationException>(builder.Complete).Message, StringComparison.Ordinal);
        builder.EndText();
        Assert.Contains("graphics state", Assert.Throws<InvalidOperationException>(builder.Complete).Message, StringComparison.Ordinal);
        builder.RestoreGraphicsState();
        Assert.Contains("marked-content", Assert.Throws<InvalidOperationException>(builder.Complete).Message, StringComparison.Ordinal);
        builder.EndMarkedContent();
        builder.Complete();
        builder.Complete();

        // A completed builder does not write anymore, and the unmatched closes are refused
        Assert.Throws<InvalidOperationException>(() => builder.Rectangle(0, 0, 1, 1));
        Assert.Throws<InvalidOperationException>(() => builder.AppendRawContent("n"u8));
        var (_, other) = Create();
        Assert.Throws<InvalidOperationException>(() => other.RestoreGraphicsState());
        Assert.Throws<InvalidOperationException>(() => other.EndText());
        Assert.Throws<InvalidOperationException>(() => other.EndMarkedContent());
    }

    [Fact]
    public void BuildersOfAPageAreCheckedOneByOne()
    {
        var (page, first) = Create();
        first.SaveGraphicsState().Translate(10, 10);
        var second = page.CreateContentBuilder().Rectangle(0, 0, 1, 1).Fill();

        second.Complete();
        Assert.Throws<InvalidOperationException>(first.Complete);
        first.RestoreGraphicsState().Complete();
    }

    [Fact]
    public void ResourcesAreNamedOnceWithManyResources()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        var builder = page.CreateContentBuilder();
        var forms = Enumerable.Range(0, 300).Select(_ => document.CreateForm(new PdfRectangle(0, 0, 1, 1))).ToList();
        foreach (var form in forms)
        {
            builder.DrawForm(form);
        }

        foreach (var form in forms)
        {
            builder.DrawForm(form);
        }

        var xobjects = page.Resources!.Get<PdfDictionary>(PdfNames.XObject)!;
        Assert.HasCount(300, xobjects);
        Assert.Equal(forms[299].Reference, xobjects[PdfName.Create("Fm300")]);

        // A change of the dictionary outside the builder is seen: the name it took is not given again
        xobjects[PdfName.Create("Fm301")] = forms[0].Reference;
        xobjects.Remove(PdfName.Create("Fm1"));
        var extra = document.CreateForm(new PdfRectangle(0, 0, 1, 1));
        builder.DrawForm(extra).DrawForm(forms[0]);
        Assert.Equal(extra.Reference, xobjects[PdfName.Create("Fm1")]);
        Assert.HasCount(301, xobjects);
        Assert.EndsWith("/Fm1 Do\n/Fm301 Do\n", Encoding.ASCII.GetString(page.GetContentData()), StringComparison.Ordinal);
    }

    [Fact]
    public void Example_DrawingMethods()
    {
        using var directory = TemporaryDirectory.Create();
        DrawShapes(directory / "shapes.pdf");
        DrawLineStyles(directory / "lines.pdf");
        DrawTransformations(directory / "transformations.pdf");
        DrawGradients(directory / "gradients.pdf");
        DrawPatterns(directory / "patterns.pdf");
        DrawReusableContent(directory / "forms.pdf");
        DrawTransparency(directory / "transparency.pdf");

        Assert.Equal(new PdfRectangle(0, 0, 300, 150), PdfDocument.Open(directory / "shapes.pdf").Pages[0].MediaBox);
        Assert.Contains("[0 10] 0 d", Encoding.ASCII.GetString(PdfDocument.Open(directory / "lines.pdf").Pages[0].GetContentData()), StringComparison.Ordinal);
        Assert.Equal("PDF", PdfDocument.Open(directory / "patterns.pdf").Pages[0].ExtractText());

        // The form is written once, and painted five times
        var forms = PdfDocument.Open(directory / "forms.pdf");
        Assert.Single(forms.Pages[0].Resources!.Get<PdfDictionary>(PdfNames.XObject)!.Keys);
        Assert.Equal(5, Encoding.ASCII.GetString(forms.Pages[0].GetContentData()).Split(" Do").Length - 1);
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawShapes
    public static void DrawShapes(string outputPath)
    {
        var document = new PdfDocument();
        document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder()
            // Filled shapes: set the fill color, build the path, then fill it
            .SetFillRgb(0.27, 0.51, 0.71).Rectangle(15, 80, 55, 55).Fill()
            .SetFillColor(PdfColor.FromRgb24(46, 139, 87)).Circle(112, 107.5, 27.5).Fill()
            .SetFillCmyk(0, 0.2, 0.9, 0).Ellipse(185, 107.5, 35, 22).Fill()
            .SetFillRgb(0.86, 0.08, 0.24).MoveTo(235, 80).LineTo(285, 80).LineTo(260, 135).ClosePath().Fill()

            // Outlines: set the stroke color and the line width, build the path, then stroke it
            .SetStrokeGray(0.3).SetLineWidth(3)
            .Rectangle(15, 15, 55, 50).Stroke()
            .MoveTo(85, 20).CurveTo(100, 75, 125, -15, 140, 60).Stroke() // a cubic Bézier curve

            // Both: fill, then stroke the same path
            .SetFillRgb(1, 0.85, 0.4).Rectangle(160, 15, 50, 50).FillAndStroke()

            // Two squares, one inside the other: with the even-odd rule, the inner one is a hole
            .SetFillRgb(0.58, 0.44, 0.86).Rectangle(230, 15, 55, 50).Rectangle(245, 27.5, 25, 25).Fill(PdfFillRule.EvenOdd);

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawLineStyles
    public static void DrawLineStyles(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder().SetStrokeRgb(0.2, 0.3, 0.5);

        // Caps: the ends of the lines, butt (the default), round and projecting square
        builder.SetLineWidth(10)
            .SetLineCap(PdfLineCap.Butt).MoveTo(25, 125).LineTo(105, 125).Stroke()
            .SetLineCap(PdfLineCap.Round).MoveTo(25, 100).LineTo(105, 100).Stroke()
            .SetLineCap(PdfLineCap.ProjectingSquare).MoveTo(25, 75).LineTo(105, 75).Stroke();

        // Joins: the corners, miter (the default), round and bevel
        builder.SetLineCap(PdfLineCap.Butt)
            .SetLineJoin(PdfLineJoin.Miter).MoveTo(135, 70).LineTo(155, 125).LineTo(175, 70).Stroke()
            .SetLineJoin(PdfLineJoin.Round).MoveTo(190, 70).LineTo(210, 125).LineTo(230, 70).Stroke()
            .SetLineJoin(PdfLineJoin.Bevel).MoveTo(245, 70).LineTo(265, 125).LineTo(285, 70).Stroke();

        // Dashes: 12 points drawn, 6 skipped; then dashes of length 0 with round caps, which are dots
        builder.SetLineWidth(3).SetDashPattern([12, 6]).MoveTo(25, 40).LineTo(285, 40).Stroke()
            .SetLineWidth(5).SetLineCap(PdfLineCap.Round).SetDashPattern([0, 10]).MoveTo(25, 20).LineTo(285, 20).Stroke()
            .SetDashPattern([]); // solid lines again

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawTransformations
    public static void DrawTransformations(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder();

        // The same square, turned around the center of the left half: move the origin there, then rotate the axes
        for (var i = 0; i < 6; i++)
        {
            builder.SaveGraphicsState()
                .Translate(75, 75)
                .Rotate(i * 15) // degrees, counterclockwise
                .SetStrokeRgb(0.1, 0.25 + (i * 0.12), 0.6).SetLineWidth(2)
                .Rectangle(-40, -40, 80, 80).Stroke()
                .RestoreGraphicsState(); // back to the coordinates of the page
        }

        // Clipping: stripes painted through a circle
        builder.SaveGraphicsState()
            .Circle(225, 75, 55).Clip().EndPath()
            .SetFillRgb(0.95, 0.6, 0.1);
        for (var x = 165; x < 285; x += 20)
        {
            builder.Rectangle(x, 0, 10, 150);
        }

        builder.Fill()
            .RestoreGraphicsState() // removes the clip
            .SetStrokeGray(0.3).SetLineWidth(1.5).Circle(225, 75, 55).Stroke();

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawGradients
    public static void DrawGradients(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder();

        // An axial gradient: the colors change along a line, from x = 15 to x = 135, with three stops
        var sunset = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 15, 0, 135, 0, PdfFunction.Gradient(
            (0, PdfColor.FromRgb(0.98, 0.8, 0.2)),
            (0.5, PdfColor.FromRgb(0.95, 0.35, 0.3)),
            (1, PdfColor.FromRgb(0.35, 0.15, 0.5))));

        // A shading paints the whole clipping region: clip to the rectangle first
        builder.SaveGraphicsState()
            .Rectangle(15, 15, 120, 120).Clip().EndPath()
            .PaintShading(sunset)
            .RestoreGraphicsState();

        // A radial gradient: from a small circle of light to the edge of the sphere
        var sphere = PdfShading.Radial(PdfDeviceColorSpace.Rgb, 205, 95, 5, 225, 75, 60,
            PdfFunction.Gradient((0, PdfColor.FromRgb(1, 1, 1)), (1, PdfColor.FromRgb(0.1, 0.35, 0.75))));
        builder.SaveGraphicsState()
            .Circle(225, 75, 60).Clip().EndPath()
            .PaintShading(sphere)
            .RestoreGraphicsState();

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawPatterns
    public static void DrawPatterns(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder();

        // A colored tiling pattern: a cell of 20 by 20 points with its own colors, repeated every 20 points
        var checkerboard = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20);
        checkerboard.CreateContentBuilder()
            .SetFillRgb(0.85, 0.9, 1).Rectangle(0, 0, 20, 20).Fill()
            .SetFillRgb(0.3, 0.45, 0.75).Rectangle(0, 0, 10, 10).Rectangle(10, 10, 10, 10).Fill();
        builder.SetFillPattern(checkerboard).Rectangle(15, 15, 80, 120).Fill();

        // An uncolored tiling pattern: a shape without colors, painted with the color given when it is used.
        // The matrix of the pattern turns its stripes by 45 degrees.
        var stripes = document.CreateTilingPattern(new PdfRectangle(0, 0, 8, 8), 8, 8, isColored: false, matrix: PdfMatrix.CreateRotation(45));
        stripes.CreateContentBuilder().Rectangle(0, 0, 8, 3).Fill();
        builder.SetFillPattern(stripes, PdfColor.FromRgb(0.8, 0.2, 0.2)).Rectangle(110, 80, 80, 55).Fill()
            .SetFillPattern(stripes, PdfColor.FromRgb(0.2, 0.6, 0.3)).Rectangle(110, 15, 80, 55).Fill();

        // A shading pattern: a gradient used as a color, here to fill text
        var gradient = new PdfShadingPattern(PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 50, 0, 100,
            PdfFunction.Gradient((0, PdfColor.FromRgb(0.1, 0.3, 0.8)), (1, PdfColor.FromRgb(0.9, 0.2, 0.5)))));
        builder.SetFillPattern(gradient).DrawText("PDF", PdfFont.GetStandardFont(PdfStandardFont.HelveticaBold), 40, 205, 60);

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawReusableContent
    public static void DrawReusableContent(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder();

        // A form: content written once in the file, painted as many times as needed
        var star = document.CreateForm(new PdfRectangle(0, 0, 40, 40));
        star.CreateContentBuilder()
            .SetFillRgb(1, 0.75, 0.1).SetStrokeRgb(0.75, 0.45, 0).SetLineWidth(1.5).SetLineJoin(PdfLineJoin.Round)
            .MoveTo(20, 38).LineTo(25, 25).LineTo(38, 25).LineTo(27, 17).LineTo(31, 3)
            .LineTo(20, 11).LineTo(9, 3).LineTo(13, 17).LineTo(2, 25).LineTo(15, 25).ClosePath()
            .FillAndStroke();

        // Each copy is placed and sized by the transformation of the graphics state
        for (var i = 0; i < 5; i++)
        {
            var scale = 0.6 + (i * 0.2);
            builder.SaveGraphicsState()
                .Translate(15 + (i * 55), 20 + (i * 15))
                .Scale(scale, scale)
                .DrawForm(star)
                .RestoreGraphicsState();
        }

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawTransparency
    public static void DrawTransparency(string outputPath)
    {
        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 200)).CreateContentBuilder();

        // Constant alpha: three disks at 60% opacity
        builder.SaveGraphicsState()
            .SetGraphicsState(new PdfExtGState { FillAlpha = 0.6 })
            .SetFillRgb(0.9, 0.2, 0.2).Circle(55, 140, 35).Fill()
            .SetFillRgb(0.2, 0.7, 0.3).Circle(95, 140, 35).Fill()
            .SetFillRgb(0.2, 0.4, 0.9).Circle(75, 105, 35).Fill()
            .RestoreGraphicsState();

        // A blend mode: the same disks, opaque, multiplied with the colors under them
        builder.SaveGraphicsState()
            .SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Multiply })
            .SetFillRgb(1, 0.4, 0.4).Circle(205, 140, 35).Fill()
            .SetFillRgb(0.4, 1, 0.5).Circle(245, 140, 35).Fill()
            .SetFillRgb(0.4, 0.6, 1).Circle(225, 105, 35).Fill()
            .RestoreGraphicsState();

        // A soft mask: the luminosity of a gradient from white to black fades the band from opaque to transparent
        var fade = document.CreateForm(new PdfRectangle(0, 0, 300, 200), group: new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Gray });
        fade.CreateContentBuilder().PaintShading(PdfShading.Axial(PdfDeviceColorSpace.Gray, 15, 0, 285, 0,
            PdfFunction.Exponential(PdfColor.FromGray(1), PdfColor.FromGray(0))));
        builder.SaveGraphicsState()
            .SetGraphicsState(new PdfExtGState { SoftMask = PdfSoftMask.FromLuminosity(fade) })
            .SetFillRgb(0.1, 0.3, 0.6).Rectangle(15, 20, 270, 40).Fill()
            .RestoreGraphicsState();

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013

    [Fact]
    public void Example_DrawImage()
    {
        using var directory = TemporaryDirectory.Create();
        var imagePath = directory / "photo.png";
        using (var photo = new Image<Rgba32>(160, 120, new Rgba32(70, 130, 180)))
        {
            photo.Save(imagePath);
        }

        DrawImage(imagePath, directory / "image.pdf");

        var page = PdfDocument.Open(directory / "image.pdf").Pages[0];
        Assert.Single(page.Resources!.Get<PdfDictionary>(PdfNames.XObject)!.Keys);
    }

#pragma warning disable xUnit1013 // An example of the documentation, called by DocumentationImageTests
    #region Pdf_DrawImage
    public static void DrawImage(string imagePath, string outputPath)
    {
        // A JPEG file is embedded as it is; another format (PNG, WebP...) is decoded, with its transparency
        var image = PdfImage.Load(imagePath);

        var document = new PdfDocument();
        var builder = document.Pages.Add(new PdfSize(300, 150)).CreateContentBuilder()
            .SetFillRgb(0.9, 0.93, 0.97).Rectangle(0, 0, 300, 150).Fill();

        // The image is stretched to the rectangle it is drawn in: compute the width that keeps its aspect ratio
        var height = 120.0;
        var width = height * image.Width / image.Height;
        builder.DrawImage(image, 15, 15, width, height)
            .DrawImage(image, 30 + width, 15, width / 2, height / 2); // drawn again: the file stores it once

        document.Save(outputPath);
    }
    #endregion
#pragma warning restore xUnit1013
}
