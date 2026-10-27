using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The OpenEXR fixtures of the golden corpus cover what the decoder reads: the generic suites compare every valid fixture
/// with what OpenEXR decodes, bit for bit (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>), and
/// these tests check that every compression and every way of storing chunks has fixtures, and that every compression
/// the decoder rejects has one.
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

    private static IEnumerable<GoldenFixture> GetValidFixtures() => GoldenCorpus.Default.Fixtures.Where(fixture => fixture.Entry.Format == "exr" && fixture.IsValid);
}
