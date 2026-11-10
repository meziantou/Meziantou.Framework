using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
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
        { "pdfa-1b", "1b" },
        { "pdfa-1a", "1a" },
        { "pdfa-4e", "4e" },
        { "pdfa-4f", "4f" },
        { "pdfua-2", "ua2" },
        { "pdfa-4-pdfua-2", "4" },
        { "pdfa-4-pdfua-2", "ua2" },
        { "pdfa-2b-form", "2b" },
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

    public static TheoryData<string> ConvertedLevels => ["2b", "2u", "3b", "3u"];

    /// <summary>A document that is not PDF/A (standard fonts not embedded, JavaScript, an annotation without appearance, XMP metadata of another schema) converted by PdfDocument.ConvertToPdfA.</summary>
    [Theory]
    [MemberData(nameof(ConvertedLevels))]
    public async Task ConvertedDocumentsConformToTheirLevels(string flavour)
    {
        var verapdf = await VeraPdfTool.LocateAsync(XunitCancellationToken);
        var document = PdfDocument.Open(CreateDocumentToConvert());
        var level = flavour switch
        {
            "2b" => PdfAConformance.PdfA2B,
            "2u" => PdfAConformance.PdfA2U,
            "3b" => PdfAConformance.PdfA3B,
            _ => PdfAConformance.PdfA3U,
        };
        var result = document.ConvertToPdfA(level);
        Assert.True(result.Succeeded, string.Join('\n', result.Violations));
        var path = Path.Combine(AppContext.BaseDirectory, "InteropOutput", "converted-" + flavour + ".pdf");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        await File.WriteAllBytesAsync(path, document.ToByteArray(), XunitCancellationToken);

        var failures = await verapdf.ValidateAsync(path, flavour, XunitCancellationToken);

        Assert.Empty(failures);
    }

    private static byte[] CreateDocumentToConvert()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.CreateContentBuilder()
            .DrawText("Hello, archive", PdfFont.GetStandardFont(PdfStandardFont.Helvetica), 24, 72, 700)
            .DrawText("Bold and serif", PdfFont.GetStandardFont(PdfStandardFont.TimesBold), 18, 72, 660)
            .DrawText("Monospaced \u00e9\u00e8", PdfFont.GetStandardFont(PdfStandardFont.Courier), 12, 72, 630)
            .SetFillRgb(0, 0, 1).Rectangle(72, 500, 100, 50).Fill();
        page.Dictionary[PdfName.Create("AA")] = new PdfDictionary { [PdfName.Create("O")] = new PdfDictionary { [PdfName.Create("S")] = PdfName.Create("JavaScript"), [PdfName.Create("JS")] = PdfString.FromText("1") } };
        page.Annotations.AddNote(new PdfPoint(300, 300), "A note");
        page.Annotations.AddLink(new PdfRectangle(72, 690, 200, 720), new Uri("https://example.com"));
        var square = new PdfDictionary
        {
            [PdfName.Create("Type")] = PdfName.Create("Annot"),
            [PdfName.Create("Subtype")] = PdfName.Create("Square"),
            [PdfName.Create("Rect")] = PdfArray.FromNumbers(300, 400, 400, 450),
            [PdfName.Create("C")] = PdfArray.FromNumbers(1, 0, 0),
        };
        page.Dictionary.Get<PdfArray>(PdfName.Create("Annots"))!.Add(document.Objects.Add(square));
        document.Catalog[PdfName.Create("OpenAction")] = new PdfDictionary { [PdfName.Create("S")] = PdfName.Create("JavaScript"), [PdfName.Create("JS")] = PdfString.FromText("app.alert('hi')") };
        document.Information.Title = "To convert";
        document.SetXmpMetadata("<x:xmpmeta xmlns:x='adobe:ns:meta/'><rdf:RDF xmlns:rdf='http://www.w3.org/1999/02/22-rdf-syntax-ns#'><rdf:Description rdf:about='' xmlns:custom='http://example.com/custom/' custom:id='42'/></rdf:RDF></x:xmpmeta>"u8);
        return document.ToByteArray();
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
            "pdfa-1b" => (PdfAConformance.PdfA1B, PdfUAConformance.None),
            "pdfa-1a" => (PdfAConformance.PdfA1A, PdfUAConformance.None),
            "pdfa-4e" => (PdfAConformance.PdfA4E, PdfUAConformance.None),
            "pdfa-4f" => (PdfAConformance.PdfA4F, PdfUAConformance.None),
            "pdfua-2" => (PdfAConformance.None, PdfUAConformance.PdfUA2),
            "pdfa-4-pdfua-2" => (PdfAConformance.PdfA4, PdfUAConformance.PdfUA2),
            "pdfa-2b-form" => (PdfAConformance.PdfA2B, PdfUAConformance.None),
            _ => throw new ArgumentOutOfRangeException(nameof(name), name, "Unknown document"),
        };
        var tagged = pdfA is PdfAConformance.PdfA1A or PdfAConformance.PdfA2A or PdfAConformance.PdfA3A || pdfUA != PdfUAConformance.None;
        var isPdfA1 = pdfA is PdfAConformance.PdfA1B or PdfAConformance.PdfA1A;

        // Text of a TrueType and of a CFF font, an image (with an alpha channel, except for PDF/A-1, which has no
        // transparency), a gradient, a link and an outline
        var document = new PdfDocument { PdfAConformance = pdfA, PdfUAConformance = pdfUA, Language = "en-US" };
        document.Information.Title = "Conformance of " + name;
        document.Information.Author = "Meziantou.Framework.Pdf";
        var trueType = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf");
        var cff = PdfFont.Load(RepositoryPaths.Fonts / "noto-sans-cff" / "NotoSans-Regular.subset.otf");
        using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 255), new Rgba32(0, 255, 0, isPdfA1 ? (byte)255 : (byte)128), new Rgba32(0, 0, 255, isPdfA1 ? (byte)255 : (byte)0), new Rgba32(255, 255, 255, 255)], 2, 2);
        var gradient = PdfShading.Axial(PdfDeviceColorSpace.Rgb, 60, 400, 260, 400, PdfFunction.Exponential(PdfColor.FromRgb(1, 0, 0), PdfColor.FromRgb(0, 0, 1)));

        var page = document.Pages.Add(PdfSize.A4);
        var link = page.Annotations.AddLink(new PdfRectangle(60, 640, 260, 660), new Uri("https://www.meziantou.net/"));
        link.Contents = "Meziantou's blog";
        var builder = page.CreateContentBuilder();
        var root = tagged ? document.StructureTree.AddElement("Document") : null;
        PdfStructureElement? Tagged(string type, Action<PdfContentBuilder> draw, string? alternate = null)
        {
            if (root is null)
            {
                draw(builder);
                return null;
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

            return element;
        }

        var heading = Tagged("H1", b => b.DrawText("Office AVATAR", trueType, 24, 60, 760));
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

        // PDF/UA-2 needs structure destinations
        var destination = PdfDestination.Fit(page);
        document.Outlines.Add("Office", pdfUA == PdfUAConformance.PdfUA2 ? destination.WithStructureElement(heading!) : destination);

        // The embedded files: a PDF/A document for PDF/A-2 and PDF/A-4, any file for PDF/A-3 and PDF/A-4f
        if (pdfA is PdfAConformance.PdfA2B or PdfAConformance.PdfA4)
        {
            var archived = new PdfDocument { PdfAConformance = PdfAConformance.PdfA2B };
            archived.Information.Title = "Archived";
            archived.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawText("Archived", trueType, 12, 10, 10);
            document.EmbeddedFiles.Add("archived.pdf", archived.ToByteArray(), new PdfEmbeddedFileOptions { Description = "An archived document", MimeType = "application/pdf", Relationship = PdfAssociatedFileRelationship.Supplement });
        }
        else if (pdfA is PdfAConformance.PdfA3B or PdfAConformance.PdfA3U or PdfAConformance.PdfA3A or PdfAConformance.PdfA4F)
        {
            var data = document.EmbeddedFiles.Add("data.csv", "Product,Price\nBook,12\n"u8, new PdfEmbeddedFileOptions
            {
                Description = "The data of the document",
                MimeType = "text/csv",
                Relationship = PdfAssociatedFileRelationship.Data,
                ModificationDate = new DateTimeOffset(2026, 10, 24, 9, 30, 0, TimeSpan.Zero),
            });
            var attachment = page.Annotations.AddFileAttachment(PdfRectangle.FromSize(300, 640, 16, 20), data);
            if (root is not null)
            {
                var annotation = root.AddElement("Annot");
                annotation.AddAnnotation(attachment);
            }
        }

        // The fields of a form: their appearances use embedded fonts and paths only
        if (name.EndsWith("-form", StringComparison.Ordinal))
        {
            var form = document.Form;
            form.AddTextField(page, "name", new PdfRectangle(300, 760, 500, 780), new PdfTextFieldOptions { Font = trueType, Value = "Office AVATAR" });
            form.AddTextField(page, "comment", new PdfRectangle(300, 680, 500, 740), new PdfTextFieldOptions { Font = cff, IsMultiline = true, Value = "\u00E9t\u00E9 \u201Cfine\u201D" });
            form.AddCheckBox(page, "agree", new PdfRectangle(300, 640, 315, 655), new PdfCheckBoxOptions { IsChecked = true });
            form.AddRadioButtons("size", [new PdfRadioButton(page, new PdfRectangle(300, 610, 315, 625), "S"), new PdfRadioButton(page, new PdfRectangle(330, 610, 345, 625), "L")], new PdfRadioButtonOptions { Value = "L" });
            form.AddComboBox(page, "country", new PdfRectangle(300, 570, 450, 590), [new PdfChoiceOption("fr", "France"), new PdfChoiceOption("de", "Germany")], new PdfChoiceFieldOptions { Font = trueType, SelectedValues = ["fr"] });
            form.AddSignatureField(page, "signature", new PdfRectangle(300, 480, 500, 540));
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
