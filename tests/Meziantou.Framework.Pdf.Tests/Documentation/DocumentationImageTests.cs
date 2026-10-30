using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Pdf.Rendering;
using Meziantou.Framework.Pdf.TestHarness;
using Xunit.Sdk;

namespace Meziantou.Framework.Pdf.Tests.Documentation;

/// <summary>
/// Renders the images of the documentation site (docs/Pdf/*/images): each test runs an example that creates a
/// document, renders its first page with <see cref="PdfRenderer"/>, and compares the pixels with the committed PNG. With
/// the environment variable <see cref="UpdateVariable"/> set to 1, a missing or different image is written instead
/// (see docs/AGENTS.md).
/// </summary>
public sealed class DocumentationImageTests : IDisposable
{
    private const string UpdateVariable = "MEZIANTOU_PDF_UPDATE_DOCS_IMAGES";
    private const string CreatingDocuments = "03-CreatingDocuments";
    private const string ReadingAndModifying = "05-ReadingAndModifying";
    private const string Rendering = "06-Rendering";

    private static readonly FullPath DocumentationRoot = RepositoryPaths.Root / "docs" / "Pdf";
    private static readonly FullPath RegularFont = RepositoryPaths.Fonts / "noto-sans" / "NotoSans-Regular.subset.ttf";

    // The bundled fonts replace the standard fonts: the pixels do not depend on the fonts of the machine
    private static readonly PdfRenderOptions RenderOptions = new() { Dpi = 96, UseSystemFonts = false };

    private readonly TemporaryDirectory _directory = TemporaryDirectory.Create();

    public void Dispose() => _directory.Dispose();

    private FullPath GetPath(string name) => _directory / name;

    [Fact]
    public void CreateCard() => AssertPage(CreatingDocuments, nameof(PdfDocumentTests.CreateCard), PdfDocumentTests.CreateCard);

    [Fact]
    public void DrawShapes() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawShapes), PdfContentBuilderTests.DrawShapes);

    [Fact]
    public void DrawLineStyles() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawLineStyles), PdfContentBuilderTests.DrawLineStyles);

    [Fact]
    public void DrawTransformations() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawTransformations), PdfContentBuilderTests.DrawTransformations);

    [Fact]
    public void DrawReceipt() => AssertPage(CreatingDocuments, nameof(PdfFontTests.DrawReceipt), PdfFontTests.DrawReceipt);

    [Fact]
    public void DrawTextStyles() => AssertPage(CreatingDocuments, nameof(PdfFontTests.DrawTextStyles), PdfFontTests.DrawTextStyles);

    [Fact]
    public void DrawParagraph() => AssertPage(CreatingDocuments, nameof(PdfFontTests.DrawParagraph), PdfFontTests.DrawParagraph);

    [Fact]
    public void DrawEmbeddedText() => AssertPage(CreatingDocuments, nameof(PdfFontTests.DrawEmbeddedText), output => PdfFontTests.DrawEmbeddedText(RegularFont, output));

    [Fact]
    public void DrawImage()
    {
        // A picture with transparent corners: a sky, a sun and a hill in a rounded rectangle
        var photo = GetPath("photo.png");
        using (var image = new Image<Rgba32>(320, 240))
        {
            var sky = new LinearGradientBrush(new PointD(0, 0), new PointD(0, 240), [new GradientStop(0, new Rgba32(30, 60, 160)), new GradientStop(1, new Rgba32(200, 220, 255))]);
            image.Draw(canvas =>
            {
                canvas.Clip(new VectorPathBuilder().AddRoundedRectangle(new RectangleD(0, 0, 320, 240), 48).Build(), clipped =>
                {
                    clipped.Fill(sky);
                    clipped.FillCircle(new Rgba32(255, 220, 90), new PointD(240, 70), 36);
                    clipped.FillEllipse(new Rgba32(60, 140, 60), new PointD(120, 260), 220, 90);
                });
            });
            image.Save(photo);
        }

        AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawImage), output => PdfContentBuilderTests.DrawImage(photo, output));
    }

    [Fact]
    public void DrawGradients() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawGradients), PdfContentBuilderTests.DrawGradients);

    [Fact]
    public void DrawPatterns() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawPatterns), PdfContentBuilderTests.DrawPatterns);

    [Fact]
    public void DrawReusableContent() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawReusableContent), PdfContentBuilderTests.DrawReusableContent);

    [Fact]
    public void DrawTransparency() => AssertPage(CreatingDocuments, nameof(PdfContentBuilderTests.DrawTransparency), PdfContentBuilderTests.DrawTransparency);

    [Fact]
    public void StampPages()
    {
        var card = GetPath("card.pdf");
        PdfDocumentTests.CreateCard(card);
        AssertPage(ReadingAndModifying, nameof(PdfDocumentTests.StampPages), output => PdfDocumentTests.StampPages(card, output));
    }

    [Fact]
    public void RenderThumbnails()
    {
        var card = GetPath("card.pdf");
        PdfDocumentTests.CreateCard(card);
        var directory = GetPath("thumbnails");
        Directory.CreateDirectory(directory);

        // The example writes the image itself: compare it as it is
        AssertImage(Rendering, nameof(PdfRendererTests.RenderThumbnails), output =>
        {
            PdfRendererTests.RenderThumbnails(card, directory);
            File.Copy(directory / "page-1.png", output);
        });
    }

    /// <summary>Runs an example that writes a document, and compares its first page, rendered, with an image of the documentation.</summary>
    private void AssertPage(string group, string name, Action<string> create)
    {
        var pdfPath = GetPath(name + ".pdf");
        create(pdfPath);
        AssertImage(group, name, output =>
        {
            using var image = PdfDocument.Open(pdfPath).Pages[0].Render(RenderOptions);
            image.Save(output);
        });
    }

    /// <summary>Runs an example that writes an image, and compares it with docs/Pdf/<paramref name="group"/>/images/<paramref name="name"/>.png.</summary>
    private void AssertImage(string group, string name, Action<string> draw)
    {
        var actualPath = GetPath(name + ".png");
        draw(actualPath);

        var expectedPath = DocumentationRoot / group / "images" / (name + ".png");
        var difference = File.Exists(expectedPath) ? Compare(expectedPath, actualPath) : "the image does not exist";
        if (difference is null)
            return;

        var relativePath = expectedPath.MakePathRelativeTo(RepositoryPaths.Root).Replace('\\', '/');
        if (Environment.GetEnvironmentVariable(UpdateVariable) is not "1")
            throw new XunitException($"{relativePath}: {difference}. Run the test with the environment variable {UpdateVariable}=1 to write the image, look at it, and commit it.");

        // The test runs for each target framework at the same time: publish the file with a rename
        Directory.CreateDirectory(expectedPath.Parent);
        var temporaryPath = expectedPath.Parent / (name + "." + Guid.NewGuid().ToString("N") + ".tmp");
        using (var image = Image.Load(actualPath))
        using (var stream = File.Create(temporaryPath))
        {
            image.Save(stream, new PngEncoder { Optimization = PngOptimization.MinimumSize });
        }

        File.Move(temporaryPath, expectedPath, overwrite: true);
    }

    /// <summary>Compares the size and the pixels, at 16 bits per sample.</summary>
    private static string? Compare(string expectedPath, string actualPath)
    {
        using var expected = Image.Load<Rgba64>(expectedPath);
        using var actual = Image.Load<Rgba64>(actualPath);
        if (expected.Size != actual.Size)
            return $"the size changed from {expected.Size} to {actual.Size}";

        var expectedBytes = new byte[expected.Width * expected.Height * 8];
        var actualBytes = new byte[expectedBytes.Length];
        expected.Frames[0].CopyPixelBytesTo(expectedBytes);
        actual.Frames[0].CopyPixelBytesTo(actualBytes);
        if (!expectedBytes.AsSpan().SequenceEqual(actualBytes))
            return "the pixels changed";

        return null;
    }
}
