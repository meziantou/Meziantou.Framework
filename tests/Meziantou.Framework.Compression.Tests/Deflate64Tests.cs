using System.IO.Compression;
using System.IO.Hashing;
using System.Text;
using Meziantou.Framework.Compression.Internals.Deflate64;

namespace Meziantou.Framework.Compression.Tests;

public sealed class Deflate64Tests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "ZipLegacy", fileName);

    private static byte[] ReadAll(Stream stream, int readSize = 81920)
    {
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static byte[] Decode(byte[] data, bool deflate64, int readSize = 81920)
    {
        using var decoder = new Deflate64DecoderStream(new MemoryStream(data), deflate64, leaveOpen: false);
        return ReadAll(decoder, readSize);
    }

    private static byte[] CompressWithDeflateStream(byte[] data, CompressionLevel level)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, level, leaveOpen: true))
        {
            deflate.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] CreateText(int length)
    {
        var builder = new StringBuilder();
        var random = new BZip2StreamTests.PseudoRandom(12);
        string[] words = ["deflate", "window", "huffman", "literal", "length", "distance", "block", "stored", "fixed", "dynamic"];
        while (builder.Length < length)
        {
            builder.Append(words[random.Next(words.Length)]).Append(random.Next(10) is 0 ? '\n' : ' ');
        }

        return Encoding.ASCII.GetBytes(builder.ToString(0, length));
    }

    private static byte[] CreateRandomData(int length)
    {
        var data = new byte[length];
        new BZip2StreamTests.PseudoRandom(length).NextBytes(data);
        return data;
    }

    [Theory]
    [InlineData("deflate64-text.zip")]
    [InlineData("deflate64-far.zip")]
    public void Decompress_7ZipDeflate64(string fileName)
    {
        var entries = ZipLegacyMethodsTests.ReadLocalEntries(GetTestDataPath(fileName));
        Assert.HasCount(1, entries);
        var entry = entries[0];
        Assert.Equal(9, entry.Method);

        var output = Decode(entry.Data, deflate64: true);
        Assert.HasCount(entry.UncompressedSize, output);
        Assert.Equal(entry.Crc, Crc32.HashToUInt32(output));
        Assert.Equal(output, Decode(entry.Data, deflate64: true, readSize: 1));
    }

    [Fact]
    public void Decompress_7ZipDeflate64_Text()
    {
        var entry = ZipLegacyMethodsTests.ReadLocalEntries(GetTestDataPath("deflate64-text.zip"))[0];
        var expected = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "TestData", "BZip2", "sample.txt"));
        Assert.Equal(expected, Decode(entry.Data, deflate64: true));
    }

    [Theory]
    [InlineData(true, 1001)]
    [InlineData(false, 259)]
    public void Decompress_LengthCode285(bool deflate64, int expectedLength)
    {
        // A fixed Huffman block: the literal 'a', the length code 285 (with 16 extra bits of 997 in Deflate64, and the
        // length 258 in Deflate), the distance code 0 (distance 1), the end of the block
        var writer = new ZipLegacyMethodsTests.LsbBitWriter();
        writer.Write(1, 1);
        writer.Write(2, 1);
        writer.WriteCode(8, 0x30 + 'a');
        writer.WriteCode(8, 0xC0 + 5);
        if (deflate64)
        {
            writer.Write(16, 997);
        }

        writer.WriteCode(5, 0);
        writer.WriteCode(7, 0);

        var output = Decode(writer.ToArray(), deflate64);
        Assert.Equal(Enumerable.Repeat((byte)'a', expectedLength).ToArray(), output);
    }

    [Fact]
    public void Decompress_DistanceCode30()
    {
        // A fixed Huffman block: 'a', a copy of 3 bytes at distance 1 (the output is 4 bytes long), then the distance code
        // 30 (32769 + 14 bits) refers to a distance before the start: an error in Deflate64, an invalid code in Deflate
        var writer = new ZipLegacyMethodsTests.LsbBitWriter();
        writer.Write(1, 1);
        writer.Write(2, 1);
        writer.WriteCode(8, 0x30 + 'a');
        writer.WriteCode(7, 1);
        writer.WriteCode(5, 0);
        writer.WriteCode(7, 1);
        writer.WriteCode(5, 30);
        writer.Write(14, 0);
        writer.WriteCode(7, 0);

        Assert.Throws<InvalidDataException>(() => Decode(writer.ToArray(), deflate64: true));
        Assert.Throws<InvalidDataException>(() => Decode(writer.ToArray(), deflate64: false));
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    [InlineData(CompressionLevel.NoCompression)]
    public void Deflate_EqualsDeflateStream(CompressionLevel level)
    {
        foreach (var data in (byte[][])[CreateText(300_000), CreateRandomData(200_000), [42]])
        {
            var compressed = CompressWithDeflateStream(data, level);
            using var reference = new DeflateStream(new MemoryStream(compressed), CompressionMode.Decompress);
            var expected = ReadAll(reference);
            Assert.Equal(data, expected);
            Assert.Equal(expected, Decode(compressed, deflate64: false));
            Assert.Equal(expected, Decode(compressed, deflate64: false, readSize: 1));
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(81920)]
    public void GetUnconsumedInput_ReturnsTheTrailingBytes(int inputReadSize)
    {
        var text = CreateText(100_000);
        var trailer = "PK\u0007\u0008 data descriptor and what follows"u8.ToArray();
        byte[] data = [.. CompressWithDeflateStream(text, CompressionLevel.Optimal), .. trailer];
        var input = new ChunkedReadStream(data, inputReadSize);
        using var decoder = new Deflate64DecoderStream(input, deflate64: false, leaveOpen: true);
        Assert.False(decoder.IsCompleted);
        Assert.Equal(text, ReadAll(decoder));
        Assert.True(decoder.IsCompleted);

        // The bytes the decoder read after the deflate data, then the bytes it did not read
        byte[] rest = [.. decoder.GetUnconsumedInput().Span, .. ReadAll(input)];
        Assert.Equal(trailer, rest);
    }

    [Fact]
    public void GetUnconsumedInput_BeforeTheEnd_Throws()
    {
        using var decoder = new Deflate64DecoderStream(new MemoryStream(CompressWithDeflateStream(CreateText(1000), CompressionLevel.Optimal)), deflate64: false, leaveOpen: false);
        Assert.Throws<InvalidOperationException>(() => decoder.GetUnconsumedInput());
    }

    [Fact]
    public void Decompress_Truncated_Throws()
    {
        var compressed = CompressWithDeflateStream(CreateText(10_000), CompressionLevel.Optimal);
        Assert.Throws<InvalidDataException>(() => Decode(compressed[..^10], deflate64: false));
        Assert.Throws<InvalidDataException>(() => Decode([], deflate64: true));
    }

    [Fact]
    public void Decompress_InvalidBlockType_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Decode([0x07], deflate64: true));
    }

    [Fact]
    public void Decompress_StoredBlock_InvalidLength_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Decode([0x01, 0x05, 0x00, 0x00, 0x00], deflate64: true));
    }

    /// <summary>A stream whose reads return at most a number of bytes.</summary>
    private sealed class ChunkedReadStream(byte[] data, int chunkSize) : MemoryStream(data, writable: false)
    {
        public override int Read(Span<byte> buffer) => base.Read(buffer[..Math.Min(buffer.Length, chunkSize)]);

        public override int Read(byte[] buffer, int offset, int count) => base.Read(buffer, offset, Math.Min(count, chunkSize));
    }
}
