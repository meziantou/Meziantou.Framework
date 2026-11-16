using System.IO.Compression;
using Meziantou.Framework.Compression.Internals.BZip2;

namespace Meziantou.Framework.Compression.Tests;

public sealed class BZip2StreamTests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "BZip2", fileName);

    private static byte[] Decompress(byte[] data, int readSize = 81920)
    {
        using var stream = new BZip2Stream(new MemoryStream(data), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[readSize];
        int read;
        while ((read = stream.Read(buffer)) > 0)
        {
            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    private static byte[] Compress(byte[] data, int blockSize = 9)
    {
        using var output = new MemoryStream();
        using (var stream = new BZip2Stream(output, new BZip2CompressionOptions { BlockSize = blockSize }, leaveOpen: true))
        {
            stream.Write(data);
        }

        return output.ToArray();
    }

    private static byte[] CreateRandomData(int length, int seed)
    {
        var data = new byte[length];
        new PseudoRandom(seed).NextBytes(data);
        return data;
    }

    // Text-like data: random words, compressible but not trivially
    private static byte[] CreateText(int length, int seed)
    {
        string[] words = ["alpha", "beta", "gamma", "delta", "epsilon", "zeta", "eta", "theta", "iota", "kappa", " ", "\n", "lambda", "mu"];
        var random = new PseudoRandom(seed);
        var data = new byte[length];
        var position = 0;
        while (position < length)
        {
            var word = words[random.Next(words.Length)];
            for (var i = 0; i < word.Length && position < length; i++)
            {
                data[position++] = (byte)word[i];
            }
        }

        return data;
    }

    [Theory]
    [InlineData("sample.txt.bz2")]
    [InlineData("multistream.bz2")]
    public void Decompress_Bzip2Output(string fileName)
    {
        var expected = File.ReadAllBytes(GetTestDataPath("sample.txt"));
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath(fileName)));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decompress_Bzip2Output_MultipleBlocks()
    {
        var expected = new byte[250_000];
        for (var i = 0; i < expected.Length; i++)
        {
            expected[i] = (byte)('a' + (i % 10));
        }

        var actual = Decompress(File.ReadAllBytes(GetTestDataPath("multiblock.bz2")));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decompress_Bzip2Output_Empty()
    {
        Assert.Empty(Decompress(File.ReadAllBytes(GetTestDataPath("empty.bz2"))));
    }

    [Fact]
    public void Decompress_RandomizedBlock()
    {
        // Written by the encoder with the randomization of bzip2 0.9.0, and checked with bzip2 -d (bzip2 1.0.8)
        var expected = File.ReadAllBytes(GetTestDataPath("sample.txt"));
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath("randomized.bz2")));
        Assert.Equal(expected, actual);
    }

    [Fact]
    public void Decompress_OneByteReads()
    {
        var expected = File.ReadAllBytes(GetTestDataPath("sample.txt"));
        var actual = Decompress(File.ReadAllBytes(GetTestDataPath("multistream.bz2")), readSize: 1);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task Decompress_Async()
    {
        var expected = File.ReadAllBytes(GetTestDataPath("sample.txt"));
        await using var stream = new BZip2Stream(File.OpenRead(GetTestDataPath("sample.txt.bz2")), CompressionMode.Decompress);
        using var output = new MemoryStream();
        await stream.CopyToAsync(output, TestContext.Current.CancellationToken);
        Assert.Equal(expected, output.ToArray());
    }

    [Fact]
    public void Decompress_IgnoresTrailingData()
    {
        var expected = File.ReadAllBytes(GetTestDataPath("sample.txt"));
        byte[] data = [.. File.ReadAllBytes(GetTestDataPath("sample.txt.bz2")), 0, 0, 0, 0];
        Assert.Equal(expected, Decompress(data));
    }

    [Fact]
    public void Decompress_CorruptedBlockCrc_Throws()
    {
        var data = Compress(CreateText(10_000, 1));

        // The CRC of the first block follows the header (4 bytes) and the magic number of the block (6 bytes)
        data[12] ^= 0x10;
        Assert.Throws<InvalidDataException>(() => Decompress(data));
    }

    [Fact]
    public void Decompress_CorruptedStreamCrc_Throws()
    {
        var data = Compress(CreateText(10_000, 1));

        // The last 32 bits before the padding of the last byte
        data[^2] ^= 0x01;
        Assert.Throws<InvalidDataException>(() => Decompress(data));
    }

    [Fact]
    public void Decompress_CorruptedData_Throws()
    {
        var data = Compress(CreateText(10_000, 1));
        data[data.Length / 2] ^= 0x55;
        Assert.Throws<InvalidDataException>(() => Decompress(data));
    }

    [Fact]
    public void Decompress_Truncated_Throws()
    {
        var data = Compress(CreateText(10_000, 1));
        Assert.Throws<InvalidDataException>(() => Decompress(data[..(data.Length - 6)]));
    }

    [Fact]
    public void Decompress_InvalidHeader_Throws()
    {
        Assert.Throws<InvalidDataException>(() => Decompress("BZh0"u8.ToArray()));
        Assert.Throws<InvalidDataException>(() => Decompress([]));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(5)]
    [InlineData(1000)]
    public void RoundTrip_SmallSizes(int length)
    {
        var data = CreateText(length, length);
        Assert.Equal(data, Decompress(Compress(data)));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    public void RoundTrip_EveryBlockSize(int blockSize)
    {
        // Longer than a block of the smallest sizes
        var data = CreateText(250_000, blockSize);
        var compressed = Compress(data, blockSize);
        Assert.Equal((byte)('0' + blockSize), compressed[3]);
        Assert.Equal(data, Decompress(compressed));
    }

    [Theory]
    [InlineData(99_980)]
    [InlineData(99_981)]
    [InlineData(100_000)]
    [InlineData(100_001)]
    [InlineData(199_962)]
    [InlineData(300_000)]
    public void RoundTrip_AcrossBlockBoundaries(int length)
    {
        var data = CreateRandomData(length, length);
        Assert.Equal(data, Decompress(Compress(data, blockSize: 1)));
    }

    [Fact]
    public void RoundTrip_Runs()
    {
        // Runs of every length around the run-length encoding of bzip2 (4 bytes and a count, at most 255)
        using var data = new MemoryStream();
        for (var length = 1; length < 600; length++)
        {
            for (var i = 0; i < length; i++)
            {
                data.WriteByte((byte)(length % 7));
            }
        }

        data.Write(new byte[300_000]);
        var bytes = data.ToArray();
        Assert.Equal(bytes, Decompress(Compress(bytes, blockSize: 1)));
        Assert.Equal(bytes, Decompress(Compress(bytes)));
    }

    [Fact]
    public void RoundTrip_PeriodicData()
    {
        // The rotations of a periodic block are equal
        var data = new byte[50_000];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)"abcab"[i % 5];
        }

        Assert.Equal(data, Decompress(Compress(data)));
        Assert.Equal("abab"u8.ToArray(), Decompress(Compress("abab"u8.ToArray())));
    }

    [Fact]
    public void RoundTrip_SmallAlphabets()
    {
        // Many rotations share long prefixes: the order of the rotations must not depend on the end of the block
        var random = new PseudoRandom(7);
        for (var length = 1; length < 300; length++)
        {
            foreach (var alphabet in (int[])[1, 2, 3])
            {
                var data = new byte[length];
                for (var i = 0; i < length; i++)
                {
                    data[i] = (byte)('a' + random.Next(alphabet));
                }

                Assert.Equal(data, Decompress(Compress(data, blockSize: 1)));
            }
        }
    }

    [Fact]
    public void RoundTrip_RandomData()
    {
        var data = CreateRandomData(1_000_000, 42);
        Assert.Equal(data, Decompress(Compress(data)));
    }

    [Fact]
    public void RoundTrip_EveryByteValue()
    {
        var data = new byte[256 * 40];
        for (var i = 0; i < data.Length; i++)
        {
            data[i] = (byte)(i * 7);
        }

        Assert.Equal(data, Decompress(Compress(data)));
    }

    [Fact]
    public void RoundTrip_WriteByteByByte()
    {
        var data = CreateText(20_000, 3);
        using var output = new MemoryStream();
        using (var stream = new BZip2Stream(output, CompressionLevel.SmallestSize, leaveOpen: true))
        {
            foreach (var value in data)
            {
                stream.WriteByte(value);
            }
        }

        Assert.Equal(data, Decompress(output.ToArray()));
    }

    [Fact]
    public void RoundTrip_RandomizedBlocks()
    {
        var data = CreateText(150_000, 5);
        using var output = new MemoryStream();
        using (var stream = new BZip2EncoderStream(output, 1, leaveOpen: true, randomize: true))
        {
            stream.Write(data);
        }

        Assert.Equal(data, Decompress(output.ToArray()));
    }

    [Fact]
    public void RoundTrip_ConcatenatedStreams()
    {
        var first = CreateText(5_000, 1);
        var second = CreateRandomData(5_000, 2);
        byte[] data = [.. Compress(first), .. Compress(second, 1)];
        Assert.Equal([.. first, .. second], Decompress(data));
    }

    [Fact]
    public void Compress_CompressesText()
    {
        var data = CreateText(500_000, 9);
        var compressed = Compress(data);
        Assert.HasCountLessThan(data.Length / 3, compressed);
    }

    [Fact]
    public void Compress_EmptyIsTheOutputOfBzip2()
    {
        Assert.Equal(File.ReadAllBytes(GetTestDataPath("empty.bz2")), Compress([]));
    }

    [Fact]
    public void Modes()
    {
        using var decompress = new BZip2Stream(new MemoryStream(), CompressionMode.Decompress);
        Assert.True(decompress.CanRead);
        Assert.False(decompress.CanWrite);
        Assert.False(decompress.CanSeek);
        Assert.Throws<NotSupportedException>(() => decompress.Write([1]));

        using var compress = new BZip2Stream(new MemoryStream(), CompressionMode.Compress);
        Assert.False(compress.CanRead);
        Assert.True(compress.CanWrite);
        Assert.Throws<NotSupportedException>(() => compress.Read(new byte[1]));
    }

    [Fact]
    public void Dispose_LeaveOpen()
    {
        var output = new MemoryStream();
        new BZip2Stream(output, CompressionMode.Compress, leaveOpen: true).Dispose();
        Assert.True(output.CanWrite);

        new BZip2Stream(output, CompressionMode.Compress).Dispose();
        Assert.False(output.CanWrite);
    }

    [Fact]
    public void Options_BlockSize_OutOfRange()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new BZip2CompressionOptions { BlockSize = 0 });
        Assert.Throws<ArgumentOutOfRangeException>(() => new BZip2CompressionOptions { BlockSize = 10 });
    }

    [Fact]
    public void Documentation_Example()
    {
        #region BZip2Stream_RoundTrip
        var data = "Hello, bzip2! Hello, bzip2!"u8.ToArray();

        using var compressed = new MemoryStream();
        using (var bzip2 = new BZip2Stream(compressed, new BZip2CompressionOptions { BlockSize = 9 }, leaveOpen: true))
        {
            bzip2.Write(data);
        }

        compressed.Position = 0;
        using var decompress = new BZip2Stream(compressed, CompressionMode.Decompress);
        using var result = new MemoryStream();
        decompress.CopyTo(result);
        Assert.Equal(data, result.ToArray());
        #endregion
    }

    /// <summary>A deterministic generator of test data (xorshift64*), the same on every platform and version of .NET.</summary>
    internal sealed class PseudoRandom(int seed)
    {
        private ulong _state = ((ulong)(uint)seed * 0x9E3779B97F4A7C15UL) | 1;

        public ulong NextUInt64()
        {
            _state ^= _state >> 12;
            _state ^= _state << 25;
            _state ^= _state >> 27;
            return _state * 0x2545F4914F6CDD1DUL;
        }

        public int Next(int maxValue) => (int)(NextUInt64() % (ulong)maxValue);

        public void NextBytes(Span<byte> buffer)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (byte)(NextUInt64() >> 56);
            }
        }
    }
}
