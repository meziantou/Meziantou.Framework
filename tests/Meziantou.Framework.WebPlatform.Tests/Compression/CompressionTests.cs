using System.IO.Hashing;
using Meziantou.Framework.WebPlatform.Compression;

namespace Meziantou.Framework.WebPlatform.Tests.Compression;

// The compression contexts of CompressionStream and DecompressionStream (https://compression.spec.whatwg.org/)
public sealed class CompressionTests
{
    private static readonly byte[] Text = System.Text.Encoding.UTF8.GetBytes(string.Concat(Enumerable.Range(0, 5000).Select(i => $"{i % 97},")));

    // "expected output", from WPT compression/resources/decompression-input.js
    private static readonly byte[] Deflate = [120, 156, 75, 173, 40, 72, 77, 46, 73, 77, 81, 200, 47, 45, 41, 40, 45, 1, 0, 48, 173, 6, 36];
    private static readonly byte[] Gzip = [31, 139, 8, 0, 0, 0, 0, 0, 0, 3, 75, 173, 40, 72, 77, 46, 73, 77, 81, 200, 47, 45, 41, 40, 45, 1, 0, 176, 1, 57, 179, 15, 0, 0, 0];

    public static TheoryData<string> Formats => ["brotli", "deflate", "deflate-raw", "gzip"];

    [Theory]
    [MemberData(nameof(Formats))]
    public void RoundTrip_ByteByByte(string formatName)
    {
        var format = CompressionFormats.Parse(formatName)!.Value;
        var compressed = Compress(format, Text);
        using var decompressor = Decompressor.Create(format);
        var output = new List<byte>();
        foreach (var value in compressed)
        {
            output.AddRange(decompressor.Decompress([value]));
            Assert.False(decompressor.HasTrailingInput);
        }

        output.AddRange(decompressor.Finish());
        Assert.Equal(Text, output);
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void RoundTrip_EmptyInput(string formatName)
    {
        var format = CompressionFormats.Parse(formatName)!.Value;
        using var decompressor = Decompressor.Create(format);
        Assert.Empty(decompressor.Decompress(Compress(format, [])));
        Assert.Empty(decompressor.Finish());
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Decompress_Truncated_Throws(string formatName)
    {
        var format = CompressionFormats.Parse(formatName)!.Value;
        var compressed = Compress(format, Text);
        using var decompressor = Decompressor.Create(format);
        decompressor.Decompress(compressed.AsSpan(0, compressed.Length - 1));
        Assert.Throws<InvalidDataException>(() => decompressor.Finish());
    }

    [Theory]
    [MemberData(nameof(Formats))]
    public void Decompress_TrailingInput(string formatName)
    {
        var format = CompressionFormats.Parse(formatName)!.Value;
        using var decompressor = Decompressor.Create(format);
        Assert.Equal(Text, decompressor.Decompress([.. Compress(format, Text), 0]));
        Assert.True(decompressor.HasTrailingInput);

        using var decompressor2 = Decompressor.Create(format);
        decompressor2.Decompress(Compress(format, Text));
        Assert.False(decompressor2.HasTrailingInput);
        decompressor2.Decompress([1]);
        Assert.True(decompressor2.HasTrailingInput);
    }

    [Fact]
    public void Inflate_StoredAndBackReferences()
    {
        // A stored block "abc", then a fixed block with a back-reference that overlaps its output (length 6, distance 3)
        byte[] data = [0x00, 0x03, 0x00, 0xFC, 0xFF, (byte)'a', (byte)'b', (byte)'c', 0x83, 0x20, 0x00];
        using var decompressor = Decompressor.Create(CompressionFormat.DeflateRaw);
        Assert.Equal("abcabcabc"u8.ToArray(), decompressor.Decompress(data));
        Assert.True(decompressor.IsEnded);
    }

    [Fact]
    public void Inflate_DistanceTooFarBack_Throws()
    {
        // A fixed block whose first symbol is a back-reference (length 3, distance 1)
        using var decompressor = Decompressor.Create(CompressionFormat.DeflateRaw);
        Assert.Throws<InvalidDataException>(() => decompressor.Decompress([0x03, 0x02, 0x00]));
    }

    [Fact]
    public void ZLib_PresetDictionary_Throws()
    {
        using var decompressor = Decompressor.Create(CompressionFormat.Deflate);
        Assert.Throws<InvalidDataException>(() => decompressor.Decompress([0x78, 0xBB, 0x74, 0xEE, 0x09, 0x59]));
    }

    [Fact]
    public void ZLib_IncorrectChecksum_Throws()
    {
        var data = Deflate.ToArray();
        data[^1] ^= 1;
        Assert.Throws<InvalidDataException>(() => Decompressor.Create(CompressionFormat.Deflate).Decompress(data));
    }

    [Fact]
    public void Gzip_HeaderFields()
    {
        // FEXTRA, FNAME, FCOMMENT and a correct FHCRC
        byte[] header = [31, 139, 8, 0x1E, 0, 0, 0, 0, 0, 3, 2, 0, (byte)'x', (byte)'y', (byte)'a', 0, (byte)'c', 0];
        var crc = Crc32.HashToUInt32(header);
        byte[] data = [.. header, (byte)crc, (byte)(crc >> 8), .. Gzip.AsSpan(10)];

        using var decompressor = Decompressor.Create(CompressionFormat.Gzip);
        Assert.Equal("expected output"u8.ToArray(), decompressor.Decompress(data));
        Assert.True(decompressor.IsEnded);

        data[header.Length] ^= 1;
        Assert.Throws<InvalidDataException>(() => Decompressor.Create(CompressionFormat.Gzip).Decompress(data));
    }

    [Fact]
    public void Gzip_SecondMember_IsTrailingInput()
    {
        using var decompressor = Decompressor.Create(CompressionFormat.Gzip);
        Assert.Equal("expected output"u8.ToArray(), decompressor.Decompress([.. Gzip, .. Gzip]));
        Assert.True(decompressor.HasTrailingInput);
    }

    [Fact]
    public void Brotli_Invalid_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Decompressor.Create(CompressionFormat.Brotli).Decompress([0xFF, 0xFF, 0xFF, 0xFF]));
    }

    private static byte[] Compress(CompressionFormat format, ReadOnlySpan<byte> input)
    {
        using var compressor = new Compressor(format);
        return [.. compressor.Compress(input), .. compressor.Finish()];
    }
}
