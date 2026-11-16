using System.Buffers.Binary;
using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Tests;

public sealed class Ppmd8Tests
{
    [Theory]
    [InlineData("text-o8.ppmd8", "text", 0)]
    [InlineData("ab-o16-restart.ppmd8", "two-letters", 0)]
    [InlineData("ab-o16-cutoff.ppmd8", "two-letters", 1)]
    public void Decode_7Zip(string fileName, string kind, int expectedRestoreMethod)
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture(fileName);
        var expected = CreateData(kind);
        Assert.Equal(expectedRestoreMethod, restoreMethod);
        Assert.Equal(expected, Decode(packed, order, memorySize, restoreMethod, expected.Length));
    }

    [Theory]
    [InlineData("text-o8.ppmd8", "text")]
    [InlineData("ab-o16-restart.ppmd8", "two-letters")]
    [InlineData("ab-o16-cutoff.ppmd8", "two-letters")]
    public void Decode_7Zip_UnknownLength_StopsAtTheEndMarker(string fileName, string kind)
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture(fileName);
        Assert.Equal(CreateData(kind), Decode(packed, order, memorySize, restoreMethod, outputLength: -1));
    }

    [Theory]
    [InlineData("ab-o16-restart.ppmd8", false)]
    [InlineData("ab-o16-cutoff.ppmd8", true)]
    public void Decode_7Zip_MemoryFull_RestoresTheModel(string fileName, bool cutOff)
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture(fileName);
        var model = new Ppmd8Model(memorySize);
        Assert.True(model.InitRangeDecoder(new PpmdStreamByteSource(new MemoryStream(packed))));
        model.Init(order, restoreMethod);
        var expected = CreateData("two-letters");
        foreach (var value in expected)
        {
            Assert.Equal(value, model.DecodeSymbol());
        }

        Assert.Equal(-1, model.DecodeSymbol());
        Assert.True(model.RestoreCount > 0);
        Assert.Equal(cutOff, model.CutOffCount > 0);
    }

    [Fact]
    public void Decode_SmallReads()
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture("text-o8.ppmd8");
        var expected = CreateData("text");
        using var decoder = new Ppmd8DecoderStream(new MemoryStream(packed), order, memorySize, restoreMethod, expected.Length, maxMemoryUsage: 1 << 30, leaveOpen: false);
        var result = new List<byte>();
        var buffer = new byte[13];
        int read;
        while ((read = decoder.Read(buffer, 0, buffer.Length)) > 0)
        {
            result.AddRange(buffer.AsSpan(0, read));
        }

        Assert.Equal(expected, result.ToArray());
    }

    [Fact]
    public void Decode_LongerThanTheData()
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture("text-o8.ppmd8");
        Assert.Throws<InvalidDataException>(() => Decode(packed, order, memorySize, restoreMethod, outputLength: 4001));
    }

    [Fact]
    public void Decode_Truncated()
    {
        var (order, memorySize, restoreMethod, packed) = ReadFixture("ab-o16-cutoff.ppmd8");
        Assert.Throws<InvalidDataException>(() => Decode(packed.AsSpan(0, packed.Length / 2).ToArray(), order, memorySize, restoreMethod, outputLength: 50_000));
    }

    [Theory]
    [InlineData(1, 1u << 20, 0)]
    [InlineData(17, 1u << 20, 0)]
    [InlineData(8, 2047u, 0)]
    [InlineData(8, 1u << 20, 2)]
    [InlineData(8, 1u << 20, -1)]
    public void Decode_InvalidParameters(int order, uint memorySize, int restoreMethod)
    {
        Assert.Throws<InvalidDataException>(() => new Ppmd8DecoderStream(new MemoryStream(), order, memorySize, restoreMethod, 0, long.MaxValue, leaveOpen: false));
    }

    [Fact]
    public void Decode_MemoryLargerThanMaxMemoryUsage()
    {
        Assert.Throws<InvalidDataException>(() => new Ppmd8DecoderStream(new MemoryStream(), 8, 1 << 20, 0, 0, maxMemoryUsage: 1 << 19, leaveOpen: false));
    }

    [Theory]
    [InlineData(1, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(4, 1)]
    public void Decode_Garbage_ThrowsInvalidDataOrDecodes(int seed, int restoreMethod)
    {
        var garbage = Ppmd7Tests.CreateRandomData(3000, seed);
        try
        {
            Decode(garbage, 2 + (seed * 3), 1 << 16, restoreMethod, outputLength: -1);
        }
        catch (InvalidDataException)
        {
        }
    }

    private static byte[] CreateData(string kind)
    {
        return kind switch
        {
            "two-letters" => Ppmd7Tests.CreateTwoLetterData(50_000, seed: 3),
            _ => Ppmd7Tests.CreateData(kind),
        };
    }

    private static (int Order, uint MemorySize, int RestoreMethod, byte[] Packed) ReadFixture(string fileName)
    {
        // The data of a zip entry (method 98): 2 bytes of parameters, then the PPMd stream
        var content = File.ReadAllBytes(Ppmd7Tests.GetTestDataPath(fileName));
        var parameters = BinaryPrimitives.ReadUInt16LittleEndian(content);
        var order = (parameters & 0xF) + 1;
        var memorySize = (uint)(((parameters >> 4) & 0xFF) + 1) << 20;
        var restoreMethod = parameters >> 12;
        return (order, memorySize, restoreMethod, content[2..]);
    }

    private static byte[] Decode(byte[] packed, int order, uint memorySize, int restoreMethod, long outputLength)
    {
        using var decoder = new Ppmd8DecoderStream(new MemoryStream(packed), order, memorySize, restoreMethod, outputLength, maxMemoryUsage: 1 << 30, leaveOpen: false);
        using var output = new MemoryStream();
        decoder.CopyTo(output);
        return output.ToArray();
    }
}
