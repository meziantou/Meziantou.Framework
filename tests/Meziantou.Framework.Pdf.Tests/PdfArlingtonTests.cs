using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.TestHarness.Arlington;

namespace Meziantou.Framework.Pdf.Tests;

/// <summary>Every object the library writes is checked against the vendored Arlington PDF Model.</summary>
public sealed class PdfArlingtonTests
{
    public static TheoryData<string> Documents => new(CreateDocuments().Keys);

    [Theory]
    [MemberData(nameof(Documents))]
    public void WrittenObjectsFollowTheModel(string name)
    {
        var document = CreateDocuments()[name]();

        foreach (var options in new[] { new PdfSaveOptions { UseObjectStreams = false }, new PdfSaveOptions { UseObjectStreams = true } })
        {
            var read = PdfDocument.Open(document.ToByteArray(options));
            var issues = ArlingtonValidator.Validate(read);

            Assert.Empty(issues.Select(i => i.ToString()));
        }
    }

    [Theory]
    [MemberData(nameof(PdfFixtureTests.Fixtures), MemberType = typeof(PdfFixtureTests))]
    public void ValidFixturesFollowTheModel(string path)
    {
        var fixture = TestHarness.Fixtures.PdfFixtureManifest.Load().Fixtures.Single(f => f.Path == path);
        if (fixture.Kind != "valid")
            return;

        var document = PdfDocument.Open(File.ReadAllBytes(TestHarness.Fixtures.PdfFixtureManifest.GetPath(fixture)));

        Assert.Empty(ArlingtonValidator.Validate(document).Where(i => i.Kind != ArlingtonIssueKind.UnknownKey).Select(i => i.ToString()));
    }

    [Fact]
    public void ValidatorReportsProblems()
    {
        var document = new PdfDocument();
        var page = document.Pages.Add(PdfSize.A4);
        page.Dictionary[PdfName.Create("Rotate")] = PdfName.Create("NotANumber");
        page.Dictionary[PdfName.Create("Unknown")] = PdfBoolean.True;
        document.Catalog[PdfName.Create("PageLayout")] = PdfName.Create("Diagonal");
        document.Catalog[PdfName.Create("Pages")] = document.Catalog.GetResolved(PdfName.Create("Pages"));

        var issues = ArlingtonValidator.Validate(document);

        Assert.Contains(issues, i => i.Kind == ArlingtonIssueKind.WrongType && i.Path.EndsWith("/Rotate", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == ArlingtonIssueKind.UnknownKey && i.Message.Contains("/Unknown", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == ArlingtonIssueKind.WrongValue && i.Path.EndsWith("/PageLayout", StringComparison.Ordinal));
        Assert.Contains(issues, i => i.Kind == ArlingtonIssueKind.WrongIndirection && i.Path.EndsWith("/Pages", StringComparison.Ordinal));
    }

    private static Dictionary<string, Func<PdfDocument>> CreateDocuments()
    {
        return new Dictionary<string, Func<PdfDocument>>(StringComparer.Ordinal)
        {
            ["empty"] = () => new PdfDocument(),
            ["rich"] = PdfWriterTests.CreateRichDocument,
            ["xmp"] = () =>
            {
                var document = PdfWriterTests.CreateRichDocument();
                document.UpdateXmpMetadata();
                return document;
            },
            ["images"] = () =>
            {
                using var image = Image.ImportPixelData<Rgba32>([new Rgba32(255, 0, 0, 128), new Rgba32(0, 255, 0, 255)], 2, 1);
                var document = new PdfDocument();
                document.Pages.Add(PdfSize.A5).CreateContentBuilder().DrawImage(PdfImage.FromImage(image), 0, 0, 10, 10);
                return document;
            },
            ["attachments"] = () =>
            {
                var document = new PdfDocument();
                var page = document.Pages.Add(PdfSize.A5);
                var file = document.EmbeddedFiles.Add("data.csv", "a,b"u8, new PdfEmbeddedFileOptions
                {
                    Description = "Data",
                    MimeType = "text/csv",
                    Relationship = PdfAssociatedFileRelationship.Data,
                    CreationDate = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
                    ModificationDate = new DateTimeOffset(2024, 5, 6, 7, 8, 9, TimeSpan.Zero),
                });
                page.Annotations.AddFileAttachment(PdfRectangle.FromSize(10, 10, 16, 20), file);
                page.Annotations.AddFileAttachment(PdfRectangle.FromSize(40, 10, 16, 20), "page.txt", "Page"u8, icon: PdfFileAttachmentIcon.Graph);
                return document;
            },
            ["fonts"] = () =>
            {
                var document = new PdfDocument();
                var builder = document.Pages.Add(PdfSize.A5).CreateContentBuilder();
                foreach (var font in Enum.GetValues<PdfStandardFont>())
                {
                    builder.DrawText(font == PdfStandardFont.Symbol ? "\u03B1" : font == PdfStandardFont.ZapfDingbats ? "\u2701" : "A", PdfFont.GetStandardFont(font), 10, 10, 10 + ((int)font * 12));
                }

                return document;
            },
        };
    }
}
