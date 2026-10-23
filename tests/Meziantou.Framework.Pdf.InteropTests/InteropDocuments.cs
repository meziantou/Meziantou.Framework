using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>The documents the interop tests give to the external tools: documents the library writes, and the fixtures.</summary>
internal static class InteropDocuments
{
    public static readonly string[] WrittenNames = ["text", "graphics", "images", "navigation", "metadata", "fonts", "advanced-graphics"];

    /// <summary>The embedded fonts of the "fonts" document, and the text of its page for each one.</summary>
    public static readonly (string Font, string Text)[] EmbeddedFonts =
    [
        ("noto-sans/NotoSans-Regular.subset.ttf", "Office AVATAR \u00E9t\u00E9 \u201Cfine\u201D \u2014 1\u20AC"),
        ("noto-sans-cff/NotoSans-Regular.subset.otf", "Office AVATAR \u00E9t\u00E9 \u201Cfine\u201D \u2014 1\u20AC"),
        ("noto-sans-cjk/NotoSansCJKjp-Regular.subset.otf", "\u65E5\u672C\u8A9E\u306E\u30C6\u30AD\u30B9\u30C8\u3002"),
        ("noto-naskh-arabic/NotoNaskhArabic-Regular.subset.ttf", "\u0645\u0631\u062D\u0628\u0627 123"),
        ("noto-sans-devanagari/NotoSansDevanagari-Regular.ttf", "\u0928\u092E\u0938\u094D\u0924\u0947 \u0915\u093F\u0924\u093E\u092C"),
    ];

    public static TheoryData<string, bool> Written
    {
        get
        {
            var data = new TheoryData<string, bool>();
            foreach (var name in WrittenNames)
            {
                data.Add(name, false);
                data.Add(name, true);
            }

            return data;
        }
    }

    public static TheoryData<string> ValidFixtures => new(PdfFixtureManifest.Load().Fixtures.Where(f => f.Kind == "valid").Select(f => f.Path));

    /// <summary>Writes a document to the artifacts directory of a test and returns its path.</summary>
    public static string Write(string name, bool objectStreams)
    {
        var document = Create(name);
        var directory = Path.Combine(AppContext.BaseDirectory, "InteropOutput");
        Directory.CreateDirectory(directory);
        // Each call writes its own file: the tests of several tools write the same documents in parallel
        var path = Path.Combine(directory, $"{name}{(objectStreams ? ".objstm" : "")}-{Guid.NewGuid():N}.pdf");
        document.Save(path, new PdfSaveOptions { UseObjectStreams = objectStreams });
        return path;
    }

    public static string GetFixturePath(string path) => RepositoryPaths.Fixtures / path;

    /// <summary>Gets the text the library wrote on each page of a document.</summary>
    public static string[] GetExpectedText(string name)
    {
        return name switch
        {
            "text" => ["Hello, World\nCaf\u00E9 \u2013 \u20AC10 \u201Cquoted\u201D\nCourier text", "\u03B1\u03B2\u03B3 \u2248 \u03C0"],
            "fonts" => [.. EmbeddedFonts.Select(font => font.Text)],
            _ => [],
        };
    }

    public static PdfDocument Create(string name)
    {
        var document = new PdfDocument();
        switch (name)
        {
            case "text":
                {
                    var page = document.Pages.Add(PdfSize.A4);
                    page.CreateContentBuilder()
                        .DrawText("Hello, World", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 72, 760)
                        .DrawText("Caf\u00E9 \u2013 \u20AC10 \u201Cquoted\u201D", PdfFont.GetStandardFont(PdfStandardFont.TimesRoman), 14, 72, 720)
                        .DrawText("Courier text", PdfFont.GetStandardFont(PdfStandardFont.Courier), 12, 72, 690);
                    document.Pages.Add(PdfSize.A4).CreateContentBuilder()
                        .DrawText("\u03B1\u03B2\u03B3 \u2248 \u03C0", PdfFont.GetStandardFont(PdfStandardFont.Symbol), 14, 72, 760);
                    break;
                }

            case "graphics":
                document.Pages.Add(PdfSize.Letter).CreateContentBuilder()
                    .SaveGraphicsState()
                    .SetLineWidth(3).SetLineCap(PdfLineCap.Round).SetLineJoin(PdfLineJoin.Round).SetDashPattern([6, 3])
                    .SetStrokeRgb(0.8, 0.1, 0.1)
                    .MoveTo(50, 50).CurveTo(100, 200, 200, 0, 300, 150).Stroke()
                    .RestoreGraphicsState()
                    .SetFillCmyk(0.1, 0.2, 0.3, 0)
                    .Circle(300, 400, 80).Fill(PdfFillRule.EvenOdd)
                    .SaveGraphicsState()
                    .Rectangle(100, 500, 200, 100).Clip().EndPath()
                    .Translate(150, 550).Rotate(30).Scale(2, 1)
                    .SetFillGray(0.5).Rectangle(-20, -10, 40, 20).Fill()
                    .RestoreGraphicsState()
                    .BeginMarkedContent("Artifact").SetStrokeGray(0).Rectangle(10, 10, 592, 772).Stroke().EndMarkedContent();
                break;
            case "images":
                {
                    using var rgba = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 128), new Rgba32(0, 0, 255, 0), new Rgba32(255, 255, 255, 255)], 2, 2);
                    using var gray = Image.ImportPixelData<Gray16>([new Gray16(0), new Gray16(30000), new Gray16(65535)], 3, 1);
                    using var rgb = Image.ImportPixelData<Rgb24>(Enumerable.Range(0, 64).Select(i => new Rgb24((byte)(i * 4), 128, (byte)(255 - (i * 4)))).ToArray(), 8, 8);
                    using var jpeg = new MemoryStream();
                    rgb.Save(jpeg, new Imaging.Formats.JpegEncoder());
                    document.Pages.Add(PdfSize.A5).CreateContentBuilder()
                        .DrawImage(PdfImage.FromImage(rgba), 10, 10, 100, 100)
                        .DrawImage(PdfImage.FromImage(gray), 120, 10, 90, 30)
                        .DrawImage(PdfImage.Load(jpeg.ToArray()), 10, 120, 80, 80);
                    break;
                }

            case "navigation":
                {
                    var first = document.Pages.Add(PdfSize.A4);
                    var second = document.Pages.Add(PdfSize.A4);
                    var chapter = document.Outlines.Add("Chapter", PdfDestination.Fit(first));
                    chapter.Children.Add("Section", PdfDestination.Xyz(second, 0, 500, 1.5));
                    chapter.IsOpen = true;
                    first.Annotations.AddLink(new PdfRectangle(10, 10, 100, 30), new Uri("https://www.meziantou.net/"));
                    first.Annotations.AddLink(new PdfRectangle(10, 40, 100, 60), PdfDestination.FitRectangle(second, new PdfRectangle(0, 0, 100, 100)));
                    break;
                }

            case "metadata":
                document.Pages.Add(PdfSize.A4);
                document.Information.Title = "Title \u00E9\u00E8 \u4E2D\u6587";
                document.Information.Author = "Author";
                document.Information.Subject = "Subject";
                document.Information.Keywords = "a, b";
                document.Information.Creator = "Creator";
                document.Information.Producer = "Meziantou.Framework.Pdf";
                document.Information.CreationDate = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.FromHours(2));
                document.Information.ModificationDate = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero);
                document.UpdateXmpMetadata();
                break;
            case "advanced-graphics":
                CreateAdvancedGraphics(document);
                break;
            case "fonts":
                foreach (var (font, text) in EmbeddedFonts)
                {
                    document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText(text, PdfFont.Load(RepositoryPaths.Fonts / font), 24, 30, 500);
                }

                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown document");
        }

        return document;
    }

    /// <summary>The pages of the "advanced-graphics" document, 200 x 200 points each, and the colors expected at points of each page.</summary>
    public static readonly (string Name, (double X, double Y, byte R, byte G, byte B, int Tolerance)[] Samples)[] AdvancedGraphicsPages =
    [
        ("axial", [(10, 100, 242, 0, 13, 30), (190, 100, 13, 0, 242, 30), (100, 100, 128, 0, 128, 30)]),
        ("radial", [(100, 100, 255, 255, 255, 30), (100, 195, 13, 13, 13, 30)]),
        ("alpha", [(100, 100, 128, 128, 128, 12), (10, 10, 255, 255, 255, 2)]),
        ("multiply", [(100, 100, 0, 255, 0, 12), (190, 190, 255, 255, 0, 2)]),
        ("colored-tiling", [(5, 5, 255, 0, 0, 2), (15, 5, 255, 255, 255, 2), (25, 25, 255, 0, 0, 2)]),
        ("uncolored-tiling", [(5, 5, 0, 0, 255, 2), (15, 15, 255, 255, 255, 2)]),
        ("shading-pattern", [(10, 100, 0, 0, 0, 30), (190, 100, 255, 255, 255, 30)]),
        ("soft-mask", [(50, 100, 255, 0, 0, 12), (150, 100, 255, 255, 255, 12)]),
        ("forms", [(25, 25, 0, 255, 0, 2), (125, 125, 0, 255, 0, 2), (75, 75, 255, 255, 255, 2)]),
        ("group", [(60, 100, 128, 128, 128, 12), (100, 100, 128, 128, 128, 12), (190, 190, 255, 255, 255, 2)]),
        ("meshes", [(20, 20, 255, 0, 0, 12), (150, 50, 0, 255, 0, 12), (50, 150, 0, 0, 255, 12), (150, 150, 255, 255, 0, 12)]),
        ("function-based", [(190, 10, 242, 0, 13, 30), (10, 190, 13, 0, 242, 30)]),
        ("sampled", [(10, 100, 242, 13, 0, 30), (190, 100, 13, 242, 0, 30)]),
    ];

    private static void CreateAdvancedGraphics(PdfDocument document)
    {
        var size = new PdfSize(200, 200);
        var red = PdfColor.FromRgb(1, 0, 0);
        var green = PdfColor.FromRgb(0, 1, 0);
        var blue = PdfColor.FromRgb(0, 0, 1);
        var yellow = PdfColor.FromRgb(1, 1, 0);
        foreach (var (name, _) in AdvancedGraphicsPages)
        {
            var builder = document.Pages.Add(size).CreateContentBuilder();
            switch (name)
            {
                case "axial":
                    builder.PaintShading(PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 0, 200, 0, PdfFunction.Exponential(red, blue), extendStart: true, extendEnd: true));
                    break;
                case "radial":
                    builder.SetFillGray(0).Rectangle(0, 0, 200, 200).Fill()
                        .PaintShading(PdfShading.Radial(PdfDeviceColorSpace.Gray, 100, 100, 0, 100, 100, 100, PdfFunction.Exponential(PdfColor.White, PdfColor.Black)));
                    break;
                case "alpha":
                    builder.SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 }).SetFillGray(0).Rectangle(50, 50, 100, 100).Fill();
                    break;
                case "multiply":
                    builder.SetFillRgb(1, 1, 0).Rectangle(0, 0, 200, 200).Fill()
                        .SetGraphicsState(new PdfExtGState { BlendMode = PdfBlendMode.Multiply })
                        .SetFillRgb(0, 1, 1).Rectangle(50, 50, 100, 100).Fill();
                    break;
                case "colored-tiling":
                    {
                        var pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20);
                        pattern.CreateContentBuilder().SetFillRgb(1, 0, 0).Rectangle(0, 0, 10, 10).Rectangle(10, 10, 10, 10).Fill();
                        builder.SetFillPattern(pattern).Rectangle(0, 0, 200, 200).Fill();
                        break;
                    }

                case "uncolored-tiling":
                    {
                        var pattern = document.CreateTilingPattern(new PdfRectangle(0, 0, 20, 20), 20, 20, isColored: false);
                        pattern.CreateContentBuilder().Rectangle(0, 0, 10, 10).Fill();
                        builder.SetFillPattern(pattern, blue).Rectangle(0, 0, 200, 200).Fill();
                        break;
                    }

                case "shading-pattern":
                    builder.SetFillPattern(new PdfShadingPattern(PdfShading.Axial(PdfDeviceColorSpace.Gray, 0, 0, 1, 0, PdfFunction.Exponential(PdfColor.Black, PdfColor.White)), new PdfMatrix(200, 0, 0, 200, 0, 0)))
                        .Rectangle(0, 0, 200, 200).Fill();
                    break;
                case "soft-mask":
                    {
                        var mask = document.CreateForm(new PdfRectangle(0, 0, 200, 200), group: new PdfTransparencyGroup { ColorSpace = PdfDeviceColorSpace.Gray });
                        mask.CreateContentBuilder().SetFillGray(1).Rectangle(0, 0, 100, 200).Fill();
                        builder.SetGraphicsState(new PdfExtGState { SoftMask = PdfSoftMask.FromLuminosity(mask) })
                            .SetFillRgb(1, 0, 0).Rectangle(0, 0, 200, 200).Fill();
                        break;
                    }

                case "forms":
                    {
                        var form = document.CreateForm(new PdfRectangle(0, 0, 50, 50));
                        form.CreateContentBuilder().SetFillRgb(0, 1, 0).Rectangle(0, 0, 50, 50).Fill();
                        builder.DrawForm(form).SaveGraphicsState().Translate(100, 100).DrawForm(form).RestoreGraphicsState();
                        break;
                    }

                case "group":
                    {
                        // Two overlapping black squares in an isolated group painted at half opacity: the overlap is not darker
                        var group = document.CreateForm(new PdfRectangle(0, 0, 200, 200), group: new PdfTransparencyGroup { Isolated = true });
                        group.CreateContentBuilder().SetFillGray(0).Rectangle(40, 40, 80, 120).Fill().Rectangle(80, 40, 80, 120).Fill();
                        builder.SetGraphicsState(new PdfExtGState { FillAlpha = 0.5 }).DrawForm(group);
                        break;
                    }

                case "meshes":
                    {
                        PdfPoint[] boundary = [new(100, 0), new(100, 33), new(100, 67), new(100, 100), new(133, 100), new(167, 100), new(200, 100), new(200, 67), new(200, 33), new(200, 0), new(167, 0), new(133, 0)];
                        PdfPoint[] tensor = [.. boundary.Select(point => new PdfPoint(point.X, point.Y + 100))];
                        builder
                            .PaintShading(PdfShading.TriangleMesh(PdfDeviceColorSpace.Rgb, [(new PdfMeshVertex(0, 0, red), new PdfMeshVertex(100, 0, red), new PdfMeshVertex(0, 100, red)), (new PdfMeshVertex(100, 0, red), new PdfMeshVertex(100, 100, red), new PdfMeshVertex(0, 100, red))]))
                            .PaintShading(PdfShading.CoonsPatchMesh(PdfDeviceColorSpace.Rgb, [new PdfPatch(boundary, [green, green, green, green])]))
                            .PaintShading(PdfShading.LatticeMesh(PdfDeviceColorSpace.Rgb, 2, [new(0, 100, blue), new(100, 100, blue), new(0, 200, blue), new(100, 200, blue)]))
                            .PaintShading(PdfShading.TensorPatchMesh(PdfDeviceColorSpace.Rgb, [new PdfPatch(tensor, [yellow, yellow, yellow, yellow])]));
                        break;
                    }

                case "function-based":
                    // r = x, g = 0, b = y over the unit square, scaled to the page
                    builder.PaintShading(PdfShading.FunctionBased(PdfDeviceColorSpace.Rgb, PdfFunction.PostScript("{ exch 0 3 -1 roll }", [0, 1, 0, 1], [0, 1, 0, 1, 0, 1]), matrix: new PdfMatrix(200, 0, 0, 200, 0, 0)));
                    break;
                case "sampled":
                    builder.PaintShading(PdfShading.Axial(PdfDeviceColorSpace.Rgb, 0, 0, 200, 0, PdfFunction.Sampled([2], 8, [0, 1], [0, 1, 0, 1, 0, 1], [255, 0, 0, 0, 255, 0]), extendStart: true, extendEnd: true));
                    break;
                default:
                    throw new InvalidOperationException("Unknown page " + name);
            }
        }
    }
}
