using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Filters;
using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression.Tests;

public sealed class BranchFilterTests
{
    public static TheoryData<string> Kinds => [.. Enum.GetNames<BranchFilterKind>()];

    /// <summary>Decodes the LZMA2 data of the first block of an XZ file made by xz: the data before the decoding of the other filters.</summary>
    private static byte[] GetFilteredData(byte[] xz)
    {
        var headerSize = (xz[12] + 1) * 4;
        var filterCount = (xz[13] & 3) + 1;
        var position = 14;
        byte dictionaryProperty = 0;
        for (var i = 0; i < filterCount; i++)
        {
            var id = xz[position++];
            var propertiesSize = xz[position++];
            if (id == 0x21)
            {
                dictionaryProperty = xz[position];
            }

            position += propertiesSize;
        }

        using var decoder = new Lzma2DecoderStream(new MemoryStream(xz, 12 + headerSize, xz.Length - 12 - headerSize), dictionaryProperty, leaveOpen: false);
        return LzmaCodecTests.ReadAll(decoder);
    }

    private static byte[] Encode(byte[] data, BranchFilterKind kind, uint startOffset, int writeSize)
    {
        using var output = new MemoryStream();
        using (var encoder = new BranchFilterEncoderStream(output, kind, startOffset, leaveOpen: true))
        {
            for (var i = 0; i < data.Length; i += writeSize)
            {
                encoder.Write(data.AsSpan(i, Math.Min(writeSize, data.Length - i)));
            }
        }

        return output.ToArray();
    }

    private static byte[] Decode(byte[] data, BranchFilterKind kind, uint startOffset, int readSize)
    {
        using var decoder = new BranchFilterDecoderStream(new MemoryStream(data), kind, startOffset, leaveOpen: false);
        return LzmaCodecTests.ReadAll(decoder, readSize);
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void DecodeXzFile(string kindName)
    {
        var name = kindName.ToLowerInvariant();
        using var stream = new XzStream(File.OpenRead(LzmaCodecTests.GetTestDataPath($"xz-filter-{name}.xz")), CompressionMode.Decompress);
        Assert.Equal(LzmaCodecTests.ReadTestData("filter-input.bin"), LzmaCodecTests.ReadAll(stream, readSize: 1));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void EncoderMatchesXz(string kindName)
    {
        var kind = Enum.Parse<BranchFilterKind>(kindName);
        var name = kindName.ToLowerInvariant();
        var input = LzmaCodecTests.ReadTestData("filter-input.bin");
        var expected = GetFilteredData(LzmaCodecTests.ReadTestData($"xz-filter-{name}.xz"));

        // The filter converts instructions: the test data contains some for each architecture
        Assert.NotEqual(input, expected);
        Assert.Equal(expected, Encode(input, kind, startOffset: 0, writeSize: 7));
        Assert.Equal(input, Decode(expected, kind, startOffset: 0, readSize: 5));
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RoundTrip(string kindName)
    {
        var kind = Enum.Parse<BranchFilterKind>(kindName);
        byte[] data = [.. LzmaCodecTests.ReadTestData("filter-input.bin"), .. LzmaCodecTests.CreateRandom(200_000, seed: (int)kind)];
        foreach (var startOffset in new uint[] { 0, 16, 0xFFFF_FFF0 })
        {
            var encoded = Encode(data, kind, startOffset, writeSize: 100_003);
            Assert.Equal(encoded, Encode(data, kind, startOffset, writeSize: 1));
            Assert.Equal(data, Decode(encoded, kind, startOffset, readSize: 81920));
            Assert.Equal(data, Decode(encoded, kind, startOffset, readSize: 1));
        }
    }

    [Theory]
    [MemberData(nameof(Kinds))]
    public void RoundTrip_ShortData(string kindName)
    {
        var kind = Enum.Parse<BranchFilterKind>(kindName);
        for (var length = 0; length < 20; length++)
        {
            var data = Enumerable.Repeat((byte)0xE8, length).ToArray();
            Assert.Equal(data, Decode(Encode(data, kind, 0, writeSize: 3), kind, 0, readSize: 2));
        }
    }

    [Fact]
    public void Delta_DecodeXzFile()
    {
        using var stream = new XzStream(File.OpenRead(LzmaCodecTests.GetTestDataPath("xz-filter-delta.xz")), CompressionMode.Decompress);
        Assert.Equal(LzmaCodecTests.ReadTestData("filter-input.bin"), LzmaCodecTests.ReadAll(stream));
    }

    [Fact]
    public void Delta_InvalidDistance()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeltaDecoderStream(new MemoryStream(), 0, leaveOpen: false));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DeltaDecoderStream(new MemoryStream(), 257, leaveOpen: false));
    }
}
