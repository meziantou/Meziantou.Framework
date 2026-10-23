using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.Fixtures;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>The documents the interop tests give to the external tools: documents the library writes, and the fixtures.</summary>
internal static class InteropDocuments
{
    public static readonly string[] WrittenNames = ["text", "graphics", "images", "navigation", "metadata"];

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
            default:
                throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown document");
        }

        return document;
    }
}
