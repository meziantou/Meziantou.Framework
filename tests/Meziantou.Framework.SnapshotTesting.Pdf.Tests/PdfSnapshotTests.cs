using System.Text;
using System.Text.Json;
using Meziantou.Framework.Imaging;
using Meziantou.Framework.Pdf;
using Meziantou.Framework.SnapshotTesting.Imaging;

namespace Meziantou.Framework.SnapshotTesting.Pdf.Tests;

public sealed class PdfSnapshotTests
{
    // The verified files are __snapshots__/PdfSnapshotTests_ReadmeExample_ValidateBytes_*.verified.*
    [Fact]
    public void ReadmeExample_ValidateBytes()
    {
        #region SnapshotTestingPdf_ValidateBytes
        var document = new PdfDocument();
        document.Information.Title = "Invoice";
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        document.Pages.Add(PdfSize.A5).CreateContentBuilder()
            .SetFillRgb(0.2, 0.4, 0.8)
            .Rectangle(40, 520, 200, 40)
            .Fill()
            .SetFillGray(0)
            .DrawText("Hello, PDF!", font, 24, 50, 480);
        var bytes = document.ToByteArray();

        // Writes PdfSnapshotTests_ReadmeExample_ValidateBytes_0.verified.json (the metadata)
        // and PdfSnapshotTests_ReadmeExample_ValidateBytes_1.verified.png (the first page)
        Snapshot.Validate(bytes, SnapshotType.Pdf);
        #endregion
    }

    // The verified files are __snapshots__/PdfSnapshotTests_ReadmeExample_Settings_*.verified.*
    [Fact]
    public void ReadmeExample_Settings()
    {
        var document = CreateDocument();
        document.Information.CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero);
        document.Outlines.Add("Introduction", PdfDestination.Fit(document.Pages[0])).Children.Add(new PdfOutlineItem("Details"));
        document.Pages[1].Annotations.AddLink(new PdfRectangle(20, 20, 120, 40), new Uri("https://www.meziantou.net"));

        #region SnapshotTestingPdf_Settings
        var settings = SnapshotSettings.Default with { };
        settings.AddPdf(new PdfSnapshotSettings
        {
            Dpi = 36,                              // Half the size of the default 72 dots per inch
            Metadata = PdfSnapshotMetadata.All,    // Information, dates, text, outlines and annotations
        });

        Snapshot.Validate(document, SnapshotType.Pdf, settings);
        #endregion
    }

    [Fact]
    public void ReadmeExample_ImageComparisonSettings()
    {
        #region SnapshotTestingPdf_ImageComparisonSettings
        var settings = new SnapshotSettings(); // SnapshotSettings.Default in the module initializer
        settings.AddPdf(imageComparisonSettings: new ImageComparisonSettings
        {
            Ssimulacra2Threshold = 90, // SSIMULACRA 2: 100 = identical, about 90 = not visible
        });
        #endregion

        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
    }

    [Fact]
    public void Serialize_PdfDocument_WritesMetadataThenOnePngPerPage()
    {
        var result = Serialize(CreateDocument());

        Assert.Equal(["json", "png", "png"], result.Data.Select(data => data.Extension));
        using var json = ParseJson(result);
        Assert.Equal("1.7", json.RootElement.GetProperty("version").GetString());
        Assert.Equal(2, json.RootElement.GetProperty("pageCount").GetInt32());
        Assert.Equal("Sample", json.RootElement.GetProperty("information").GetProperty("title").GetString());
        Assert.False(json.RootElement.GetProperty("information").GetProperty("isEncrypted").GetBoolean());

        var pages = json.RootElement.GetProperty("pages");
        Assert.Equal(2, pages.GetArrayLength());
        Assert.Equal(200, pages[0].GetProperty("width").GetDouble());
        Assert.Equal(100, pages[0].GetProperty("height").GetDouble());
        Assert.Equal(0, pages[0].GetProperty("rotation").GetInt32());
        Assert.False(pages[0].TryGetProperty("text", out _));
        Assert.False(pages[0].TryGetProperty("annotations", out _));
        Assert.False(json.RootElement.TryGetProperty("creationDate", out _));
        Assert.False(json.RootElement.TryGetProperty("outlines", out _));
    }

    [Fact]
    public void Serialize_RendersPagesAtTheResolutionOfTheSettings()
    {
        var result = Serialize(CreateDocument(), new PdfSnapshotSettings { Dpi = 144 });

        using var page = Image.Load(result.Data[1].Data);
        Assert.Equal(400, page.Width);
        Assert.Equal(200, page.Height);
        Assert.Equal(new Rgba32(255, 0, 0), page.CloneAs<Rgba32>().Frames[0][20, 20]);
    }

    [Fact]
    public void Serialize_ByteArrayAndStreamGiveTheSameSnapshotsAsTheDocument()
    {
        var document = CreateDocument();
        var bytes = document.ToByteArray();

        var fromBytes = Serialize(bytes);
        using var stream = new MemoryStream(bytes);
        var fromStream = Serialize(stream);

        Assert.Equal(3, fromBytes.Data.Count);
        Assert.Equal(fromBytes.Data.Select(data => data.Data), fromStream.Data.Select(data => data.Data));
        Assert.Equal(Serialize(document).Data[1].Data, fromBytes.Data[1].Data);
    }

    [Fact]
    public void Serialize_DataThatIsNotAPdfDocument_IsStoredAsIs()
    {
        var bytes = "not a pdf"u8.ToArray();

        var result = Serialize(bytes);

        var data = Assert.Single(result.Data);
        Assert.Equal("pdf", data.Extension);
        Assert.Equal(bytes, data.Data);
    }

    [Fact]
    public void Serialize_DocumentWithoutPages_WritesOnlyTheMetadata()
    {
        var result = Serialize(new PdfDocument());

        var data = Assert.Single(result.Data);
        Assert.Equal("json", data.Extension);
    }

    [Fact]
    public void Serialize_OtherSnapshotTypeOrValue_IsNotHandled()
    {
        var serializer = new PdfSnapshotSerializer(settings: null);

        Assert.False(serializer.TrySerialize(SnapshotType.Png, CreateDocument().ToByteArray(), out _));
        Assert.False(serializer.TrySerialize(SnapshotType.Default, CreateDocument(), out _));
        Assert.False(serializer.TrySerialize(SnapshotType.Pdf, "text", out _));
        Assert.False(serializer.TrySerialize(SnapshotType.Pdf, value: null, out _));
    }

    [Fact]
    public void Serialize_MetadataNone_WritesOnlyTheVersionAndThePages()
    {
        var result = Serialize(CreateDocument(), new PdfSnapshotSettings { Metadata = PdfSnapshotMetadata.None });

        using var json = ParseJson(result);
        Assert.Equal(["version", "pageCount", "pages"], json.RootElement.EnumerateObject().Select(property => property.Name));
    }

    [Fact]
    public void Serialize_MetadataText_WritesTheLinesOfEveryPage()
    {
        var result = Serialize(CreateDocument(), new PdfSnapshotSettings { Metadata = PdfSnapshotMetadata.Text });

        using var json = ParseJson(result);
        var pages = json.RootElement.GetProperty("pages");
        Assert.Equal(["Page 1"], pages[0].GetProperty("text").EnumerateArray().Select(line => line.GetString()));
        Assert.Equal(["Page 2"], pages[1].GetProperty("text").EnumerateArray().Select(line => line.GetString()));
        Assert.False(json.RootElement.TryGetProperty("information", out _));
    }

    [Fact]
    public void Serialize_MetadataDates_WritesTheDatesOfTheDocument()
    {
        var document = CreateDocument();
        document.Information.CreationDate = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.FromHours(1));

        var result = Serialize(document, new PdfSnapshotSettings { Metadata = PdfSnapshotMetadata.Dates });

        using var json = ParseJson(result);
        Assert.Equal("2026-01-02T03:04:05+01:00", json.RootElement.GetProperty("creationDate").GetString());
        Assert.False(json.RootElement.TryGetProperty("modificationDate", out _));
    }

    [Fact]
    public void Serialize_JsonUsesLineFeedsAndEndsWithANewLine()
    {
        var text = Encoding.UTF8.GetString(Serialize(CreateDocument()).Data[0].Data);

        Assert.DoesNotContain('\r', text);
        Assert.EndsWith("}\n", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(0d)]
    [InlineData(-1d)]
    [InlineData(double.NaN)]
    [InlineData(double.PositiveInfinity)]
    public void Settings_InvalidDpi_Throws(double dpi)
    {
        var settings = new PdfSnapshotSettings();

        Assert.Throws<ArgumentOutOfRangeException>("value", () => settings.Dpi = dpi);
    }

    [Fact]
    public void AddPdf_KeepsTheComparerAlreadyRegisteredForPng()
    {
        var settings = new SnapshotSettings();
        var comparer = new ImageComparer(new ImageComparisonSettings { SimilarityThreshold = 0.9f });
        settings.Comparers.Set(SnapshotType.Png, comparer);

        settings.AddPdf();

        Assert.Same(comparer, settings.Comparers.Get(SnapshotType.Png));
    }

    [Fact]
    public void AddPdf_WithImageComparisonSettings_ReplacesTheComparerForPng()
    {
        var settings = new SnapshotSettings();
        var comparer = new ImageComparer();
        settings.Comparers.Set(SnapshotType.Png, comparer);

        settings.AddPdf(imageComparisonSettings: new ImageComparisonSettings { SimilarityThreshold = 0.9f });

        Assert.NotSame(comparer, settings.Comparers.Get(SnapshotType.Png));
        Assert.IsType<ImageComparer>(settings.Comparers.Get(SnapshotType.Png));
    }

    [Fact]
    public void AddPdf_ValidatesDocumentsEndToEnd()
    {
        using var directory = TemporaryDirectory.Create();
        var settings = new SnapshotSettings
        {
            AutoDetectContinuousEnvironment = false,
            SnapshotUpdateStrategy = SnapshotUpdateStrategy.OverwriteWithoutFailure,
            SnapshotPathStrategy = context => directory / ("snapshot_" + context.Index.ToString(CultureInfo.InvariantCulture) + ".verified." + context.Extension),
        };
        settings.AddPdf();
        Snapshot.Validate(CreateDocument(), SnapshotType.Pdf, settings);

        Assert.True(File.Exists(directory / "snapshot_0.verified.json"));
        Assert.True(File.Exists(directory / "snapshot_1.verified.png"));
        Assert.True(File.Exists(directory / "snapshot_2.verified.png"));

        var disallow = settings with { SnapshotUpdateStrategy = SnapshotUpdateStrategy.Disallow };
        Snapshot.Validate(CreateDocument(), SnapshotType.Pdf, disallow);

        var changed = CreateDocument();
        changed.Pages[1].CreateContentBuilder().SetFillRgb(0, 0, 1).Rectangle(150, 50, 20, 20).Fill();
        Assert.Throws<SnapshotAssertionException>(() => Snapshot.Validate(changed, SnapshotType.Pdf, disallow));
    }

    /// <summary>Two 200×100 pages: a red square and the text "Page 1" on the first one, the text "Page 2" on the second one.</summary>
    private static PdfDocument CreateDocument()
    {
        var document = new PdfDocument();
        document.Information.Title = "Sample";
        var font = PdfFont.GetStandardFont(PdfStandardFont.Helvetica);
        document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder()
            .SetFillRgb(1, 0, 0)
            .Rectangle(0, 80, 20, 20)
            .Fill()
            .SetFillGray(0)
            .DrawText("Page 1", font, 12, 40, 40);
        document.Pages.Add(new PdfSize(200, 100)).CreateContentBuilder()
            .SetFillGray(0)
            .DrawText("Page 2", font, 12, 40, 40);
        return document;
    }

    private static SerializedSnapshot Serialize(object value, PdfSnapshotSettings? settings = null)
    {
        Assert.True(new PdfSnapshotSerializer(settings).TrySerialize(SnapshotType.Pdf, value, out var result));
        return result;
    }

    private static JsonDocument ParseJson(SerializedSnapshot result) => JsonDocument.Parse(result.Data[0].Data);
}
