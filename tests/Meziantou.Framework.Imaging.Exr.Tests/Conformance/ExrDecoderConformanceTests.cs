using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The OpenEXR fixtures of the golden corpus cover what the decoder reads: the generic suites compare every valid fixture
/// with what OpenEXR decodes, bit for bit (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>), and
/// these tests check that every compression and every way of storing chunks has fixtures, and that every compression
/// the decoder rejects has one. The files the default options reject for their extra channels, layers or pixels outside
/// the display window declare their decodings with <see cref="OpenExrDecodingOptions"/> (<c>exrDecodings</c>), which
/// these tests compare with what OpenEXR decodes, bit for bit, too.
/// </summary>
public sealed class ExrDecoderConformanceTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("rle")]
    [InlineData("zips")]
    [InlineData("zip")]
    [InlineData("piz")]
    [InlineData("pxr24")]
    [InlineData("b44")]
    [InlineData("b44a")]
    public void EveryCompressionHasValidFixtures(string compression)
    {
        var fixtures = GetValidFixtures().Where(fixture => fixture.Entry.HasFeature("exr.compression=" + compression)).ToList();
        Assert.NotEmpty(fixtures);
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("exr.type=half"));
        Assert.Contains(fixtures, fixture => fixture.Entry.HasFeature("exr.type=float"));
    }

    [Theory]
    [InlineData("exr.storage=tiled")]
    [InlineData("exr.levels=mipmap")]
    [InlineData("exr.levels=ripmap")]
    [InlineData("exr.lineOrder=decreasing-y")]
    [InlineData("exr.lineOrder=random-y")]
    [InlineData("exr.dataWindow=inside")]
    [InlineData("exr.type=uint")]
    [InlineData("exr.external")]
    public void EveryLayoutHasValidFixtures(string feature)
    {
        Assert.Contains(GetValidFixtures(), fixture => fixture.Entry.HasFeature(feature));
    }

    [Theory]
    [InlineData("invalid/exr/compression-dwaa")]
    [InlineData("invalid/exr/compression-dwab")]
    [InlineData("invalid/exr/compression-zstd")]
    [InlineData("invalid/exr/compression-htj2k32")]
    public void RejectedCompressionsHaveFixtures(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.Equal(FixtureKinds.Unsupported, fixture.Entry.Kind);
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(fixture.ReadInput()));
        Assert.StartsWith("Compression: ", exception.Feature, StringComparison.Ordinal);
    }

    public static TheoryData<string> DecodingFixtures => [.. DecodingIds];

    private static IEnumerable<string> DecodingIds => GoldenCorpus.Default.GetIds(format: "exr", kind: FixtureKinds.Unsupported).Where(id => GoldenCorpus.Default.Get(id).Entry.ExrDecodings is not null);

    [Theory]
    [MemberData(nameof(DecodingFixtures))]
    public void DecodingsWithOptionsHaveThePixelsOfOpenExr(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data));
        foreach (var decoding in fixture.ExrDecodings)
        {
            var options = new OpenExrDecodingOptions { Layer = decoding.Layer, WindowMode = Enum.Parse<OpenExrWindowMode>(decoding.WindowMode) };
            var configuration = new ImageConfiguration { OpenExrDecoding = options };
            var description = $"{id}, layer {decoding.Layer ?? "(every channel)"}, {decoding.WindowMode}";
            if (decoding.ExpectedError is { } error)
            {
                Assert.Equal(error.Feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration })).Feature);
                Assert.Equal(error.Feature, Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data, new ImageDecodeOptions { Configuration = configuration })).Feature);
                continue;
            }

            var info = Image.Identify(data, new ImageIdentifyOptions { Configuration = configuration, Mode = ImageIdentifyMode.FullScan });
            Assert.Equal(new Size(decoding.Width!.Value, decoding.Height!.Value), info.Size);
            Assert.Equal(decoding.PixelFormat, info.PixelFormat.ToString());
            Assert.Equal(decoding.ColorModel, info.ColorModel.ToString());
            Assert.Equal(decoding.BitsPerComponent, info.BitsPerComponent);

            var expected = fixture.GetExrDecodingReference(decoding).Memory;
            foreach (var workers in (int[])[1, 16])
            {
                var parallel = new ImageConfiguration { OpenExrDecoding = options, MaxDegreeOfParallelism = workers };
                using (var image = Image.Load(new MemoryStream(data), new ImageDecodeOptions { Configuration = parallel }))
                {
                    Assert.Equal(decoding.PixelFormat, image.PixelFormat.ToString());
                    Assert.NotNull(image.Metadata.OpenExrWindows);
                }

                using var floats = Image.Load<RgbaFloat>(data, new ImageDecodeOptions { Configuration = parallel });
                Assert.True(expected.Span.SequenceEqual(GetBytes(floats)), $"The pixels of {description} with {workers.ToString(CultureInfo.InvariantCulture)} workers differ from those of OpenEXR.");
            }

            using var reader = Image.OpenReader<RgbaFloat>(new MemoryStream(data), new ImageReaderOptions { Configuration = configuration });
            using var frame = reader.ReadFrame();
            Assert.NotNull(frame);
            Assert.True(expected.Span.SequenceEqual(GetBytes(frame)), $"The pixels of {description} read by a reader differ from those of OpenEXR.");
        }

        static byte[] GetBytes(Image<RgbaFloat> image)
        {
            var bytes = new byte[image.Width * image.Height * 16];
            image.Frames[0].CopyPixelBytesTo(bytes);
            return bytes;
        }
    }

    [Fact]
    public void DecodingsWithOptionsCoverTheDecoderFeatures()
    {
        var fixtures = DecodingIds.Select(GoldenCorpus.Default.Get).ToList();
        var decodings = fixtures.SelectMany(fixture => fixture.ExrDecodings.Select(decoding => (Fixture: fixture, Decoding: decoding))).ToList();
        var decoded = decodings.Where(item => item.Decoding.ExpectedError is null).ToList();

        // Every compression, scan lines and tiles, mipmaps, sub-sampled channels that are ignored, sample images of the OpenEXR project
        foreach (var compression in new[] { "none", "rle", "zips", "zip", "piz", "pxr24", "b44", "b44a" })
        {
            Assert.Contains(decoded, item => item.Fixture.Id == "invalid/exr/overscan-layers-" + compression);
        }

        Assert.Contains(decoded, item => item.Fixture.Id == "invalid/exr/tiles-layers-overscan" && item.Decoding.WindowMode == "CropToDisplayWindow");
        Assert.Contains(decoded, item => item.Fixture.Id == "invalid/exr/tiles-mipmap-layers");
        Assert.Contains(decoded, item => item.Fixture.Id == "invalid/exr/subsampled-extra-channel");
        Assert.Contains(decoded, item => item.Fixture.Entry.Provenance.Origin == "external");

        // Layers, windows and what stays rejected
        Assert.Contains(decoded, item => item.Decoding.Layer is { Length: 0 });
        Assert.Contains(decoded, item => item.Decoding.Layer is { Length: > 0 });
        Assert.Contains(decoded, item => item.Decoding.Layer is null);
        Assert.Contains(decoded, item => item.Decoding.ColorModel == "Grayscale");
        Assert.Contains(decoded, item => item.Decoding.ColorModel == "Rgba");
        Assert.Contains(decoded, item => item.Decoding.WindowMode == "CropToDisplayWindow");
        Assert.Contains(decoded, item => item.Decoding.WindowMode == "DataWindow");
        foreach (var feature in new[] { "Missing layer", "Ambiguous layer", "Luminance-chroma channels", "Pixels outside the display window" })
        {
            Assert.Contains(decodings, item => item.Decoding.ExpectedError?.Feature == feature);
        }

        // Every file rejected for its channels or its windows says how it is decoded
        var rejected = GoldenCorpus.Default.GetIds(format: "exr", kind: FixtureKinds.Unsupported)
            .Select(GoldenCorpus.Default.Get)
            .Where(fixture => fixture.Entry.ExpectedError?.Feature is "Extra channels" or "Pixels outside the display window")
            .Select(fixture => fixture.Id);
        Assert.Equal(rejected.Order(StringComparer.Ordinal), DecodingIds.Order(StringComparer.Ordinal));
    }

    private static IEnumerable<GoldenFixture> GetValidFixtures() => GoldenCorpus.Default.Fixtures.Where(fixture => fixture.Entry.Format == "exr" && fixture.IsValid);
}
