using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class OggCrc32Tests
{
    [Fact]
    public void Compute_EmptyData_ReturnsZero()
    {
        var crc = OggCrc32.Compute([]);
        Assert.Equal(0u, crc);
    }

    [Fact]
    public void Compute_SingleByte()
    {
        var crc = OggCrc32.Compute([0x01]);
        Assert.NotEqual(0u, crc);
    }

    [Fact]
    public void Compute_SameInput_SameOutput()
    {
        byte[] data = [0x4F, 0x67, 0x67, 0x53]; // "OggS"
        var crc1 = OggCrc32.Compute(data);
        var crc2 = OggCrc32.Compute(data);
        Assert.Equal(crc1, crc2);
    }

    [Fact]
    public void Compute_DifferentInput_DifferentOutput()
    {
        var crc1 = OggCrc32.Compute([0x01, 0x02, 0x03]);
        var crc2 = OggCrc32.Compute([0x04, 0x05, 0x06]);
        Assert.NotEqual(crc1, crc2);
    }
}
