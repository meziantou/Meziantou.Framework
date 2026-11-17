using Meziantou.Framework.Compression.Internals.Ppmd;

namespace Meziantou.Framework.Compression.Tests;

public sealed class PpmdRarTests
{
    [Theory]
    [InlineData(2, 1 << 20, "empty")]
    [InlineData(6, 1 << 20, "byte")]
    [InlineData(6, 1 << 20, "text")]
    [InlineData(16, 1 << 20, "random")]
    [InlineData(64, 1 << 20, "zeros")]
    [InlineData(32, 1 << 16, "two-letters")]
    public void RoundTrip(int order, int memorySize, string kind)
    {
        var data = Ppmd7Tests.CreateData(kind);
        var model = new Ppmd7Model((uint)memorySize);
        model.Init(order);
        var packed = Encode(model, data);

        var decoder = new PpmdRarDecoder();
        Assert.True(decoder.Initialize(new ArrayByteSource(packed), order, memorySize, resetModel: true, maxMemoryUsage: 1 << 30));
        foreach (var value in data)
        {
            Assert.Equal(value, decoder.DecodeSymbol());
        }
    }

    [Fact]
    public void Decode_Fixture()
    {
        // The order, the memory size in MiB minus one (as in the RAR block header), then the stream of the test encoder
        var content = File.ReadAllBytes(Ppmd7Tests.GetTestDataPath("text-o6.ppmd7a"));
        var order = content[0];
        var memorySize = (content[1] + 1) << 20;
        var expected = Ppmd7Tests.CreateData("text");

        var decoder = new PpmdRarDecoder();
        Assert.True(decoder.Initialize(new ArrayByteSource(content[2..]), order, memorySize, resetModel: true, maxMemoryUsage: 1 << 30));
        foreach (var value in expected)
        {
            Assert.Equal(value, decoder.DecodeSymbol());
        }

        var model = new Ppmd7Model((uint)memorySize);
        model.Init(order);
        Assert.Equal(content[2..], Encode(model, expected));
    }

    [Fact]
    public void RoundTrip_MemoryFull_RestartsTheModel()
    {
        var data = Ppmd7Tests.CreateData("two-letters");
        var model = new Ppmd7Model(1 << 16);
        model.Init(32);
        var packed = Encode(model, data);
        Assert.True(model.RestartCount > 0);

        var decoder = new PpmdRarDecoder();
        Assert.True(decoder.Initialize(new ArrayByteSource(packed), 32, 1 << 16, resetModel: true, maxMemoryUsage: 1 << 16));
        foreach (var value in data)
        {
            Assert.Equal(value, decoder.DecodeSymbol());
        }
    }

    [Fact]
    public void Blocks_ContinueTheModel()
    {
        // RAR starts a new range coder for each PPMd block, and keeps the model unless the block resets it
        var text = Ppmd7Tests.CreateData("text");
        var blocks = new[] { text[..1000], text[1000..2500], text[2500..] };
        var model = new Ppmd7Model(1 << 20);
        model.Init(8);
        var packedBlocks = blocks.Select(block => Encode(model, block)).ToArray();

        var decoder = new PpmdRarDecoder();
        for (var i = 0; i < blocks.Length; i++)
        {
            Assert.True(decoder.Initialize(new ArrayByteSource(packedBlocks[i]), 8, 1 << 20, resetModel: i == 0, maxMemoryUsage: 1 << 30));
            foreach (var value in blocks[i])
            {
                Assert.Equal(value, decoder.DecodeSymbol());
            }
        }

        // The last block decoded with a new model gives something else
        Assert.True(decoder.Initialize(new ArrayByteSource(packedBlocks[2]), 8, 1 << 20, resetModel: true, maxMemoryUsage: 1 << 30));
        var decoded = new byte[blocks[2].Length];
        for (var i = 0; i < decoded.Length; i++)
        {
            var symbol = decoder.DecodeSymbol();
            if (symbol < 0)
                break;

            decoded[i] = (byte)symbol;
        }

        Assert.False(decoded.AsSpan().SequenceEqual(blocks[2]));
    }

    [Fact]
    public void Initialize_ContinueAfterAnError_ReturnsFalse()
    {
        // As unrar, the model is dropped after an error: it stopped in the middle of a symbol
        var text = Ppmd7Tests.CreateData("text");
        var model = new Ppmd7Model(1 << 20);
        model.Init(6);
        var packed = Encode(model, text);

        var decoder = new PpmdRarDecoder();
        Assert.True(decoder.Initialize(new ArrayByteSource(packed[..50]), 6, 1 << 20, resetModel: true, maxMemoryUsage: 1 << 30));
        var decodedSymbols = 0;
        while (decoder.DecodeSymbol() >= 0)
        {
            decodedSymbols++;
        }

        Assert.True(decodedSymbols < 1000);
        Assert.Equal(-1, decoder.DecodeSymbol());
        Assert.False(decoder.Initialize(new ArrayByteSource(packed), 6, 1 << 20, resetModel: false, maxMemoryUsage: 1 << 30));
        Assert.True(decoder.Initialize(new ArrayByteSource(packed), 6, 1 << 20, resetModel: true, maxMemoryUsage: 1 << 30));
        Assert.Equal(text[0], decoder.DecodeSymbol());
    }

    [Fact]
    public void Initialize_ContinueWithoutModel_ReturnsFalse()
    {
        var decoder = new PpmdRarDecoder();
        Assert.False(decoder.Initialize(new ArrayByteSource([0, 0, 0, 0]), 8, 1 << 20, resetModel: false, maxMemoryUsage: 1 << 30));
        Assert.Equal(-1, decoder.DecodeSymbol());
    }

    [Theory]
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF })]
    [InlineData(new byte[] { 0, 0, 0 })]
    public void Initialize_InvalidRangeDecoder_ReturnsFalse(byte[] data)
    {
        var decoder = new PpmdRarDecoder();
        Assert.False(decoder.Initialize(new ArrayByteSource(data), 8, 1 << 20, resetModel: true, maxMemoryUsage: 1 << 30));
        Assert.Equal(-1, decoder.DecodeSymbol());
    }

    [Theory]
    [InlineData(1, 1 << 20, 1L << 30)]
    [InlineData(65, 1 << 20, 1L << 30)]
    [InlineData(8, 1000, 1L << 30)]
    [InlineData(8, -1, 1L << 30)]
    [InlineData(8, 1 << 20, (1L << 20) - 1)]
    public void Initialize_InvalidParameters_Throws(int order, int memorySize, long maxMemoryUsage)
    {
        var decoder = new PpmdRarDecoder();
        Assert.Throws<InvalidDataException>(() => decoder.Initialize(new ArrayByteSource([0, 0, 0, 0]), order, memorySize, resetModel: true, maxMemoryUsage));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Decode_Garbage_ReturnsBytesOrError(int seed)
    {
        var garbage = Ppmd7Tests.CreateRandomData(2000, seed);
        var decoder = new PpmdRarDecoder();
        if (!decoder.Initialize(new ArrayByteSource(garbage), 4 + seed, 1 << 16, resetModel: true, maxMemoryUsage: 1 << 30))
            return;

        for (var i = 0; i < 1_000_000; i++)
        {
            if (decoder.DecodeSymbol() < 0)
                return;
        }
    }

    private static byte[] Encode(Ppmd7Model model, byte[] data)
    {
        var rangeEncoder = default(Ppmd7aTestRangeEncoder);
        var output = new MemoryStream();
        rangeEncoder.Initialize(output);
        foreach (var value in data)
        {
            model.EncodeSymbol(ref rangeEncoder, value);
        }

        rangeEncoder.Flush();
        return output.ToArray();
    }

    private sealed class ArrayByteSource(byte[] data) : PpmdByteSource
    {
        private int _position;

        public override int ReadByte() => _position < data.Length ? data[_position++] : -1;
    }

    /// <summary>
    /// The carry-less range encoder of Dmitry Shkarin (Subbotin's coder), the inverse of the range decoder of RAR 2.9/4.x
    /// (Ppmd7a_RangeDec of 7-Zip): the binary symbols are coded as intervals of 2^14, as unrar does.
    /// </summary>
    private struct Ppmd7aTestRangeEncoder : IPpmd7RangeEncoder
    {
        private const uint Top = 1 << 24;
        private const uint Bot = 1 << 15;
        private const uint BinScale = 1 << 14;

        private Stream _output;
        private uint _low;
        private uint _range;

        public void Initialize(Stream output)
        {
            _output = output;
            _low = 0;
            _range = 0xFFFFFFFF;
        }

        public void Encode(uint start, uint size, uint total)
        {
            _range /= total;
            _low += start * _range;
            _range *= size;
            Normalize();
        }

        public void EncodeBit0(uint size0) => Encode(0, size0, BinScale);

        public void EncodeBit1(uint size0) => Encode(size0, BinScale - size0, BinScale);

        public void Flush()
        {
            for (var i = 0; i < 4; i++)
            {
                _output.WriteByte((byte)(_low >> 24));
                _low <<= 8;
            }
        }

        private void Normalize()
        {
            while (true)
            {
                if ((_low ^ (_low + _range)) >= Top)
                {
                    if (_range >= Bot)
                        break;

                    _range = (0 - _low) & (Bot - 1);
                }

                _output.WriteByte((byte)(_low >> 24));
                _range <<= 8;
                _low <<= 8;
            }
        }
    }
}
