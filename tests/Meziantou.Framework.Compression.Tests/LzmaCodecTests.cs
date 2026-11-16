using System.Text;
using Meziantou.Framework.Compression.Internals.Filters;
using Meziantou.Framework.Compression.Internals.Lzma;

namespace Meziantou.Framework.Compression.Tests;

public sealed class LzmaCodecTests
{
    public static TheoryData<int, int> PresetsAndSizes
    {
        get
        {
            var data = new TheoryData<int, int>();
            foreach (var preset in new[] { 0, 1, 3, 4, 6, 9 })
            {
                foreach (var size in new[] { 0, 1, 2, 100, 70_000 })
                {
                    data.Add(preset, size);
                }
            }

            return data;
        }
    }

    internal static string GetTestDataPath(string name) => Path.Combine(AppContext.BaseDirectory, "TestData", "Lzma", name);

    internal static byte[] ReadTestData(string name) => File.ReadAllBytes(GetTestDataPath(name));

    internal static byte[] CreateText(int length, int seed = 0)
    {
        string[] words = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", "lambda", "mu"];
        var state = (uint)seed * 2654435761u + 1;
        var builder = new StringBuilder(length + 16);
        while (builder.Length < length)
        {
            builder.Append(words[NextRandom(ref state) % words.Length]).Append(NextRandom(ref state) % 4 == 0 ? '\n' : ' ');
        }

        return Encoding.ASCII.GetBytes(builder.ToString(0, length));
    }

    internal static byte[] CreateRandom(int length, int seed = 0)
    {
        var data = new byte[length];
        var state = (uint)seed * 2654435761u + 1;
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(NextRandom(ref state) >> 13);
        }

        return data;
    }

    /// <summary>A deterministic pseudo-random generator (xorshift32) for the test data.</summary>
    private static uint NextRandom(ref uint state)
    {
        state ^= state << 13;
        state ^= state >> 17;
        state ^= state << 5;
        return state;
    }

    /// <summary>Reads a stream with reads of <paramref name="readSize"/> bytes.</summary>
    internal static byte[] ReadAll(Stream stream, int readSize = 81920)
    {
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static (byte[] Compressed, byte[] Properties) EncodeLzma(byte[] data, LzmaEncoderSettings settings, bool writeEndMarker)
    {
        using var output = new MemoryStream();
        byte[] properties;
        using (var encoder = new LzmaEncoderStream(output, settings, writeEndMarker, leaveOpen: true))
        {
            properties = encoder.Properties;
            encoder.Write(data);
        }

        return (output.ToArray(), properties);
    }

    private static (byte[] Compressed, byte DictionaryProperty) EncodeLzma2(byte[] data, LzmaEncoderSettings settings, int writeSize = int.MaxValue)
    {
        using var output = new MemoryStream();
        byte property;
        using (var encoder = new Lzma2EncoderStream(output, settings, leaveOpen: true))
        {
            property = encoder.DictionaryProperty;
            for (var i = 0; i < data.Length; i += writeSize)
            {
                encoder.Write(data.AsSpan(i, Math.Min(writeSize, data.Length - i)));
            }
        }

        return (output.ToArray(), property);
    }

    private static byte[] DecodeLzma2(byte[] compressed, byte property, int readSize = 81920)
    {
        using var decoder = new Lzma2DecoderStream(new MemoryStream(compressed), property, leaveOpen: false);
        return ReadAll(decoder, readSize);
    }

    [Fact]
    public void Crc64Xz_CheckValue()
    {
        Assert.Equal(0x995DC9BBDF1939FAUL, Crc64Xz.Compute("123456789"u8));

        var crc = Crc64Xz.Create();
        crc.Append("1234"u8);
        crc.Append("56789"u8);
        Assert.Equal(0x995DC9BBDF1939FAUL, crc.GetCurrentHashAsUInt64());
    }

    [Theory]
    [MemberData(nameof(PresetsAndSizes))]
    public void Lzma_RoundTrip(int preset, int size)
    {
        var data = CreateText(size, seed: size);
        var (compressed, properties) = EncodeLzma(data, LzmaEncoderSettings.FromPreset(preset), writeEndMarker: true);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, outputLength: -1, leaveOpen: false);
        Assert.Equal(data, ReadAll(decoder));
    }

    [Theory]
    [MemberData(nameof(PresetsAndSizes))]
    public void Lzma2_RoundTrip(int preset, int size)
    {
        var data = CreateText(size, seed: size);
        var (compressed, property) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(preset));
        Assert.Equal(data, DecodeLzma2(compressed, property));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void Lzma_RoundTrip_ModeAndMatchFinder(bool normalMode, bool binaryTree)
    {
        var mode = normalMode ? LzmaEncoderMode.Normal : LzmaEncoderMode.Fast;
        var matchFinder = binaryTree ? LzmaMatchFinderKind.BinaryTree4 : LzmaMatchFinderKind.HashChain4;
        var data = CreateText(50_000);
        var settings = LzmaEncoderSettings.FromPreset(6);
        settings.Mode = mode;
        settings.MatchFinder = matchFinder;
        settings.NiceLength = 273;
        var (compressed, properties) = EncodeLzma(data, settings, writeEndMarker: true);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, outputLength: -1, leaveOpen: false);
        Assert.Equal(data, ReadAll(decoder));
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(8, 4, 4)]
    [InlineData(0, 4, 0)]
    [InlineData(4, 0, 4)]
    public void Lzma_RoundTrip_LiteralAndPositionBits(int lc, int lp, int pb)
    {
        var data = CreateText(30_000);
        var settings = LzmaEncoderSettings.FromPreset(5);
        settings.LiteralContextBits = lc;
        settings.LiteralPositionBits = lp;
        settings.PositionBits = pb;
        var (compressed, properties) = EncodeLzma(data, settings, writeEndMarker: true);
        Assert.Equal(LzmaConstants.EncodeProperties(lc, lp, pb), properties[0]);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, outputLength: -1, leaveOpen: false);
        Assert.Equal(data, ReadAll(decoder));
    }

    [Fact]
    public void Lzma_KnownLength_WithoutEndMarker()
    {
        var data = CreateText(10_000);
        var (compressed, properties) = EncodeLzma(data, LzmaEncoderSettings.FromPreset(6), writeEndMarker: false);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, data.Length, leaveOpen: false);
        Assert.Equal(data, ReadAll(decoder, readSize: 1));
    }

    [Fact]
    public void Lzma_KnownLength_AcceptsEndMarker()
    {
        var data = CreateText(10_000);
        var (compressed, properties) = EncodeLzma(data, LzmaEncoderSettings.FromPreset(6), writeEndMarker: true);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, data.Length, leaveOpen: false);
        Assert.Equal(data, ReadAll(decoder));
    }

    [Fact]
    public void Lzma_KnownLength_EndMarkerTooEarly()
    {
        var data = CreateText(10_000);
        var (compressed, properties) = EncodeLzma(data, LzmaEncoderSettings.FromPreset(6), writeEndMarker: true);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed), properties, data.Length + 1, leaveOpen: false);
        Assert.Throws<InvalidDataException>(() => ReadAll(decoder));
    }

    [Fact]
    public void Lzma_Truncated()
    {
        var data = CreateText(10_000);
        var (compressed, properties) = EncodeLzma(data, LzmaEncoderSettings.FromPreset(6), writeEndMarker: true);
        using var decoder = new LzmaDecoderStream(new MemoryStream(compressed[..^10]), properties, outputLength: -1, leaveOpen: false);
        Assert.Throws<InvalidDataException>(() => ReadAll(decoder));
    }

    [Fact]
    public void Lzma_InvalidProperties()
    {
        Assert.Throws<InvalidDataException>(() => new LzmaDecoderStream(new MemoryStream(), [225, 0, 0, 1, 0], -1, leaveOpen: false));
        Assert.Throws<InvalidDataException>(() => new LzmaDecoderStream(new MemoryStream(), [93, 0, 0], -1, leaveOpen: false));
    }

    [Fact]
    public void Lzma_PropertiesAvailableAfterConstruction()
    {
        var settings = LzmaEncoderSettings.FromPreset(6);
        using var encoder = new LzmaEncoderStream(new MemoryStream(), settings, writeEndMarker: true, leaveOpen: false);
        Assert.Equal([0x5D, 0x00, 0x00, 0x80, 0x00], encoder.Properties);
    }

    [Fact]
    public void Lzma2_Incompressible()
    {
        var data = CreateRandom(300_000);
        var (compressed, property) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(6));

        // The chunks are stored: the overhead is 3 bytes per 64 KiB, and the end marker
        Assert.InRange(compressed.Length, data.Length, data.Length + 32);
        Assert.Equal(data, DecodeLzma2(compressed, property));
    }

    [Fact]
    public void Lzma2_HighlyRepetitive()
    {
        var data = new byte[5_000_000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i % 7 == 0 ? 'a' : 'b');
        }

        var (compressed, property) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(1));
        Assert.InRange(compressed.Length, 1, 2000);
        Assert.Equal(data, DecodeLzma2(compressed, property));
    }

    [Fact]
    public void Lzma2_DataLargerThanDictionary()
    {
        // A block of random data repeated after more than the dictionary size, and mixed text
        var block = CreateRandom(100_000, seed: 1);
        var text = CreateText(400_000, seed: 2);
        byte[] data = [.. block, .. text, .. block, .. CreateText(300_000, seed: 3), .. block];
        var settings = LzmaEncoderSettings.FromPreset(6);
        settings.DictionarySize = 1 << 16;
        var (compressed, property) = EncodeLzma2(data, settings, writeSize: 4099);
        Assert.Equal(data, DecodeLzma2(compressed, property, readSize: 1000));
    }

    [Fact]
    public void Lzma2_OneByteReadsAndWrites()
    {
        var data = CreateText(20_000);
        var (compressed, property) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(6), writeSize: 1);
        Assert.Equal(data, DecodeLzma2(compressed, property, readSize: 1));
    }

    [Fact]
    public void Lzma2_FlushChunk()
    {
        var data = CreateText(20_000);
        using var output = new MemoryStream();
        var encoder = new Lzma2EncoderStream(output, LzmaEncoderSettings.FromPreset(6), leaveOpen: true);
        encoder.Write(data.AsSpan(0, 10_000));
        encoder.FlushChunk();
        var flushed = output.ToArray();
        encoder.Write(data.AsSpan(10_000));
        encoder.Dispose();

        using (var partial = new Lzma2DecoderStream(new MemoryStream(flushed), encoder.DictionaryProperty, leaveOpen: false))
        {
            var buffer = new byte[10_000];
            partial.ReadExactly(buffer);
            Assert.Equal(data.AsSpan(0, 10_000), buffer);
        }

        Assert.Equal(data, DecodeLzma2(output.ToArray(), encoder.DictionaryProperty));
    }

    [Fact]
    public void Lzma2_LargeDictionaryPropertyOnSmallData()
    {
        var data = CreateText(1000);
        var (compressed, _) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(0));

        // The window grows with the data: a dictionary of 4 GiB does not allocate 4 GiB
        Assert.Equal(data, DecodeLzma2(compressed, 40));
    }

    [Fact]
    public void Lzma2_Corrupt()
    {
        var data = CreateText(20_000);
        var (compressed, property) = EncodeLzma2(data, LzmaEncoderSettings.FromPreset(6));
        Assert.Throws<InvalidDataException>(() => DecodeLzma2(compressed[..^20], property));
        Assert.Throws<InvalidDataException>(() => DecodeLzma2([0x02, 0x00, 0x00, 0x41, 0x00], property));
        Assert.Throws<InvalidDataException>(() => DecodeLzma2([0x80, 0x00, 0x00, 0x00, 0x05, 0, 0, 0, 0, 0, 0], property));
        Assert.Throws<InvalidDataException>(() => DecodeLzma2([0x03], property));
        Assert.Throws<InvalidDataException>(() => DecodeLzma2([0x01, 0x00, 0x00, 0x41, 0x00], 41));

        var corrupt = compressed.ToArray();
        corrupt[10] ^= 0x40;
        Assert.Throws<InvalidDataException>(() => DecodeLzma2(corrupt, property));
    }

    [Fact]
    public void EncoderSettings_Validation()
    {
        var settings = LzmaEncoderSettings.FromPreset(6);
        settings.DictionarySize = 100;
        Assert.Throws<ArgumentOutOfRangeException>(() => new LzmaEncoderStream(new MemoryStream(), settings, writeEndMarker: true, leaveOpen: false));

        settings = LzmaEncoderSettings.FromPreset(6);
        settings.LiteralContextBits = 4;
        settings.LiteralPositionBits = 1;
        Assert.Throws<ArgumentOutOfRangeException>(() => new Lzma2EncoderStream(new MemoryStream(), settings, leaveOpen: false));

        Assert.Throws<ArgumentOutOfRangeException>(() => LzmaEncoderSettings.FromPreset(10));
    }

    [Fact]
    public void Decode_7zLzma()
    {
        var archive = ReadTestData("7z-lzma.7z");
        using var decoder = new LzmaDecoderStream(new MemoryStream(archive, 32, 1382), [0x5D, 0x00, 0x20, 0x00, 0x00], 6191, leaveOpen: false);
        Assert.Equal(ReadTestData("sample.txt"), ReadAll(decoder));
    }

    [Fact]
    public void Decode_7zLzma2()
    {
        var archive = ReadTestData("7z-lzma2.7z");
        using var decoder = new Lzma2DecoderStream(new MemoryStream(archive, 32, 1389), 0x02, leaveOpen: false);
        Assert.Equal(ReadTestData("sample.txt"), ReadAll(decoder, readSize: 1));
    }

    [Fact]
    public void Decode_7zBcj2()
    {
        // The packed streams of the folder: main (LZMA), range coder (raw), call (LZMA), jump (LZMA)
        var archive = ReadTestData("7z-bcj2.7z");
        byte[] properties = [0x5D, 0x00, 0x10, 0x00, 0x00];
        var main = new LzmaDecoderStream(new MemoryStream(archive, 32, 2004), properties, 2396, leaveOpen: false);
        var rangeCoder = new MemoryStream(archive, 2036, 11);
        var call = new LzmaDecoderStream(new MemoryStream(archive, 2047, 15), properties, 12, leaveOpen: false);
        var jump = new LzmaDecoderStream(new MemoryStream(archive, 2062, 25), properties, 24, leaveOpen: false);
        using var decoder = new Bcj2DecoderStream(main, call, jump, rangeCoder, 2432, leaveOpen: false);
        Assert.Equal(ReadTestData("filter-input.bin"), ReadAll(decoder, readSize: 3));
    }

    [Fact]
    public void Bcj2_TruncatedMainStream()
    {
        using var decoder = new Bcj2DecoderStream(new MemoryStream([1, 2, 3]), new MemoryStream(), new MemoryStream(), new MemoryStream([0, 0, 0, 0, 0]), 10, leaveOpen: false);
        Assert.Throws<InvalidDataException>(() => ReadAll(decoder));
    }

    [Fact]
    public void Lzma2DictionaryProperty()
    {
        Assert.Equal(4096u, LzmaConstants.GetLzma2DictionarySize(0));
        Assert.Equal(6144u, LzmaConstants.GetLzma2DictionarySize(1));
        Assert.Equal(uint.MaxValue, LzmaConstants.GetLzma2DictionarySize(40));
        Assert.Equal((byte)22, LzmaConstants.GetLzma2DictionaryProperty(1 << 23));
        Assert.Throws<InvalidDataException>(() => LzmaConstants.GetLzma2DictionarySize(41));
    }
}
