using System.IO.Compression;

namespace Meziantou.Framework.Compression.Tests;

public sealed class LzmaStreamTests
{
    private static byte[] Decompress(byte[] data, int readSize = 81920)
    {
        using var stream = new LzmaStream(new MemoryStream(data), CompressionMode.Decompress);
        return LzmaCodecTests.ReadAll(stream, readSize);
    }

    private static byte[] Compress(byte[] data, LzmaCompressionOptions options)
    {
        using var output = new MemoryStream();
        using (var stream = new LzmaStream(output, options, leaveOpen: true))
        {
            stream.Write(data);
        }

        return output.ToArray();
    }

    [Fact]
    public void RoundTrip_NonSeekableStreams()
    {
        var data = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "BZip2", "sample.txt"));
        using var output = new MemoryStream();
        using (var stream = new LzmaStream(ZipTestData.CreateNonSeekableWriteStream(output), new LzmaCompressionOptions { Preset = 1 }))
        {
            stream.Write(data);
        }

        using var decompress = new LzmaStream(ZipTestData.CreateForwardOnlyStream(output.ToArray(), 1), CompressionMode.Decompress);
        Assert.Equal(data, LzmaCodecTests.ReadAll(decompress, 1000));
    }

    [Theory]
    [InlineData("lzma-unknown-size.lzma")]
    [InlineData("lzma-known-size.lzma")]
    public void Decompress_Files(string name)
    {
        var expected = LzmaCodecTests.ReadTestData("sample.txt");
        Assert.Equal(expected, Decompress(LzmaCodecTests.ReadTestData(name)));
        Assert.Equal(expected, Decompress(LzmaCodecTests.ReadTestData(name), readSize: 1));
    }

    [Fact]
    public void Decompress_Corrupt()
    {
        Assert.Throws<InvalidDataException>(() => Decompress(LzmaCodecTests.ReadTestData("lzma-corrupt.lzma")));
        Assert.Throws<InvalidDataException>(() => Decompress([0x5D, 0, 0]));
        Assert.Throws<InvalidDataException>(() => Decompress([0xE1, 0, 0, 1, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0]));

        var data = LzmaCodecTests.ReadTestData("lzma-unknown-size.lzma");
        Assert.Throws<InvalidDataException>(() => Decompress(data[..^5]));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(0, 1)]
    [InlineData(1, 1000)]
    [InlineData(6, 100_000)]
    [InlineData(9, 1000)]
    public void RoundTrip(int preset, int size)
    {
        var data = LzmaCodecTests.CreateText(size, seed: size);
        var compressed = Compress(data, new LzmaCompressionOptions { Preset = preset });
        Assert.Equal(data, Decompress(compressed));
    }

    [Fact]
    public void RoundTrip_Options()
    {
        var data = LzmaCodecTests.CreateText(50_000);
        var options = new LzmaCompressionOptions { Preset = 7, Extreme = true, DictionarySize = 100_000, LiteralContextBits = 8, LiteralPositionBits = 4, PositionBits = 0 };
        var compressed = Compress(data, options);
        Assert.Equal((byte)((((0 * 5) + 4) * 9) + 8), compressed[0]);
        Assert.Equal(data, Decompress(compressed, readSize: 1));
    }

    [Fact]
    public async Task Async_RoundTrip()
    {
        var data = LzmaCodecTests.CreateRandom(50_000);
        using var output = new MemoryStream();
        await using (var stream = new LzmaStream(output, CompressionLevel.Fastest, leaveOpen: true))
        {
            await stream.WriteAsync(data);
            await stream.FlushAsync();
        }

        output.Position = 0;
        await using var decompress = new LzmaStream(output, CompressionMode.Decompress);
        var result = new byte[data.Length];
        await decompress.ReadExactlyAsync(result);
        Assert.Equal(data, result);
        Assert.Equal(0, await decompress.ReadAsync(new byte[1]));
    }

    [Fact]
    public void Options_Validation()
    {
        var options = new LzmaCompressionOptions();
        Assert.Throws<ArgumentOutOfRangeException>(() => options.LiteralContextBits = 9);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.LiteralPositionBits = 5);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.PositionBits = -1);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.Preset = 10);
        Assert.Throws<ArgumentOutOfRangeException>(() => options.DictionarySize = (1536 << 20) + 1);
        Assert.Equal(3, options.LiteralContextBits);
        Assert.Equal(0, options.LiteralPositionBits);
        Assert.Equal(2, options.PositionBits);
    }
}
