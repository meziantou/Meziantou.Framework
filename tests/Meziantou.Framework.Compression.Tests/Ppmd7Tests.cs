using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Tests;

public sealed class Ppmd7Tests
{
    public static TheoryData<int, uint, string> RoundTripData => new()
    {
        { 2, 1 << 11, "empty" },
        { 2, 1 << 11, "byte" },
        { 2, 1 << 11, "text" },
        { 6, 1 << 16, "text" },
        { 6, 1 << 24, "text" },
        { 16, 1 << 20, "text" },
        { 64, 1 << 20, "text" },
        { 6, 1 << 16, "random" },
        { 32, 1 << 20, "random" },
        { 4, 1 << 16, "zeros" },
        { 64, 1 << 16, "zeros" },
        { 8, 1 << 16, "two-letters" },
        { 64, 1 << 11, "two-letters" },
    };

    [Theory]
    [MemberData(nameof(RoundTripData))]
    public void RoundTrip(int order, uint memorySize, string kind)
    {
        var data = CreateData(kind);
        var packed = Encode(data, order, memorySize);
        Assert.Equal(data, Decode(packed, order, memorySize, data.Length));
    }

    [Theory]
    [InlineData(12, 1u << 16)]
    [InlineData(32, 1u << 16)]
    [InlineData(64, 1u << 11)]
    public void RoundTrip_MemoryFull_RestartsTheModel(int order, uint memorySize)
    {
        var data = CreateData("two-letters");
        var model = new Ppmd7Model(memorySize);
        model.Init(order);
        var rangeEncoder = default(Ppmd7zRangeEncoder);
        using var output = new MemoryStream();
        rangeEncoder.Initialize(output, new byte[256]);
        foreach (var value in data)
        {
            model.EncodeSymbol(ref rangeEncoder, value);
        }

        rangeEncoder.FlushData();
        rangeEncoder.FlushBuffer();

        Assert.True(model.RestartCount > 0);
        Assert.Equal(output.ToArray(), Encode(data, order, memorySize));
        Assert.Equal(data, Decode(output.ToArray(), order, memorySize, data.Length));
    }

    [Theory]
    [InlineData("text-o6.ppmd7", "text")]
    [InlineData("text-o2.ppmd7", "text")]
    [InlineData("ab-o32-restart.ppmd7", "two-letters")]
    public void Decode_7Zip(string fileName, string kind)
    {
        var (order, memorySize, packed) = ReadFixture(fileName);
        var expected = CreateData(kind);
        Assert.Equal(expected, Decode(packed, order, memorySize, expected.Length));
    }

    [Theory]
    [InlineData("text-o6.ppmd7", "text")]
    [InlineData("text-o2.ppmd7", "text")]
    [InlineData("ab-o32-restart.ppmd7", "two-letters")]
    public void Encode_SameAs7Zip(string fileName, string kind)
    {
        var (order, memorySize, packed) = ReadFixture(fileName);
        Assert.Equal(packed, Encode(CreateData(kind), order, memorySize));
    }

    [Fact]
    public void Decode_UnknownLength_StopsAtTheEndMarker()
    {
        var data = CreateData("text");
        var model = new Ppmd7Model(1 << 16);
        model.Init(6);
        var rangeEncoder = default(Ppmd7zRangeEncoder);
        using var output = new MemoryStream();
        rangeEncoder.Initialize(output, new byte[4096]);
        foreach (var value in data)
        {
            model.EncodeSymbol(ref rangeEncoder, value);
        }

        model.EncodeSymbol(ref rangeEncoder, -1);
        rangeEncoder.FlushData();
        rangeEncoder.FlushBuffer();

        Assert.Equal(data, Decode(output.ToArray(), 6, 1 << 16, outputLength: -1));
        Assert.Throws<InvalidDataException>(() => Decode(output.ToArray(), 6, 1 << 16, data.Length + 1));
    }

    [Fact]
    public void Decode_SmallReads()
    {
        var data = CreateData("text");
        var packed = Encode(data, 6, 1 << 16);
        using var decoder = new Ppmd7DecoderStream(new MemoryStream(packed), 6, 1 << 16, data.Length, maxMemoryUsage: 1 << 20, leaveOpen: false);
        var result = new List<byte>();
        var buffer = new byte[7];
        int read;
        while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read));
        }

        Assert.Equal(data, result.ToArray());
        Assert.Equal(-1, decoder.ReadByte());
    }

    [Theory]
    [InlineData(1, 1u << 16)]
    [InlineData(65, 1u << 16)]
    [InlineData(6, 2047u)]
    [InlineData(6, 0xFFFFFFFFu)]
    public void Decode_InvalidParameters(int order, uint memorySize)
    {
        Assert.Throws<InvalidDataException>(() => new Ppmd7DecoderStream(new MemoryStream(), order, memorySize, 0, long.MaxValue, leaveOpen: false));
    }

    [Fact]
    public void Decode_MemoryLargerThanMaxMemoryUsage()
    {
        Assert.Throws<InvalidDataException>(() => new Ppmd7DecoderStream(new MemoryStream(), 6, 1 << 20, 0, maxMemoryUsage: (1 << 20) - 1, leaveOpen: false));
    }

    [Fact]
    public void Decode_FirstByteNotZero()
    {
        var packed = Encode(CreateData("text"), 6, 1 << 16);
        packed[0] = 1;
        Assert.Throws<InvalidDataException>(() => Decode(packed, 6, 1 << 16, 4000));
    }

    [Fact]
    public void Decode_Truncated()
    {
        var data = CreateData("text");
        var packed = Encode(data, 6, 1 << 16);
        Assert.Throws<InvalidDataException>(() => Decode(packed.AsSpan(0, packed.Length / 2).ToArray(), 6, 1 << 16, data.Length));
    }

    [Fact]
    public void Decode_AfterAnError_KeepsFailing()
    {
        var data = CreateData("text");
        var packed = Encode(data, 6, 1 << 16);
        using var decoder = new Ppmd7DecoderStream(new MemoryStream(packed[..100]), 6, 1 << 16, data.Length, maxMemoryUsage: 1 << 20, leaveOpen: false);
        var buffer = new byte[data.Length];
        Assert.Throws<InvalidDataException>(() => decoder.ReadExactly(buffer));
        Assert.Throws<InvalidDataException>(() => decoder.Read(buffer));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Decode_Garbage_ThrowsInvalidDataOrDecodes(int seed)
    {
        var garbage = CreateRandomData(2000, seed);
        garbage[0] = 0;
        try
        {
            Decode(garbage, 2 + seed, 1 << 16, outputLength: -1);
        }
        catch (InvalidDataException)
        {
        }
    }

    [Theory]
    [InlineData(-1, 6, 1u << 24)]
    [InlineData(0, 3, 1u << 19)]
    [InlineData(5, 6, 1u << 24)]
    [InlineData(7, 16, 1u << 26)]
    [InlineData(9, 32, 1u << 28)]
    [InlineData(12, 32, 1u << 28)]
    public void GetDefaultParameters(int level, int expectedOrder, uint expectedMemorySize)
    {
        Assert.Equal((expectedOrder, expectedMemorySize), Ppmd7EncoderStream.GetDefaultParameters(level));
    }

    internal static byte[] CreateData(string kind)
    {
        return kind switch
        {
            "empty" => [],
            "byte" => [42],
            "text" => File.ReadAllBytes(GetTestDataPath("text.bin")),
            "random" => CreateRandomData(30_000, seed: 1),
            "zeros" => new byte[20_000],
            "two-letters" => CreateTwoLetterData(20_000, seed: 3),
            _ => throw new ArgumentOutOfRangeException(nameof(kind)),
        };
    }

    /// <summary>Pseudo-random bytes (xorshift32), the same on every run.</summary>
    internal static byte[] CreateRandomData(int length, int seed)
    {
        var result = new byte[length];
        var x = (uint)seed * 0x9E3779B9u;
        for (var i = 0; i < length; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            result[i] = (byte)(x >> 11);
        }

        return result;
    }

    /// <summary>Random 'a' and 'b' (xorshift32): little to compress, many contexts, so the model memory fills quickly.</summary>
    internal static byte[] CreateTwoLetterData(int length, uint seed)
    {
        var result = new byte[length];
        var x = seed;
        for (var i = 0; i < length; i++)
        {
            x ^= x << 13;
            x ^= x >> 17;
            x ^= x << 5;
            result[i] = (byte)('a' + ((x >> 7) % 2));
        }

        return result;
    }

    internal static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "Ppmd", fileName);

    private static (int Order, uint MemorySize, byte[] Packed) ReadFixture(string fileName)
    {
        // The 5 bytes of the 7z coder properties, then the packed stream
        var content = File.ReadAllBytes(GetTestDataPath(fileName));
        return (content[0], BinaryPrimitives.ReadUInt32LittleEndian(content.AsSpan(1)), content[5..]);
    }

    private static byte[] Encode(byte[] data, int order, uint memorySize)
    {
        using var output = new MemoryStream();
        using (var encoder = new Ppmd7EncoderStream(output, order, memorySize, leaveOpen: true))
        {
            encoder.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] Decode(byte[] packed, int order, uint memorySize, long outputLength)
    {
        using var decoder = new Ppmd7DecoderStream(new MemoryStream(packed), order, memorySize, outputLength, maxMemoryUsage: 1 << 30, leaveOpen: false);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        return output.ToArray();
    }
}
