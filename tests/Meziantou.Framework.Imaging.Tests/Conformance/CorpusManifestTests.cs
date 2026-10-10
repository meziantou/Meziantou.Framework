using System.Text.Json;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Pixels;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// Validates the committed corpus before any pixel comparison runs. Golden decode tests must only
/// consume fixtures that pass these checks.
/// </summary>
public sealed class CorpusManifestTests
{
    [Fact]
    public void CommittedManifestIsValid()
    {
        var root = FixtureRoot.GetDirectory();
        var manifest = FixtureManifestLoader.Load(root / FixtureRoot.ManifestFileName);
        var errors = FixtureManifestValidator.Validate(manifest, root);
        Assert.Empty(errors);
    }

    [Fact]
    public void SchemaVersionMatchesHarness()
    {
        using var schema = LoadSchema();
        var version = schema.RootElement.GetProperty("properties").GetProperty("schemaVersion").GetProperty("const").GetInt32();
        Assert.Equal(FixtureManifest.CurrentSchemaVersion, version);
    }

    [Fact]
    public void SchemaEnumerationsMatchHarness()
    {
        using var schema = LoadSchema();
        var defs = schema.RootElement.GetProperty("$defs");
        AssertSameSet(FixtureManifestValidator.AllowedLicenses, Enum(defs, "provenance", "license"));
        AssertSameSet(FixtureManifestValidator.AllowedOrigins, Enum(defs, "provenance", "origin"));
        AssertSameSet(FixtureKinds.All, Enum(defs, "fixture", "kind"));
        AssertSameSet(FixtureManifestValidator.AllowedFormats, Enum(defs, "fixture", "format"));
        AssertSameSet(RawPixelLayout.All.Select(layout => layout.Name), Enum(defs, "buffer", "layout"));
        AssertSameSet(RawBufferReader.AllowedCompressions, Enum(defs, "buffer", "compression"));
        AssertSameSet(FixtureManifestValidator.AllowedPixelFormats, Enum(defs, "expected", "pixelFormat"));
        AssertSameSet(FixtureManifestValidator.AllowedColorModels, Enum(defs, "expected", "colorModel"));
        AssertSameSet(FixtureManifestValidator.AllowedIccProfiles, Enum(defs, "expected", "iccProfile"));
        AssertSameSet(FixtureManifestValidator.AllowedCrossCheckResults, Enum(defs, "crossCheck", "result"));
        AssertSameSet(FixtureManifestValidator.AllowedLimits, defs.GetProperty("decodeOptions").GetProperty("properties").GetProperty("limits").GetProperty("properties").EnumerateObject().Select(property => property.Name));

        var comparison = defs.GetProperty("comparison").GetProperty("properties");
        Assert.Equal(ComparisonPolicy.AbsoluteErrorCeiling, comparison.GetProperty("maxAbsoluteError").GetProperty("maximum").GetInt32());
        Assert.Equal(ComparisonPolicy.MeanAbsoluteErrorCeiling, comparison.GetProperty("maxMeanAbsoluteError").GetProperty("maximum").GetDouble());
    }

    [Fact]
    public void LibraryEnumerationNamesAreKnownToTheHarness()
    {
        // The manifest uses library member names; keep both lists in sync when the library adds members
        AssertSameSet(System.Enum.GetNames<PixelFormat>().Where(name => name != nameof(PixelFormat.Unknown)), FixtureManifestValidator.AllowedPixelFormats);
        AssertSameSet(System.Enum.GetNames<ImageColorModel>().Where(name => name != nameof(ImageColorModel.Unknown)), FixtureManifestValidator.AllowedColorModels);
        AssertSameSet(typeof(ImageResourceLimits).GetProperties().Where(property => property.Name.StartsWith("Max", StringComparison.Ordinal)).Select(property => property.Name), FixtureManifestValidator.AllowedLimits);
        foreach (var fixture in LoadManifest().Fixtures.Where(fixture => fixture.ExpectedError is not null))
        {
            if (fixture.ExpectedError!.Format is { } format)
            {
                Assert.True(System.Enum.TryParse<ImageFormat>(format, ignoreCase: false, out _), $"{fixture.Id}: unknown ImageFormat '{format}'.");
            }

            if (fixture.ExpectedError.LimitKind is { } kind)
            {
                Assert.True(System.Enum.TryParse<ImageResourceLimitKind>(kind, ignoreCase: false, out _), $"{fixture.Id}: unknown ImageResourceLimitKind '{kind}'.");
            }

            Assert.NotNull(typeof(ImageException).Assembly.GetTypes().SingleOrDefault(type => type.Name == fixture.ExpectedError.Exception && typeof(Exception).IsAssignableFrom(type)));
        }
    }

    [Fact]
    public void ManifestRecordsGeneratorAndPinnedTools()
    {
        var manifest = LoadManifest();
        Assert.NotNull(manifest.Generator);
        Assert.Equal("tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.cs", manifest.Generator!.Script);
        Assert.Contains(manifest.Generator.Tools, tool => tool.StartsWith("ffmpeg ", StringComparison.Ordinal));
        Assert.Contains(manifest.Generator.Tools, tool => tool.StartsWith("libjpeg-turbo ", StringComparison.Ordinal));
        foreach (var fixture in manifest.Fixtures)
        {
            Assert.True(fixture.Provenance.Tools is { Count: > 0 }, $"{fixture.Id}: tools must be recorded.");
            // WebP, QOI, BMP, TGA and Netpbm come from their own generators (libwebp tools, the qoi.h reference, FFmpeg; any
            // OS); the golden generator keeps them unchanged
            var generator = fixture.Format switch
            {
                "webp" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/WebPCorpus.cs",
                "qoi" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/QoiCorpus.cs",
                "bmp" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/BmpCorpus.cs",
                "tga" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/TgaCorpus.cs",
                "pnm" => "tools/Meziantou.Framework.Imaging.CorpusGenerator/PnmCorpus.cs",
                _ => "tools/Meziantou.Framework.Imaging.CorpusGenerator/GoldenCorpus.cs",
            };
            Assert.StartsWith(generator, fixture.Provenance.Generator);
            if (fixture.Reference?.Method == FixtureReference.Decoded)
            {
                Assert.True(fixture.Reference.CrossChecks is { Count: > 0 }, $"{fixture.Id}: decoded references must be cross-checked with another independent decoder.");
            }

            if (fixture.Reference is { Method: FixtureReference.HandComputed } reference)
            {
                Assert.True(reference.CrossChecks is { Count: > 0 }, $"{fixture.Id}: hand-computed references are cross-checked with an independent decoder.");
            }
        }
    }

    [Theory]
    [InlineData("png.interlace=adam7")]
    [InlineData("png.bitDepth=1")]
    [InlineData("png.bitDepth=2")]
    [InlineData("png.bitDepth=4")]
    [InlineData("png.bitDepth=16")]
    [InlineData("png.colorType=3")]
    [InlineData("png.chunk=tRNS")]
    [InlineData("apng.blend=over,source")]
    [InlineData("apng.dispose=background,none,previous")]
    [InlineData("apng.poster=separate")]
    [InlineData("gif.disposal=0,1,2,3")]
    [InlineData("gif.version=87a")]
    [InlineData("gif.interlaced")]
    [InlineData("gif.localPalette")]
    [InlineData("gif.lzw.tableFull")]
    [InlineData("gif.lzw.deferredClear")]
    [InlineData("gif.minCodeSize=2")]
    [InlineData("jpeg.process=baseline")]
    [InlineData("jpeg.process=progressive")]
    [InlineData("jpeg.sampling=4:2:0")]
    [InlineData("jpeg.sampling=4:2:2")]
    [InlineData("jpeg.sampling=4:4:4")]
    [InlineData("jpeg.components=1")]
    [InlineData("jpeg.app1=Exif")]
    [InlineData("jpeg.process=extended")]
    [InlineData("jpeg.sampling=4:4:0")]
    [InlineData("jpeg.sampling=4:1:1")]
    [InlineData("jpeg.quantization=16-bit")]
    [InlineData("jpeg.scanComponents=1,1,1")]
    [InlineData("jpeg.app14=Adobe")]
    [InlineData("jpeg.app2=ICC_PROFILE")]
    [InlineData("webp.layout=simple")]
    [InlineData("webp.layout=extended")]
    [InlineData("webp.bitstream=vp8")]
    [InlineData("webp.bitstream=vp8l")]
    [InlineData("webp.alpha=alph")]
    [InlineData("webp.alpha=vp8l")]
    [InlineData("webp.alph.compression=none")]
    [InlineData("webp.alph.compression=vp8l")]
    [InlineData("webp.vp8.filter=simple")]
    [InlineData("webp.vp8.filter=normal")]
    [InlineData("webp.vp8.segmentation")]
    [InlineData("webp.vp8l.transform=predictor")]
    [InlineData("webp.vp8l.transform=color-indexing")]
    [InlineData("webp.chunk=ICCP")]
    [InlineData("webp.chunk=EXIF")]
    [InlineData("webp.chunk=XMP")]
    [InlineData("webp.animation")]
    [InlineData("webp.blend=alpha")]
    [InlineData("webp.blend=none")]
    [InlineData("webp.dispose=background")]
    [InlineData("webp.frame=partial")]
    [InlineData("webp.frames=mixed")]
    [InlineData("webp.loop=infinite")]
    [InlineData("webp.loop=finite")]
    [InlineData("qoi.channels=3")]
    [InlineData("qoi.channels=4")]
    [InlineData("qoi.colorspace=linear")]
    [InlineData("qoi.op=index")]
    [InlineData("qoi.op=diff")]
    [InlineData("qoi.op=luma")]
    [InlineData("qoi.op=rgb")]
    [InlineData("qoi.op=rgba")]
    [InlineData("qoi.op=run")]
    [InlineData("qoi.run=62")]
    [InlineData("qoi.run.crossesRow")]
    [InlineData("bmp.bpp=1")]
    [InlineData("bmp.bpp=4")]
    [InlineData("bmp.bpp=8")]
    [InlineData("bmp.bpp=16")]
    [InlineData("bmp.bpp=24")]
    [InlineData("bmp.bpp=32")]
    [InlineData("bmp.rowOrder=bottom-up")]
    [InlineData("bmp.rowOrder=top-down")]
    [InlineData("bmp.rowPadding")]
    [InlineData("bmp.compression=bitfields")]
    [InlineData("bmp.alphaMask=yes")]
    [InlineData("bmp.alphaMask=no")]
    [InlineData("bmp.gapBeforePixels")]
    [InlineData("bmp.resolution")]
    [InlineData("bmp.header=40")]
    [InlineData("bmp.header=56")]
    [InlineData("bmp.header=108")]
    [InlineData("tga.imageType=1")]
    [InlineData("tga.imageType=2")]
    [InlineData("tga.imageType=3")]
    [InlineData("tga.imageType=10")]
    [InlineData("tga.imageType=11")]
    [InlineData("tga.depth=15")]
    [InlineData("tga.depth=16")]
    [InlineData("tga.depth=24")]
    [InlineData("tga.depth=32")]
    [InlineData("tga.rowOrder=top-down")]
    [InlineData("tga.rowOrder=bottom-up")]
    [InlineData("tga.columnOrder=right-to-left")]
    [InlineData("tga.alphaBits=0")]
    [InlineData("tga.alphaBits=1")]
    [InlineData("tga.alphaBits=8")]
    [InlineData("tga.colorMap.offset")]
    [InlineData("tga.rle.run")]
    [InlineData("tga.rle.raw")]
    [InlineData("tga.rle.packet=128")]
    [InlineData("tga.rle.crossesRow")]
    [InlineData("tga.idField")]
    [InlineData("tga.footer")]
    [InlineData("pnm.magic=P1")]
    [InlineData("pnm.magic=P2")]
    [InlineData("pnm.magic=P3")]
    [InlineData("pnm.magic=P4")]
    [InlineData("pnm.magic=P5")]
    [InlineData("pnm.magic=P6")]
    [InlineData("pnm.magic=P7")]
    [InlineData("pnm.tuple=BLACKANDWHITE")]
    [InlineData("pnm.tuple=GRAYSCALE_ALPHA")]
    [InlineData("pnm.tuple=RGB_ALPHA")]
    [InlineData("pnm.maxval=1")]
    [InlineData("pnm.maxval=255")]
    [InlineData("pnm.maxval=65535")]
    [InlineData("pnm.maxval.normalized")]
    [InlineData("pnm.comments")]
    [InlineData("pnm.pbm.rowPadding")]
    public void StarterCorpusCoversFeature(string feature)
    {
        Assert.Contains(LoadManifest().Fixtures, fixture => fixture.Kind == FixtureKinds.Valid && fixture.HasFeature(feature));
    }

    [Theory]
    [InlineData(FixtureKinds.Invalid, "png")]
    [InlineData(FixtureKinds.Invalid, "gif")]
    [InlineData(FixtureKinds.Unsupported, "gif")]
    [InlineData(FixtureKinds.Unsupported, "jpeg")]
    [InlineData(FixtureKinds.Invalid, "jpeg")]
    [InlineData(FixtureKinds.Limit, "jpeg")]
    [InlineData(FixtureKinds.Limit, "png")]
    [InlineData(FixtureKinds.Limit, "gif")]
    [InlineData(FixtureKinds.Invalid, "webp")]
    [InlineData(FixtureKinds.Unsupported, "webp")]
    [InlineData(FixtureKinds.Limit, "webp")]
    [InlineData(FixtureKinds.Invalid, "qoi")]
    [InlineData(FixtureKinds.Limit, "qoi")]
    [InlineData(FixtureKinds.Invalid, "bmp")]
    [InlineData(FixtureKinds.Unsupported, "bmp")]
    [InlineData(FixtureKinds.Limit, "bmp")]
    [InlineData(FixtureKinds.Invalid, "tga")]
    [InlineData(FixtureKinds.Unsupported, "tga")]
    [InlineData(FixtureKinds.Limit, "tga")]
    [InlineData(FixtureKinds.Invalid, "pnm")]
    [InlineData(FixtureKinds.Unsupported, "pnm")]
    [InlineData(FixtureKinds.Limit, "pnm")]
    public void StarterCorpusHasErrorFixtures(string kind, string format)
    {
        Assert.Contains(LoadManifest().Fixtures, fixture => fixture.Kind == kind && fixture.Format == format && fixture.ExpectedError is not null && fixture.Expected is null);
    }

    private static FixtureManifest LoadManifest() => FixtureManifestLoader.Load(FixtureRoot.GetDirectory() / FixtureRoot.ManifestFileName);

    private static JsonDocument LoadSchema() => JsonDocument.Parse(File.ReadAllText(FixtureRoot.GetDirectory() / FixtureRoot.SchemaFileName));

    private static IEnumerable<string> Enum(JsonElement defs, string definition, string property)
        => defs.GetProperty(definition).GetProperty("properties").GetProperty(property).GetProperty("enum").EnumerateArray().Select(item => item.GetString()!);

    private static void AssertSameSet(IEnumerable<string> expected, IEnumerable<string> actual)
        => Assert.Equal(expected.Order(StringComparer.Ordinal).ToArray(), actual.Order(StringComparer.Ordinal).ToArray());
}
