using Meziantou.Framework.Imaging.Metadata;
using Meziantou.Framework.Imaging.TestHarness.Fixtures;
using Meziantou.Framework.Imaging.TestHarness.Golden;

namespace Meziantou.Framework.Imaging.Tests.Conformance;

/// <summary>
/// The DPX and Cineon fixtures of the golden corpus cover what the decoders read: the generic suites compare every valid
/// fixture with its reference, sample for sample (<c>EagerLoadConformanceTests</c>, <c>SequentialReadConformanceTests</c>),
/// and these tests check that every descriptor, bit depth, packing and byte order has fixtures, that the files of the
/// three reference tools are among them, and that the header fields are reported.
/// </summary>
public sealed class DpxDecoderConformanceTests
{
    [Theory]
    [InlineData("dpx", "dpx.byteOrder=big")]
    [InlineData("dpx", "dpx.byteOrder=little")]
    [InlineData("dpx", "dpx.bitDepth=1")]
    [InlineData("dpx", "dpx.bitDepth=8")]
    [InlineData("dpx", "dpx.bitDepth=10")]
    [InlineData("dpx", "dpx.bitDepth=12")]
    [InlineData("dpx", "dpx.bitDepth=16")]
    [InlineData("dpx", "dpx.bitDepth=32")]
    [InlineData("dpx", "dpx.bitDepth=64")]
    [InlineData("dpx", "dpx.packing=packed")]
    [InlineData("dpx", "dpx.packing=filled-a")]
    [InlineData("dpx", "dpx.packing=filled-b")]
    [InlineData("dpx", "dpx.descriptor=1")]
    [InlineData("dpx", "dpx.descriptor=4")]
    [InlineData("dpx", "dpx.descriptor=6")]
    [InlineData("dpx", "dpx.descriptor=50")]
    [InlineData("dpx", "dpx.descriptor=51")]
    [InlineData("dpx", "dpx.descriptor=52")]
    [InlineData("dpx", "dpx.descriptor=100")]
    [InlineData("dpx", "dpx.descriptor=101")]
    [InlineData("dpx", "dpx.descriptor=102")]
    [InlineData("dpx", "dpx.descriptor=103")]
    [InlineData("dpx", "dpx.writer=ffmpeg")]
    [InlineData("dpx", "dpx.writer=magick")]
    [InlineData("dpx", "dpx.writer=oiio")]
    [InlineData("cin", "cin.bits=8")]
    [InlineData("cin", "cin.bits=10")]
    [InlineData("cin", "cin.bits=12")]
    [InlineData("cin", "cin.bits=16")]
    [InlineData("cin", "cin.channels=1")]
    [InlineData("cin", "cin.byteOrder=little")]
    public void EveryFeatureHasValidFixtures(string format, string feature)
    {
        Assert.Contains(GoldenCorpus.Default.GetIds(format: format, kind: FixtureKinds.Valid).Select(GoldenCorpus.Default.Get), fixture => fixture.Entry.HasFeature(feature));
    }

    [Theory]
    [InlineData("dpx/orientation-1")]
    [InlineData("dpx/orientation-4")]
    [InlineData("dpx/orientation-7")]
    public void EveryOrientationCodeHasAFixture(string id) => Assert.NotEqual(ExifOrientation.TopLeft, Image.Identify(GoldenCorpus.Default.Get(id).ReadInput()).Metadata.Orientation);

    [Fact]
    public void IndustryHeadersAreReported()
    {
        var header = Image.Identify(GoldenCorpus.Default.Get("dpx/industry-headers").ReadInput()).Metadata.DpxHeader!;
        Assert.Equal("Meziantou.Framework.Imaging corpus", header.Creator);
        Assert.Equal("Project X", header.Project);
        Assert.Equal("1234056789012345", header.FilmEdgeCode);
        Assert.Equal("Academy", header.FilmFormat);
        Assert.Equal(1001u, header.FramePosition);
        Assert.Equal(240u, header.SequenceLength);
        Assert.Equal(24f, header.FrameRate);
        Assert.Equal(180f, header.ShutterAngle);
        Assert.Equal("01:02:03:04", header.TimeCode);
        Assert.Equal(23.976f, header.TemporalSamplingRate);
        Assert.Equal(2.2f, header.Gamma);
        Assert.Equal("Scene 1, take 2", header.SlateInformation);
        Assert.Equal(DpxTransferCharacteristic.PrintingDensity, header.Transfer);
        Assert.Equal(2.048f, header.ReferenceHighQuantity);
    }

    [Theory]
    [InlineData("dpx/tool-ffmpeg-rgb24", "Lavc")]
    [InlineData("dpx/tool-magick-10", "imagemagick")]
    [InlineData("dpx/tool-oiio-10", "OpenImageIO")]
    public void TheWriterOfAToolFileIsItsCreator(string id, string creator)
        => Assert.Contains(creator, Image.Identify(GoldenCorpus.Default.Get(id).ReadInput()).Metadata.DpxHeader!.Creator, StringComparison.Ordinal);

    [Fact]
    public void CineonFilmHeaderIsReported()
    {
        var header = Image.Identify(GoldenCorpus.Default.Get("cin/film-header").ReadInput()).Metadata.CineonHeader!;
        Assert.Equal("frame.0001.cin", header.FileName);
        Assert.Equal("2026:11:15", header.CreationDate);
        Assert.Equal("10:20:30EST", header.CreationTime);
        Assert.Equal("ACADEMY", header.FilmFormat);
        Assert.Equal("KEYFRAME", header.FrameAttribute);
        Assert.Equal("1234056789012345", header.FilmEdgeCode);
        Assert.Equal(24f, header.FrameRate);
        Assert.Equal("Scanner", header.InputDevice);
    }
}
