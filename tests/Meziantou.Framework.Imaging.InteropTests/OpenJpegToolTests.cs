using Meziantou.Framework.Imaging.TestHarness.ExternalTools;
using Meziantou.Prebuilt;

namespace Meziantou.Framework.Imaging.InteropTests;

/// <summary>
/// Verifies the pinned JPEG 2000 tools (downloaded from meziantou/prebuilt) and the helpers that read what they write,
/// without the library under test. The behaviors the other tests rely on are pinned here.
/// </summary>
public sealed class OpenJpegToolTests
{
    [Fact]
    public async Task OpenJpegReportsThePinnedVersion()
    {
        var openJpeg = await RequiredTools.OpenJpegAsync();

        // The three tools report the version the package declares (checked for each of them when they are located)
        Assert.Equal(PrebuiltTools.OpjCompress.Version, openJpeg.Version);
        Assert.Equal(PrebuiltTools.OpjDecompress.Version, openJpeg.Version);
        Assert.Equal(PrebuiltTools.OpjDump.Version, openJpeg.Version);
    }

    [Fact]
    public async Task OpenJphRuns()
    {
        var openJph = await RequiredTools.OpenJphAsync();

        Assert.True(File.Exists(openJph.CompressPath));
        Assert.True(File.Exists(openJph.ExpandPath));
        Assert.Equal(PrebuiltTools.OjphExpand.Version, OpenJphTool.Version);
    }
}
