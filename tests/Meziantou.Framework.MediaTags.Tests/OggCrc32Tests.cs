using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class OggCrc32Tests
{
    // Known answers for the OGG variant of CRC-32: polynomial 0x04C11DB7, initial value 0, not reflected, no
    // final XOR. They come from an independent implementation of the specification, so a wrong table, a wrong
    // shift direction or an off-by-one in the C# code fails here rather than silently producing files that
    // every OGG player rejects.
    [Theory]
    [InlineData("123456789", 0x89A1897Fu)]
    [InlineData("OggS", 0x5FB0A94Fu)]
    public void Compute_KnownAnswer(string text, uint expected)
    {
        Assert.Equal(expected, OggCrc32.Compute(Encoding.ASCII.GetBytes(text)));
    }

    [Fact]
    public void Compute_SingleByte_KnownAnswer()
    {
        // A single 0x01 byte shifts the polynomial into the register exactly once
        Assert.Equal(0x04C11DB7u, OggCrc32.Compute([0x01]));
    }

    [Fact]
    public void Compute_EmptyData_ReturnsZero()
    {
        var crc = OggCrc32.Compute([]);
        Assert.Equal(0u, crc);
    }

    [Fact]
    public void Compute_MatchesTheChecksumStoredByTheEncoder()
    {
        // The strongest check available: the checksum in the fixture was computed by libogg, not by this code.
        var file = File.ReadAllBytes(Path.Combine("TestFiles", "basic.ogg"));
        var page = OggPageInspector.ReadPages(file)[0];

        Assert.Equal(page.StoredChecksum, OggCrc32.Compute(page.BytesWithZeroedChecksum));
    }

    [Fact]
    public void Compute_Update_MatchesComputeOverTheWholeInput()
    {
        byte[] data = [0x4F, 0x67, 0x67, 0x53, 0x00, 0x02, 0xFF, 0x10];

        var whole = OggCrc32.Compute(data);
        var incremental = OggCrc32.Update(OggCrc32.Compute(data.AsSpan(0, 3)), data.AsSpan(3));

        Assert.Equal(whole, incremental);
    }
}
