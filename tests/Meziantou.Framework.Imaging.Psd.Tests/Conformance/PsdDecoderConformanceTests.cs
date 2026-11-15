using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;
using Meziantou.Framework.Imaging.TestHarness.Psd;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The PSD fixtures of the golden corpus cover what the decoder reads: the generic suites compare every valid fixture with
/// its reference, exactly (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>), and these tests check
/// that every color mode, depth, compression and writer has fixtures, and that the CMYK fixtures, which an untyped load
/// rejects, load as the inks <see cref="ReferencePsd"/> reads.
/// </summary>
public sealed class PsdDecoderConformanceTests
{
    [Theory]
    [InlineData("psd.mode=bitmap")]
    [InlineData("psd.mode=grayscale")]
    [InlineData("psd.mode=duotone")]
    [InlineData("psd.mode=indexed")]
    [InlineData("psd.mode=rgb")]
    [InlineData("psd.depth=1")]
    [InlineData("psd.depth=8")]
    [InlineData("psd.depth=16")]
    [InlineData("psd.depth=32")]
    [InlineData("psd.compression=raw")]
    [InlineData("psd.compression=rle")]
    [InlineData("psd.version=psb")]
    [InlineData("psd.alpha=merged")]
    [InlineData("psd.extraChannels")]
    [InlineData("psd.layers=layerInfo")]
    [InlineData("psd.layers=lr16")]
    [InlineData("psd.resources")]
    [InlineData("psd.writer=psd-tools")]
    [InlineData("psd.writer=magick")]
    [InlineData("psd.writer=builder")]
    public void EveryFeatureHasValidFixtures(string feature)
    {
        Assert.Contains(GetFixtures(FixtureKinds.Valid), fixture => fixture.Entry.HasFeature(feature));
    }

    [Theory]
    [InlineData("invalid/psd/cmyk8")]
    [InlineData("invalid/psd/cmyk16")]
    public void CmykFixturesLoadAsTheirInks(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        var data = fixture.ReadInput();
        var picture = ReferencePsd.Read(data);
        Assert.Equal(ImageColorModel.Cmyk, Image.Identify(data).ColorModel);
        Assert.Equal("CMYK samples without color conversion", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);

        // The stored samples are inverted: the largest one is no ink
        using var image = Image.Load<Cmyk64>(data);
        var max = picture.Depth == 16 ? 65535 : 255;
        for (var y = 0; y < picture.Height; y++)
        {
            for (var x = 0; x < picture.Width; x++)
            {
                var i = (y * picture.Width) + x;
                int Ink(int channel) => max - (picture.Depth == 16 ? System.Buffers.Binary.BinaryPrimitives.ReadUInt16BigEndian(picture.Channels[channel].AsSpan(i * 2)) : picture.Channels[channel][i]);
                var scale = picture.Depth == 16 ? 1 : 257;
                Assert.Equal(new Cmyk64((ushort)(Ink(0) * scale), (ushort)(Ink(1) * scale), (ushort)(Ink(2) * scale), (ushort)(Ink(3) * scale)), image.Frames[0][x, y]);
            }
        }
    }

    [Fact]
    public void LabFixtureLoadsWithAConversion()
    {
        var data = GoldenCorpus.Default.Get("invalid/psd/lab8").ReadInput();
        Assert.Equal(ImageColorModel.CieLab, Image.Identify(data).ColorModel);
        using var image = Image.Load(data, new ImageDecodeOptions { CieLabConversion = CieLabConversionOptions.Default });
        Assert.Equal(PixelFormat.Rgb24, image.PixelFormat);
        Assert.Equal(CieLabConversionOptions.Default.DestinationProfile, image.Metadata.IccProfile);
    }

    [Fact]
    public void AFileWithoutCompositeIsIdentifiedAndRejected()
    {
        var fixture = GoldenCorpus.Default.Get("invalid/psd/no-composite");
        Assert.Equal(FixtureKinds.Unsupported, fixture.Entry.Kind);
        var data = fixture.ReadInput();
        Assert.Equal(new Size(6, 4), Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan }).Size);
        Assert.Equal("No composite image", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    [Fact]
    public void ImageMagickUnmattesTheColorsTheSameWay()
    {
        // The generator records ImageMagick's decoding of the matted fixtures: it removes the matte of RGB composites with the
        // same rule (it does not un-matte grayscale ones, which the generator declares)
        foreach (var fixture in GetFixtures(FixtureKinds.Valid).Where(fixture => fixture.Entry.HasFeature("psd.alpha=merged") && fixture.Entry.HasFeature("psd.mode=rgb") && !fixture.Entry.HasFeature("psd.depth=32")))
        {
            var checks = fixture.Entry.Reference!.CrossChecks;
            var magick = Assert.Single(checks, check => check.Decoder.StartsWith("ImageMagick", StringComparison.Ordinal));
            Assert.True(magick.MaxAbsoluteError <= 1, $"{fixture.Id}: ImageMagick differs by {magick.MaxAbsoluteError}");
        }
    }

    private static IEnumerable<GoldenFixture> GetFixtures(string kind) => GoldenCorpus.Default.GetIds(format: "psd", kind: kind).Select(GoldenCorpus.Default.Get);
}
