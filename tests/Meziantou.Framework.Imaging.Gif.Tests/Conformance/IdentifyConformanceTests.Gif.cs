using Meziantou.Framework.Imaging.TestHarness.Adapters;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

// The identification rules that only GIF has: the generic cases of this class are compiled into every codec test project
public sealed partial class IdentifyConformanceTests
{
    [Fact]
    public void HeaderIdentificationNeverGuessesGifFrameCounts()
    {
        foreach (var fixture in GoldenCorpus.Default.Fixtures.Where(fixture => fixture.IsValid && fixture.Entry.Format == "gif"))
        {
            var info = Image.Identify(fixture.ReadInput());
            Assert.Equal(ImageIdentifyMode.Header, info.IdentifyMode);
            Assert.Null(info.FrameCount);
            Assert.NotEqual(false, info.IsAnimated);
        }
    }

    [Fact]
    public void FrameLimitOfAFullScanIsASafetyBound()
    {
        // limit/gif/frames-over-limit: 4 frames, MaxFrames = 3 fails; MaxFrames = 4 succeeds (the limit is inclusive)
        var fixture = GoldenCorpus.Default.Get("limit/gif/frames-over-limit");
        var data = fixture.ReadInput();
        var exception = Assert.Throws<ImageResourceLimitException>(() => Image.Identify(data, FixtureOptions.CreateIdentifyOptions(fixture, ImageIdentifyMode.FullScan)));
        Assert.Equal(ImageResourceLimitKind.Frames, exception.Kind);
        Assert.Equal(3, exception.Limit);
        Assert.Equal(4, exception.Requested);

        var exact = new ImageIdentifyOptions { Mode = ImageIdentifyMode.FullScan, Configuration = new ImageConfiguration { Limits = new ImageResourceLimits { MaxFrames = 4 } } };
        Assert.Equal(4, Image.Identify(data, exact).FrameCount);
    }
}
