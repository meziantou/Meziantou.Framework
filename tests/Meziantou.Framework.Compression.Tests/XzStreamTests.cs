using System.IO.Compression;

namespace Meziantou.Framework.Compression.Tests;

public sealed class XzStreamTests
{
    public static TheoryData<int, int, XzCheck> RoundTripCases
    {
        get
        {
            var data = new TheoryData<int, int, XzCheck>();
            foreach (var preset in new[] { 0, 3, 6, 9 })
            {
                foreach (var size in new[] { 0, 1, 1000, 100_000 })
                {
                    data.Add(preset, size, (XzCheck)((preset + size) % 4));
                }
            }

            return data;
        }
    }

    private static byte[] Decompress(byte[] data, int readSize = 81920)
    {
        using var stream = new XzStream(new MemoryStream(data), CompressionMode.Decompress);
        return LzmaCodecTests.ReadAll(stream, readSize);
    }

    private static byte[] Compress(byte[] data, XzCompressionOptions options, int writeSize = int.MaxValue)
    {
        using var output = new MemoryStream();
        using (var stream = new XzStream(output, options, leaveOpen: true))
        {
            for (var i = 0; i < data.Length; i += writeSize)
            {
                stream.Write(data, i, Math.Min(writeSize, data.Length - i));
            }
        }

        return output.ToArray();
    }

    [Theory]
    [InlineData("xz-check-none.xz")]
    [InlineData("xz-check-crc32.xz")]
    [InlineData("xz-check-crc64.xz")]
    [InlineData("xz-check-sha256.xz")]
    [InlineData("xz-multiblock.xz")]
    [InlineData("xz-concatenated-padding.xz")]
    public void Decompress_Files(string name)
    {
        var expected = LzmaCodecTests.ReadTestData("sample.txt");
        var data = LzmaCodecTests.ReadTestData(name);
        Assert.Equal(expected, Decompress(data));
        Assert.Equal(expected, Decompress(data, readSize: 1));
    }

    [Fact]
    public void Decompress_Empty()
    {
        Assert.Empty(Decompress(LzmaCodecTests.ReadTestData("xz-empty.xz")));
    }

    [Fact]
    public void Decompress_CorruptCheck()
    {
        var exception = Assert.Throws<InvalidDataException>(() => Decompress(LzmaCodecTests.ReadTestData("xz-corrupt-check.xz")));
        Assert.Contains("check", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Decompress_Truncated()
    {
        var data = LzmaCodecTests.ReadTestData("xz-check-crc64.xz");
        foreach (var length in new[] { 0, 5, 12, 20, 100, data.Length - 13, data.Length - 1 })
        {
            Assert.Throws<InvalidDataException>(() => Decompress(data[..length]));
        }
    }

    [Fact]
    public void Decompress_InvalidData()
    {
        var data = LzmaCodecTests.ReadTestData("xz-check-crc64.xz");

        // Not the magic bytes
        Assert.Throws<InvalidDataException>(() => Decompress([.. "not xz data"u8]));

        // Stream padding that is not a multiple of 4 bytes, and data after the stream
        Assert.Throws<InvalidDataException>(() => Decompress([.. data, 0, 0]));
        Assert.Throws<InvalidDataException>(() => Decompress([.. data, 1, 2, 3, 4]));

        // Each byte of the headers, the index and the footer is protected
        foreach (var position in new[] { 7, 8, 13, 14, data.Length - 30, data.Length - 10, data.Length - 3 })
        {
            var corrupt = data.ToArray();
            corrupt[position] ^= 0x10;
            Assert.Throws<InvalidDataException>(() => Decompress(corrupt));
        }
    }

    [Theory]
    [MemberData(nameof(RoundTripCases))]
    public void RoundTrip(int preset, int size, XzCheck check)
    {
        var data = LzmaCodecTests.CreateText(size, seed: size);
        var compressed = Compress(data, new XzCompressionOptions { Preset = preset, Check = check });
        Assert.Equal(data, Decompress(compressed));
    }

    [Fact]
    public void RoundTrip_Blocks()
    {
        var data = LzmaCodecTests.CreateText(100_000);
        var compressed = Compress(data, new XzCompressionOptions { Preset = 1, BlockSize = 30_000, Check = XzCheck.Sha256 }, writeSize: 7777);
        Assert.Equal(data, Decompress(compressed, readSize: 1));
    }

    [Fact]
    public void RoundTrip_ExtremeAndDictionarySize()
    {
        var data = LzmaCodecTests.CreateText(100_000);
        var compressed = Compress(data, new XzCompressionOptions { Preset = 9, Extreme = true, DictionarySize = 1 << 16 });
        Assert.Equal(data, Decompress(compressed));
    }

    [Fact]
    public void RoundTrip_Incompressible()
    {
        var data = LzmaCodecTests.CreateRandom(200_000);
        var compressed = Compress(data, new XzCompressionOptions());
        Assert.InRange(compressed.Length, data.Length, data.Length + 100);
        Assert.Equal(data, Decompress(compressed));
    }

    [Fact]
    public void CompressionLevel_RoundTrip()
    {
        var data = LzmaCodecTests.CreateText(10_000);
        foreach (var level in Enum.GetValues<CompressionLevel>())
        {
            using var output = new MemoryStream();
            using (var stream = new XzStream(output, level, leaveOpen: true))
            {
                stream.Write(data);
            }

            Assert.Equal(data, Decompress(output.ToArray()));
        }
    }

    [Fact]
    public async Task Async_RoundTrip()
    {
        var data = LzmaCodecTests.CreateText(100_000);
        using var output = new MemoryStream();
        await using (var stream = new XzStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            await stream.WriteAsync(data.AsMemory(0, 50_000));
            await stream.WriteAsync(data.AsMemory(50_000));
        }

        output.Position = 0;
        await using var decompress = new XzStream(output, CompressionMode.Decompress);
        using var result = new MemoryStream();
        await decompress.CopyToAsync(result);
        Assert.Equal(data, result.ToArray());
    }

    [Fact]
    public void Flush_MakesTheDataDecodable()
    {
        var data = LzmaCodecTests.CreateText(10_000);
        using var output = new MemoryStream();
        using var stream = new XzStream(output, CompressionMode.Compress, leaveOpen: true);
        stream.Write(data);
        stream.Flush();

        using var decompress = new XzStream(new MemoryStream(output.ToArray()), CompressionMode.Decompress);
        var buffer = new byte[data.Length];
        decompress.ReadExactly(buffer);
        Assert.Equal(data, buffer);
    }

    [Fact]
    public void StreamProperties()
    {
        using var output = new MemoryStream();
        using (var stream = new XzStream(output, CompressionMode.Compress, leaveOpen: true))
        {
            Assert.Same(output, stream.BaseStream);
            Assert.True(stream.CanWrite);
            Assert.False(stream.CanRead);
            Assert.False(stream.CanSeek);
            Assert.Throws<NotSupportedException>(() => stream.Read(new byte[1]));
            Assert.Throws<NotSupportedException>(() => stream.Length);
        }

        Assert.True(output.CanRead);
        using var decompress = new XzStream(new MemoryStream(output.ToArray()), CompressionMode.Decompress);
        Assert.True(decompress.CanRead);
        Assert.False(decompress.CanWrite);
        Assert.Throws<NotSupportedException>(() => decompress.Write(new byte[1]));
        Assert.Equal(-1, decompress.ReadByte());
    }

    [Fact]
    public void LeaveOpen()
    {
        var output = new MemoryStream();
        new XzStream(output, CompressionMode.Compress).Dispose();
        Assert.False(output.CanRead);
    }

    [Fact]
    public void Options_Validation()
    {
        var options = new XzCompressionOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Preset = 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Preset = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DictionarySize = 4095);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.BlockSize = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Check = (XzCheck)42);
        Assert.Equal(6, options.Preset);
        Assert.Equal(XzCheck.Crc64, options.Check);
        Assert.Throws<ArgumentNullException>(() => new XzStream(new MemoryStream(), (XzCompressionOptions)null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => new XzStream(new MemoryStream(), (CompressionMode)42));
    }

    [Fact]
    public void Compress_Example()
    {
        #region XzStream_RoundTrip
        var data = "Hello, XZ! Hello, XZ! Hello, XZ!"u8.ToArray();

        using var compressed = new MemoryStream();
        using (var xz = new XzStream(compressed, new XzCompressionOptions { Preset = 9, Check = XzCheck.Sha256 }, leaveOpen: true))
        {
            xz.Write(data);
        }

        compressed.Position = 0;
        using var decompress = new XzStream(compressed, CompressionMode.Decompress);
        using var result = new MemoryStream();
        decompress.CopyTo(result);
        Assert.Equal(data, result.ToArray());
        #endregion
    }
}
