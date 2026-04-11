using Meziantou.Framework.MediaTags.Internals;

namespace Meziantou.Framework.MediaTags.Tests;

public sealed class SynchsafeIntegerTests
{
    [Theory]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x00 }, 0)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x01 }, 1)]
    [InlineData(new byte[] { 0x00, 0x00, 0x00, 0x7F }, 127)]
    [InlineData(new byte[] { 0x00, 0x00, 0x01, 0x00 }, 128)]
    [InlineData(new byte[] { 0x00, 0x00, 0x02, 0x00 }, 256)]
    [InlineData(new byte[] { 0x00, 0x01, 0x00, 0x00 }, 16384)]
    [InlineData(new byte[] { 0x7F, 0x7F, 0x7F, 0x7F }, 0x0FFFFFFF)]
    public void Decode_KnownValues(byte[] data, int expected)
    {
        var result = SynchsafeInteger.Decode(data);
        Assert.Equal(expected, result);
    }

    [Theory]
    [InlineData(0, new byte[] { 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(1, new byte[] { 0x00, 0x00, 0x00, 0x01 })]
    [InlineData(127, new byte[] { 0x00, 0x00, 0x00, 0x7F })]
    [InlineData(128, new byte[] { 0x00, 0x00, 0x01, 0x00 })]
    [InlineData(256, new byte[] { 0x00, 0x00, 0x02, 0x00 })]
    [InlineData(16384, new byte[] { 0x00, 0x01, 0x00, 0x00 })]
    [InlineData(0x0FFFFFFF, new byte[] { 0x7F, 0x7F, 0x7F, 0x7F })]
    public void Encode_KnownValues(int value, byte[] expected)
    {
        Span<byte> result = stackalloc byte[4];
        SynchsafeInteger.Encode(value, result);
        Assert.Equal(expected, result.ToArray());
    }

    [Fact]
    public void RoundTrip()
    {
        Span<byte> buf = stackalloc byte[4];
        for (var i = 0; i < 100000; i += 137)
        {
            SynchsafeInteger.Encode(i, buf);
            var decoded = SynchsafeInteger.Decode(buf);
            Assert.Equal(i, decoded);
        }
    }

    [Fact]
    public void Encode_NegativeValue_Throws()
    {
        var buf = new byte[4];
        Assert.Throws<ArgumentOutOfRangeException>(() => SynchsafeInteger.Encode(-1, buf));
    }

    [Fact]
    public void Encode_TooLargeValue_Throws()
    {
        var buf = new byte[4];
        Assert.Throws<ArgumentOutOfRangeException>(() => SynchsafeInteger.Encode(0x10000000, buf));
    }
}
