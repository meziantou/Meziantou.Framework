using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The DNG fixtures of the golden corpus cover what the decoder reads: the generic suites compare every valid fixture with
/// its reference, bit for bit (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>), and these tests
/// check that every feature has fixtures, and that the files the decoder rejects are identified as DNG.
/// </summary>
public sealed class DngDecoderConformanceTests
{
    [Theory]
    [InlineData("dng.photometric=cfa")]
    [InlineData("dng.photometric=linear-raw")]
    [InlineData("dng.cfa=2x2")]
    [InlineData("dng.cfa=6x6")]
    [InlineData("dng.bits=8")]
    [InlineData("dng.bits=10")]
    [InlineData("dng.bits=12")]
    [InlineData("dng.bits=14")]
    [InlineData("dng.bits=16")]
    [InlineData("dng.bits=16-float")]
    [InlineData("dng.bits=32-float")]
    [InlineData("dng.compression=none")]
    [InlineData("dng.compression=deflate")]
    [InlineData("dng.compression=lossless-jpeg")]
    [InlineData("dng.compression=jpeg-xl")]
    [InlineData("dng.predictor=2")]
    [InlineData("dng.predictor=3")]
    [InlineData("dng.predictor=34892")]
    [InlineData("dng.predictor=34893")]
    [InlineData("dng.predictor=34894")]
    [InlineData("dng.tiles")]
    [InlineData("dng.big-endian")]
    [InlineData("dng.linearization")]
    [InlineData("dng.black-level-pattern")]
    [InlineData("dng.active-area")]
    [InlineData("dng.two-illuminants")]
    [InlineData("dng.forward-matrix")]
    [InlineData("dng.white-xy")]
    [InlineData("dng.baseline-exposure")]
    [InlineData("dng.opcodes=1")]
    [InlineData("dng.opcodes=2")]
    [InlineData("dng.opcodes=3")]
    [InlineData("dng.preview")]
    [InlineData("dng.interleave")]
    [InlineData("dng.xmp")]
    public void EveryFeatureHasValidFixtures(string feature)
    {
        Assert.Contains(GetValidFixtures(), fixture => fixture.Entry.HasFeature(feature));
    }

    [Fact]
    public void ErrorFixturesAreIdentifiedAsDng()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.Entry.Format == "dng" && !fixture.IsValid && fixture.Entry.Kind != FixtureKinds.Limit))
        {
            Assert.Equal(ImageFormat.Dng, Image.DetectFormat(fixture.ReadInput()));
        }
    }

    [Fact]
    public void FilesRewrittenByTheDngSdkRenderLikeTheirSource()
    {
        using var source = Image.Load<RgbaFloat>(GoldenCorpus.Default.Get("dng/bayer-rggb").ReadInput());
        foreach (var id in new[] { "dng/sdk-lossless-jpeg", "dng/sdk-jpeg-xl" })
        {
            using var rewritten = Image.Load<RgbaFloat>(GoldenCorpus.Default.Get(id).ReadInput());
            var expected = new RgbaFloat[source.Width * source.Height];
            var actual = new RgbaFloat[expected.Length];
            source.Frames[0].CopyPixelDataTo(expected);
            rewritten.Frames[0].CopyPixelDataTo(actual);

            // The DNG SDK writes the rationals of the camera profile with its own denominators: the colors agree to 1E-4
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(Math.Abs(expected[i].R - actual[i].R) < 1e-4 && Math.Abs(expected[i].G - actual[i].G) < 1e-4 && Math.Abs(expected[i].B - actual[i].B) < 1e-4, $"{id}: pixel {i} is ({actual[i].R}, {actual[i].G}, {actual[i].B}); the source gives ({expected[i].R}, {expected[i].G}, {expected[i].B}).");
            }
        }
    }

    private static IEnumerable<GoldenFixture> GetValidFixtures() => GoldenCorpus.Default.GetIds(format: "dng", kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get);
}
