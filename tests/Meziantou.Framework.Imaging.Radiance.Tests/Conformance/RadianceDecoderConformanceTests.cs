using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The Radiance HDR fixtures of the golden corpus cover what the decoder reads: the generic suites compare every valid
/// fixture with its reference, bit for bit (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>), and
/// these tests check that every scanline encoding and every orientation has fixtures, and that the files the decoder
/// rejects are rejected for the reason their fixture declares.
/// </summary>
public sealed class RadianceDecoderConformanceTests
{
    [Theory]
    [InlineData("hdr.encoding=flat")]
    [InlineData("hdr.encoding=old-rle")]
    [InlineData("hdr.encoding=new-rle")]
    [InlineData("hdr.scanOrder=-Y+X")]
    [InlineData("hdr.scanOrder=-Y-X")]
    [InlineData("hdr.scanOrder=+Y-X")]
    [InlineData("hdr.scanOrder=+Y+X")]
    [InlineData("hdr.scanOrder=+X+Y")]
    [InlineData("hdr.scanOrder=-X+Y")]
    [InlineData("hdr.scanOrder=-X-Y")]
    [InlineData("hdr.scanOrder=+X-Y")]
    [InlineData("hdr.exposure")]
    [InlineData("hdr.primaries")]
    public void EveryFeatureHasValidFixtures(string feature)
    {
        Assert.Contains(GetValidFixtures(), fixture => fixture.Entry.HasFeature(feature));
    }

    [Fact]
    public void ScanlinesOfDifferentEncodingsShareAFile()
    {
        var fixture = GoldenCorpus.Default.Get("hdr/mixed-encodings");
        Assert.True(fixture.Entry.HasFeature("hdr.encoding=new-rle") && fixture.Entry.HasFeature("hdr.encoding=old-rle") && fixture.Entry.HasFeature("hdr.encoding=flat"));
    }

    [Theory]
    [InlineData("invalid/hdr/xyze")]
    [InlineData("invalid/hdr/xyze-columns")]
    public void XyzeIsIdentifiedAndRejectedByALoad(string id)
    {
        var fixture = GoldenCorpus.Default.Get(id);
        Assert.Equal(FixtureKinds.Unsupported, fixture.Entry.Kind);
        var data = fixture.ReadInput();
        var info = Image.Identify(data, new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan });
        Assert.True(info.Metadata.RadianceHeader!.IsXyz);
        Assert.Equal("XYZE samples", Assert.Throws<UnsupportedImageFeatureException>(() => Image.Load(data)).Feature);
    }

    [Fact]
    public void HeaderVariablesAreReported()
    {
        using var image = Image.Load(GoldenCorpus.Default.Get("hdr/header-variables").ReadInput());
        var header = image.Metadata.RadianceHeader!;
        Assert.Equal(5d, header.Exposure);
        Assert.Equal(new[] { 1.1, 1d, 0.9 }, header.ColorCorrection.ToArray());
        Assert.Equal(0.5, header.PixelAspectRatio);
        Assert.Equal(2.2, header.Gamma);
        Assert.Equal("RADIANCE 5.4a", header.Software);
        Assert.Equal("-vtv -vp 0 0 1 -vd 0 1 0 -vu 0 0 1 -vh 45 -vv 45", header.View);
    }

    private static IEnumerable<GoldenFixture> GetValidFixtures() => GoldenCorpus.Default.GetIds(format: "hdr", kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get);
}
