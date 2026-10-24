using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.TestHarness;
using Meziantou.Framework.Pdf.TestHarness.ExternalTools;

namespace Meziantou.Framework.Pdf.InteropTests;

/// <summary>
/// veraPDF validates the documents the library writes for each PDF/A and PDF/UA level. The rules a document is known to
/// fail are listed in <c>verapdf-known-failures.txt</c>: a rule that fails or passes unexpectedly fails the test.
/// </summary>
public sealed class ConformanceInteropTests
{
    public static TheoryData<string, string> Documents => new()
    {
        { "pdfa-2b", "2b" },
        { "pdfa-2u", "2u" },
        { "pdfa-2a", "2a" },
        { "pdfa-3b", "3b" },
        { "pdfa-3u", "3u" },
        { "pdfa-3a", "3a" },
        { "pdfa-4", "4" },
        { "pdfua-1", "ua1" },
        { "pdfa-2a-pdfua-1", "2a" },
        { "pdfa-2a-pdfua-1", "ua1" },
    };

    [Theory]
    [MemberData(nameof(Documents))]
    public async Task DocumentsConformToTheirLevels(string name, string flavour)
    {
        var verapdf = await VeraPdfTool.LocateAsync(XunitCancellationToken);
        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "conformance-" + name + "-" + flavour + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, CreateDocument(name).ToByteArray(), XunitCancellationToken);

        var failures = await verapdf.ValidateAsync(path, flavour, XunitCancellationToken);

        var known = ReadKnownFailures().Where(entry => entry.Name == name && entry.Flavour == flavour).Select(entry => entry.Rule).Order(StringComparer.Ordinal).ToList();
        var actual = failures.Select(failure => failure[..failure.IndexOf(':', StringComparison.Ordinal)]).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        Assert.True(known.SequenceEqual(actual, StringComparer.Ordinal), $"veraPDF {VeraPdfTool.Version} {flavour}: the failed rules are not the known failures of '{name}' in verapdf-known-failures.txt\n" + string.Join('\n', failures));
    }

    private static PdfDocument CreateDocument(string name)
    {
        var (pdfA, pdfUA) = name switch
        {
            "pdfa-2b" => (PdfAConformance.PdfA2B, PdfUAConformance.None),
            "pdfa-2u" => (PdfAConformance.PdfA2U, PdfUAConformance.None),
            "pdfa-2a" => (PdfAConformance.PdfA2A, PdfUAConformance.None),
            "pdfa-3b" => (PdfAConformance.PdfA3B, PdfUAConformance.None),
            "pdfa-3u" => (PdfAConformance.PdfA3U, PdfUAConformance.None),
            "pdfa-3a" => (PdfAConformance.PdfA3A, PdfUAConformance.None),
            "pdfa-4" => (PdfAConformance.PdfA4, PdfUAConformance.None),
            "pdfua-1" => (PdfAConformance.None, PdfUAConformance.PdfUA1),
            "pdfa-2a-pdfua-1" => (PdfAConformance.PdfA2A, PdfUAConformance.PdfUA1),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown document"),
        };
        var tagged = pdfA is PdfAConformance.PdfA2A or PdfAConformance.PdfA3A || pdfUA != PdfUAConformance.None;

        // Text of a TrueType and of a CFF font, an image with an alpha channel, a gradient, a link and an outline
        var document = new PdfDocument { PdfAConformance = pdfA, PdfUAConformance = pdfUA, Language = "en-US" };
        document.Information.Title = "Conformance of " + name;
        document.Information.Author = "Meziantou.Framework.Pdf";
        var trueType = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var cff = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans-cff" / "NotoSans-Regular.subset.otf");
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, 128), new Rgba32(0, 0, 255, 0), new Rgba32(255, 255, 255, 255)], 2, 2);
        var gradient = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 60, 400, 260, 400, PdfFunction.Exponential(PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 0, 1)));

        var page = document.Pages.Add(PdfSize.A4);
        var link = page.Annotations.AddLink(new PdfRectangle(60, 640, 260, 660), new Uri("https://www.meziantou.net/"));
        link.Contents = "Meziantou's blog";
        document.Outlines.Add("Office", PdfDestination.Fit(page));
        var builder = page.CreateContentBuilder();
        var root = tagged ? document.StructureTree.AddElement("Document") : null;
        void Tagged(string type, Action<PdfContentBuilder> draw, string? alternate = null)
        {
            if (root is null)
            {
                draw(builder);
                return;
            }

            var element = root.AddElement(type);
            element.AlternateDescription = alternate;
            builder.BeginStructureElement(element);
            draw(builder);
            builder.EndMarkedContent();
            if (type == "Link")
            {
                element.AddAnnotation(link);
            }
        }

        Tagged("H1", b => b.DrawText("Office AVATAR", trueType, 24, 60, 760));
        Tagged("P", b => b.DrawText("\u00E9t\u00E9 \u201Cfine\u201D \u2014 1\u20AC", cff, 14, 60, 720));
        Tagged("Link", b => b.DrawText("AVATAR", trueType, 12, 60, 645));
        Tagged("Figure", b => b.DrawImage(PdfImage.FromImage(image), 60, 480, 100, 100), "A square of four colors");
        Tagged("Figure", b => b.SaveGraphicsState().Rectangle(60, 380, 200, 40).Clip().EndPath().PaintShading(gradient).RestoreGraphicsState(), "A gradient from red to blue");
        if (root is null)
        {
            builder.DrawText("1", trueType, 10, 300, 40);
        }
        else
        {
            builder.BeginArtifact(PdfArtifactType.Pagination).DrawText("1", trueType, 10, 300, 40).EndMarkedContent();
        }

        return document;
    }

    private static List<(string Name, string Flavour, string Rule)> ReadKnownFailures()
    {
        var result = new List<(string, string, string)>();
        foreach (var raw in File.ReadAllLines(RepositoryPaths.Root / "tests" / "Meziantou.Framework.Pdf.InteropTests" / "verapdf-known-failures.txt"))
        {
            var line = raw.Split('#')[0].Trim();
            if (line.Length == 0)
                continue;

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            result.Add((parts[0], parts[1], parts[2]));
        }

        return result;
    }
}
