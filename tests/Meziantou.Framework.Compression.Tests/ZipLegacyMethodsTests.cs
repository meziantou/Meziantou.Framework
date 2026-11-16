using System.Buffers.Binary;
using System.IO.Hashing;
using Meziantou.Framework.Compression.Internals.ZipLegacy;

namespace Meziantou.Framework.Compression.Tests;

public sealed class ZipLegacyMethodsTests
{
    private static string GetTestDataPath(string fileName) => Path.Combine(AppContext.BaseDirectory, "TestData", "ZipLegacy", fileName);

    /// <summary>Reads the entries of a zip file from their local headers (the sizes and the CRC are in the local headers of the test files).</summary>
    internal static List<ZipLocalEntry> ReadLocalEntries(string path)
    {
        var data = File.ReadAllBytes(path);
        var entries = new List<ZipLocalEntry>();
        var offset = 0;
        while (offset + 30 <= data.Length && BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(offset)) == 0x04034B50)
        {
            var header = data.AsSpan(offset, 30);
            var flags = BinaryPrimitives.ReadUInt16LittleEndian(header[6..]);
            var method = BinaryPrimitives.ReadUInt16LittleEndian(header[8..]);
            var crc = BinaryPrimitives.ReadUInt32LittleEndian(header[14..]);
            var compressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[18..]);
            var uncompressedSize = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[22..]);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(header[26..]);
            var extraLength = BinaryPrimitives.ReadUInt16LittleEndian(header[28..]);
            var start = offset + 30 + nameLength + extraLength;
            var name = System.Text.Encoding.ASCII.GetString(data, offset + 30, nameLength);
            entries.Add(new ZipLocalEntry(name, method, flags, crc, uncompressedSize, data.AsSpan(start, compressedSize).ToArray()));
            offset = start + compressedSize;
        }

        return entries;
    }

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

    private static byte[] Decode(ZipLocalEntry entry, byte[] data, int readSize = 81920)
    {
        using var decoder = CreateDecoder(entry, data);
        return ReadAll(decoder, readSize);
    }

    private static Stream CreateDecoder(ZipLocalEntry entry, byte[] data) => entry.Method switch
    {
        1 => new ZipShrinkDecoderStream(new MemoryStream(data), entry.UncompressedSize, leaveOpen: false),
        >= 2 and <= 5 => new ZipReduceDecoderStream(new MemoryStream(data), entry.Method - 1, entry.UncompressedSize, leaveOpen: false),
        6 => new ZipImplodeDecoderStream(new MemoryStream(data), largeWindow: (entry.Flags & 2) is not 0, literalTree: (entry.Flags & 4) is not 0, entry.UncompressedSize, leaveOpen: false),
        _ => throw new NotSupportedException(),
    };

    [Theory]
    [InlineData("SHRUNK.ZIP", 1)]
    [InlineData("imploding-4Kdict-2trees.zip", 6)]
    [InlineData("imploding-8Kdict-3trees.zip", 6)]
    public void Decompress_Fixtures(string fileName, int method)
    {
        var entries = ReadLocalEntries(GetTestDataPath(fileName));
        Assert.NotEmpty(entries);
        foreach (var entry in entries)
        {
            Assert.Equal(method, entry.Method);
            var output = Decode(entry, entry.Data);
            Assert.HasCount(entry.UncompressedSize, output);
            Assert.Equal(entry.Crc, Crc32.HashToUInt32(output));
            Assert.Equal(output, Decode(entry, entry.Data, readSize: 1));
        }
    }

    [Theory]
    [InlineData("SHRUNK.ZIP")]
    [InlineData("imploding-4Kdict-2trees.zip")]
    [InlineData("imploding-8Kdict-3trees.zip")]
    public void Decompress_Truncated_Throws(string fileName)
    {
        foreach (var entry in ReadLocalEntries(GetTestDataPath(fileName)))
        {
            Assert.Throws<InvalidDataException>(() => Decode(entry, entry.Data[..(entry.Data.Length / 2)]));
        }
    }

    [Fact]
    public void Implode_InvalidTree_Throws()
    {
        // A tree of 3 values where 64 are expected
        byte[] data = [0x00, 0x21, 0, 0, 0, 0];
        Assert.Throws<InvalidDataException>(() =>
        {
            using var decoder = new ZipImplodeDecoderStream(new MemoryStream(data), largeWindow: false, literalTree: false, outputLength: 10, leaveOpen: false);
            return ReadAll(decoder);
        });
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Reduce_HandBuiltVector(int factor)
    {
        var (data, expected) = CreateReduceVector(factor);
        using var decoder = new ZipReduceDecoderStream(new MemoryStream(data), factor, expected.Length, leaveOpen: false);
        Assert.Equal(expected, ReadAll(decoder));

        using var oneByteDecoder = new ZipReduceDecoderStream(new MemoryStream(data), factor, expected.Length, leaveOpen: false);
        Assert.Equal(expected, ReadAll(oneByteDecoder, readSize: 1));
    }

    [Fact]
    public void Reduce_Truncated_Throws()
    {
        var (data, expected) = CreateReduceVector(2);
        Assert.Throws<InvalidDataException>(() =>
        {
            using var decoder = new ZipReduceDecoderStream(new MemoryStream(data[..^4]), 2, expected.Length, leaveOpen: false);
            return ReadAll(decoder);
        });
    }

    [Fact]
    public void Reduce_InvalidFollowerIndex_Throws()
    {
        var writer = new LsbBitWriter();

        // The byte 0 has 3 followers: the index has 2 bits, and 3 is not a follower
        for (var i = 255; i >= 0; i--)
        {
            if (i is 0)
            {
                writer.Write(6, 3);
                writer.Write(8, 1);
                writer.Write(8, 2);
                writer.Write(8, 3);
            }
            else
            {
                writer.Write(6, 0);
            }
        }

        writer.Write(1, 0);
        writer.Write(2, 3);
        Assert.Throws<InvalidDataException>(() =>
        {
            using var decoder = new ZipReduceDecoderStream(new MemoryStream(writer.ToArray()), 1, 10, leaveOpen: false);
            return ReadAll(decoder);
        });
    }

    [Fact]
    public void Shrink_ControlCodes()
    {
        // 'a', 'b', 257 ("ab"), widen the codes to 10 bits, 258 ("ba"), 'x' (260 is "bax"), a partial clearing that frees
        // the codes that are not a prefix (259 "abb" and 260), then 'c' (259 is "xc"), 'd' (260 is "cd"), 260 and 259
        var writer = new LsbBitWriter();
        writer.Write(9, 'a');
        writer.Write(9, 'b');
        writer.Write(9, 257);
        writer.Write(9, 256);
        writer.Write(9, 1);
        writer.Write(10, 258);
        writer.Write(10, 'x');
        writer.Write(10, 256);
        writer.Write(10, 2);
        writer.Write(10, 'c');
        writer.Write(10, 'd');
        writer.Write(10, 260);
        writer.Write(10, 259);
        var expected = "ababbaxcdcdxc"u8.ToArray();
        using var decoder = new ZipShrinkDecoderStream(new MemoryStream(writer.ToArray()), expected.Length, leaveOpen: false);
        Assert.Equal(expected, ReadAll(decoder));
    }

    [Fact]
    public void Shrink_InvalidControlCode_Throws()
    {
        var writer = new LsbBitWriter();
        writer.Write(9, 'a');
        writer.Write(9, 256);
        writer.Write(9, 3);
        Assert.Throws<InvalidDataException>(() =>
        {
            using var decoder = new ZipShrinkDecoderStream(new MemoryStream(writer.ToArray()), 10, leaveOpen: false);
            return ReadAll(decoder);
        });
    }

    /// <summary>
    /// Creates reduced data by hand: the follower set of 'a' is "bc", the other sets are empty. The data contain literals,
    /// followers, a literal DLE, copies with the length in the length byte only and with an extra length byte, and a copy
    /// from more than 256 bytes back (whose high bits are in the length byte).
    /// </summary>
    private static (byte[] Data, byte[] Expected) CreateReduceVector(int factor)
    {
        var writer = new LsbBitWriter();
        for (var i = 255; i >= 0; i--)
        {
            if (i == 'a')
            {
                writer.Write(6, 2);
                writer.Write(8, 'b');
                writer.Write(8, 'c');
            }
            else
            {
                writer.Write(6, 0);
            }
        }

        var lengthMask = 0xFF >> factor;
        var last = 0;
        var expected = new List<byte>();

        void Emit(int value)
        {
            if (last == 'a')
            {
                // A follower when possible, a literal otherwise
                if (value is 'b' or 'c')
                {
                    writer.Write(1, 0);
                    writer.Write(1, value == 'b' ? 0 : 1);
                }
                else
                {
                    writer.Write(1, 1);
                    writer.Write(8, value);
                }
            }
            else
            {
                writer.Write(8, value);
            }

            last = value;
        }

        void Literal(byte value)
        {
            Emit(value);
            expected.Add(value);
            if (value is 0x90)
            {
                Emit(0);
            }
        }

        void Copy(int distance, int length)
        {
            var lengthCode = length - 3;
            var high = (distance - 1) >> 8;
            Emit(0x90);
            if (lengthCode >= lengthMask)
            {
                Emit(lengthMask | (high << (8 - factor)));
                Emit(lengthCode - lengthMask);
            }
            else
            {
                Emit(lengthCode | (high << (8 - factor)));
            }

            Emit((distance - 1) & 0xFF);
            for (var i = 0; i < length; i++)
            {
                expected.Add(expected[^distance]);
            }
        }

        Literal((byte)'a');
        Literal((byte)'b');
        Literal((byte)'c');
        Copy(distance: 3, length: 6);
        Literal(0x90);
        Literal((byte)'a');
        Literal((byte)'c');
        Copy(distance: 1, length: lengthMask + 8);
        for (var i = 0; i < 300; i++)
        {
            Literal((byte)((i % 100) + 1));
        }

        Copy(distance: 300, length: 10);
        return (writer.ToArray(), [.. expected]);
    }

    internal sealed record ZipLocalEntry(string Name, int Method, int Flags, uint Crc, int UncompressedSize, byte[] Data);

    /// <summary>Writes bits, least significant bit first.</summary>
    internal sealed class LsbBitWriter
    {
        private readonly List<byte> _bytes = [];
        private ulong _bits;
        private int _count;

        public void Write(int count, int value)
        {
            _bits |= (ulong)(value & ((1L << count) - 1)) << _count;
            _count += count;
            while (_count >= 8)
            {
                _bytes.Add((byte)_bits);
                _bits >>= 8;
                _count -= 8;
            }
        }

        /// <summary>Writes a Huffman code: its most significant bit first.</summary>
        public void WriteCode(int count, int code)
        {
            for (var i = count - 1; i >= 0; i--)
            {
                Write(1, (code >> i) & 1);
            }
        }

        public byte[] ToArray()
        {
            var result = new List<byte>(_bytes);
            if (_count > 0)
            {
                result.Add((byte)_bits);
            }

            return [.. result];
        }
    }
}
