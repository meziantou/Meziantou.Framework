using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression.Tests;

public sealed class LZipStreamTests
{
    private static byte[] Decompress(byte[] data, int readSize = 81920)
    {
        using var stream = new LZipStream(new MemoryStream(data), CompressionMode.Decompress);
        return LzmaCodecTests.ReadAll(stream, readSize);
    }

    private static byte[] Compress(byte[] data, LZipCompressionOptions options, int writeSize = int.MaxValue)
    {
        using var output = new MemoryStream();
        using (var stream = new LZipStream(output, options, leaveOpen: true))
        {
            for (var i = 0; i < data.Length; i += writeSize)
            {
                stream.Write(data, i, Math.Min(writeSize, data.Length - i));
            }
        }

        return output.ToArray();
    }

    [Theory]
    [InlineData("lzip-single.lz")]
    [InlineData("lzip-multimember.lz")]
    public void Decompress_Files(string name)
    {
        var expected = LzmaCodecTests.ReadTestData("sample.txt");
        Assert.Equal(expected, Decompress(LzmaCodecTests.ReadTestData(name)));
        Assert.Equal(expected, Decompress(LzmaCodecTests.ReadTestData(name), readSize: 1));
    }

    [Fact]
    public void Decompress_TrailingDataIsIgnored()
    {
        var data = LzmaCodecTests.ReadTestData("lzip-single.lz");
        Assert.Equal(LzmaCodecTests.ReadTestData("sample.txt"), Decompress([.. data, .. "trailing"u8]));
        Assert.Equal(LzmaCodecTests.ReadTestData("sample.txt"), Decompress([.. data, 0, 0]));
    }

    [Fact]
    public void Decompress_Corrupt()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Decompress(LzmaCodecTests.ReadTestData("lzip-corrupt-crc.lz")));
        Assert.Contains("CRC32", exception.Message, StringComparison.Ordinal);

        var data = LzmaCodecTests.ReadTestData("lzip-single.lz");
        Assert.Throws<InvalidDataException>(() => Decompress([]));
        Assert.Throws<InvalidDataException>(() => Decompress([.. "LZIQ"u8, .. data[4..]]));
        Assert.Throws<InvalidDataException>(() => Decompress(data[..^1]));
        Assert.Throws<InvalidDataException>(() => Decompress([.. data, .. "LZIP"u8]));

        // Version 0, an invalid dictionary size, a wrong data size and a wrong member size
        foreach (var (position, value) in new[] { (4, (byte)0), (5, (byte)0x2C), (5, (byte)0xEC), (data.Length - 16, (byte)1), (data.Length - 8, (byte)1) })
        {
            var corrupt = data.ToArray();
            corrupt[position] = value;
            Assert.Throws<InvalidDataException>(() => Decompress(corrupt));
        }
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(3, 1000)]
    [InlineData(6, 100_000)]
    public void RoundTrip(int preset, int size)
    {
        var data = LzmaCodecTests.CreateText(size, seed: size);
        var compressed = Compress(data, new LZipCompressionOptions { Preset = preset });
        Assert.Equal(data, Decompress(compressed));
    }

    [Fact]
    public void RoundTrip_Members()
    {
        var data = LzmaCodecTests.CreateText(100_000);
        var compressed = Compress(data, new LZipCompressionOptions { Preset = 2, MemberSize = 30_000, DictionarySize = 100_000 }, writeSize: 999);
        Assert.Equal(4, CountMembers(compressed));
        Assert.Equal(data, Decompress(compressed, readSize: 1));

        static int CountMembers(byte[] data)
        {
            var count = 0;
            for (var i = 0; i + 4 <= data.Length; i++)
            {
                if (data.AsSpan(i, 4).SequenceEqual("LZIP"u8))
                {
                    count++;
                }
            }

            return count;
        }
    }

    [Fact]
    public void DictionarySize_Coding()
    {
        Assert.Equal(1u << 12, LZipReader.DecodeDictionarySize(12));
        Assert.Equal(1u << 29, LZipReader.DecodeDictionarySize(29));
        Assert.Equal((1u << 20) - (1u << 16), LZipReader.DecodeDictionarySize(20 | (1 << 5)));
        Assert.Equal(0u, LZipReader.DecodeDictionarySize(30));
        Assert.Equal(0u, LZipReader.DecodeDictionarySize(12 | (1 << 5)));
        Assert.Equal((byte)20, LZipReader.EncodeDictionarySize(1 << 20));
        Assert.Equal((byte)(20 | (1 << 5)), LZipReader.EncodeDictionarySize((1 << 20) - (1 << 16)));
        Assert.Equal((byte)(20 | (2 << 5)), LZipReader.EncodeDictionarySize(900_000));

        var options = new LZipCompressionOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DictionarySize = 4095);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DictionarySize = (1 << 29) + 1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.MemberSize = -1);
    }

    [Fact]
    public async Task Async_RoundTrip()
    {
        var data = LzmaCodecTests.CreateText(20_000);
        using var output = new MemoryStream();
        await using (var stream = new LZipStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            await stream.WriteAsync(data);
        }

        output.Position = 0;
        await using var decompress = new LZipStream(output, CompressionMode.Decompress);
        using var result = new MemoryStream();
        await decompress.CopyToAsync(result);
        Assert.Equal(data, result.ToArray());
    }
}
