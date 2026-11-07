using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writer of the descriptors of Rec. ITU-T H.265 (clauses 7.2 and 9.2): the expected bytes are worked out by hand from
/// the definition of each descriptor (Tables 9-1 to 9-3), and every value is read back by <see cref="HevcBitReader"/>,
/// whose own tests are hand-computed too.
/// </summary>
public sealed class HevcBitWriterTests
{
    private delegate void WriterAction(ref HevcBitWriter writer);

    [Fact]
    public void FixedWidthNumbersAreWrittenMostSignificantBitFirst()
    {
        // 1 011 0010 | 0100 1111 1010 | 0101 0011 1100
        var bytes = Write((ref HevcBitWriter writer) =>
        {
            Assert.True(writer.IsByteAligned);
            writer.WriteFlag(true);
            Assert.False(writer.IsByteAligned);
            writer.WriteBits(0b011, 3);
            writer.WriteBits(0b0010, 4);
            Assert.Equal(8, writer.BitPosition);
            Assert.True(writer.IsByteAligned);
            writer.WriteBits(0, 0);
            writer.WriteBits(0b0100_1111_1010, 12);
            Assert.Equal(20, writer.BitPosition);
            writer.WriteBits(0b0101_0011_1100, 12);
        });

        Assert.Equal([0b1011_0010, 0b0100_1111, 0b1010_0101, 0b0011_1100], bytes);
    }

    [Fact]
    public void ThirtyTwoBitsAreWrittenAtAnyBitOffset()
    {
        for (var offset = 0; offset < 8; offset++)
        {
            var leading = offset;
            var bytes = Write((ref HevcBitWriter writer) =>
            {
                writer.WriteBits((1u << leading) - 1, leading);
                writer.WriteBits(0xDEADBEEF, 32);
                writer.WriteBits(0, 32);
                writer.WriteBits(uint.MaxValue, 32);
                writer.WriteRbspTrailingBits();
            });

            var reader = new HevcBitReader(bytes);
            Assert.Equal((1u << offset) - 1, reader.ReadBits(offset));
            Assert.Equal(0xDEADBEEF, reader.ReadBits(32));
            Assert.Equal(0u, reader.ReadBits(32));
            Assert.Equal(uint.MaxValue, reader.ReadBits(32));
            reader.ReadRbspTrailingBits();
        }
    }

    [Fact]
    public void UnsignedExpGolombCodesAreThoseOfTable92()
    {
        // Table 9-2: 0 is 1; 1 is 010; 2 is 011; 3 is 00100; 4 is 00101; 5 is 00110; 6 is 00111; 7 is 0001000; 8 is 0001001
        var bytes = Write((ref HevcBitWriter writer) =>
        {
            for (var codeNum = 0u; codeNum <= 8; codeNum++)
            {
                writer.WriteUE(codeNum);
            }

            writer.WriteAlignmentZeroBits();
        });

        // 1 010 011 0 | 0100 0010 | 1 00110 00 | 111 00010 | 00 000100 | 1 0000000
        Assert.Equal([0b1010_0110, 0b0100_0010, 0b1001_1000, 0b1110_0010, 0b0000_0100, 0b1000_0000], bytes);
        int[] lengths = [1, 3, 3, 5, 5, 5, 5, 7, 7];
        for (var codeNum = 0u; codeNum <= 8; codeNum++)
        {
            Assert.Equal(lengths[codeNum], HevcBitWriter.GetUELength(codeNum));
        }

        // The largest value of the syntax: 31 zero bits, then 32 bits
        Assert.Equal(63, HevcBitWriter.GetUELength(uint.MaxValue - 1));
    }

    [Fact]
    public void SignedExpGolombCodesAreThoseOfTable93()
    {
        // Table 9-3: the code numbers 0, 1, 2, 3, 4, 5 and 6 are the values 0, 1, -1, 2, -2, 3 and -3
        int[] values = [0, 1, -1, 2, -2, 3, -3];
        var signed = Write((ref HevcBitWriter writer) =>
        {
            foreach (var value in values)
            {
                writer.WriteSE(value);
            }

            writer.WriteAlignmentZeroBits();
        });
        var unsigned = Write((ref HevcBitWriter writer) =>
        {
            for (var codeNum = 0u; codeNum < 7; codeNum++)
            {
                writer.WriteUE(codeNum);
            }

            writer.WriteAlignmentZeroBits();
        });

        Assert.Equal(unsigned, signed);
        for (var codeNum = 0u; codeNum < 7; codeNum++)
        {
            Assert.Equal(HevcBitWriter.GetUELength(codeNum), HevcBitWriter.GetSELength(values[codeNum]));
        }
    }

    [Fact]
    public void ExpGolombCodesAreReadBack()
    {
        uint[] unsignedValues = [0, 1, 2, 6, 7, 254, 255, 256, 65534, 65535, 65536, 1u << 31, uint.MaxValue - 1];
        int[] signedValues = [0, 1, -1, 2, -2, 127, -128, 32767, -32768, 1 << 20, -(1 << 20), int.MaxValue, -int.MaxValue];
        var bytes = Write((ref HevcBitWriter writer) =>
        {
            long bits = 0;
            foreach (var value in unsignedValues)
            {
                writer.WriteUE(value);
                bits += HevcBitWriter.GetUELength(value);
                Assert.Equal(bits, writer.BitPosition);
            }

            foreach (var value in signedValues)
            {
                writer.WriteSE(value);
                bits += HevcBitWriter.GetSELength(value);
                Assert.Equal(bits, writer.BitPosition);
            }

            writer.WriteRbspTrailingBits();
        });

        var reader = new HevcBitReader(bytes);
        foreach (var value in unsignedValues)
        {
            Assert.Equal(value, reader.ReadUE());
        }

        foreach (var value in signedValues)
        {
            Assert.Equal(value, reader.ReadSE());
        }

        Assert.False(reader.MoreRbspData());
        reader.ReadRbspTrailingBits();
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void RandomSyntaxElementsAreReadBack()
    {
        var random = new Random(1);
        var elements = new (int Kind, long Value, int Bits)[5000];
        for (var i = 0; i < elements.Length; i++)
        {
            var kind = random.Next(4);
            var bits = random.Next(1, 33);
            elements[i] = kind switch
            {
                0 => (0, random.NextInt64(1L << bits), bits),
                1 => (1, random.Next(2), 1),
                2 => (2, random.NextInt64(1L << random.Next(1, 32)), 0),
                _ => (3, random.NextInt64(-(1L << 30), 1L << 30), 0),
            };
        }

        var bytes = Write((ref HevcBitWriter writer) =>
        {
            foreach (var (kind, value, bits) in elements)
            {
                switch (kind)
                {
                    case 0:
                        writer.WriteBits((uint)value, bits);
                        break;
                    case 1:
                        writer.WriteFlag(value is not 0);
                        break;
                    case 2:
                        writer.WriteUE((uint)value);
                        break;
                    default:
                        writer.WriteSE((int)value);
                        break;
                }
            }

            writer.WriteRbspTrailingBits();
        });

        var reader = new HevcBitReader(bytes);
        foreach (var (kind, value, bits) in elements)
        {
            var actual = kind switch
            {
                0 => (long)reader.ReadBits(bits),
                1 => reader.ReadFlag() ? 1L : 0L,
                2 => reader.ReadUE(),
                _ => reader.ReadSE(),
            };
            Assert.Equal(value, actual);
        }

        reader.ReadRbspTrailingBits();
    }

    [Fact]
    public void TrailingBitsAndAlignmentsEndOnAByte()
    {
        // rbsp_trailing_bits( ) after 3 bits: 101 1 0000; on a byte: 1000 0000
        Assert.Equal([0b1011_0000], Write((ref HevcBitWriter writer) =>
        {
            writer.WriteBits(0b101, 3);
            writer.WriteRbspTrailingBits();
            Assert.Equal(8, writer.BitPosition);
        }));
        Assert.Equal([0xAB, 0x80], Write((ref HevcBitWriter writer) =>
        {
            writer.WriteBits(0xAB, 8);
            writer.WriteRbspTrailingBits();
        }));

        // byte_alignment( ) has the same bits; the bits of pcm_alignment_zero_bit are zero, and there is none on a byte
        Assert.Equal([0b1111_1110, 0xCD], Write((ref HevcBitWriter writer) =>
        {
            writer.WriteBits(0b111111, 6);
            writer.WriteByteAlignment();
            writer.WriteBits(0xCD, 8);
            writer.WriteAlignmentZeroBits();
        }));
        Assert.Equal([0b1100_0000, 0xEF], Write((ref HevcBitWriter writer) =>
        {
            writer.WriteBits(0b11, 2);
            writer.WriteAlignmentZeroBits();
            writer.WriteBits(0xEF, 8);
        }));

        var reader = new HevcBitReader(Write((ref HevcBitWriter writer) =>
        {
            writer.WriteUE(5);
            writer.WriteByteAlignment();
            writer.WriteBits(0x1234, 16);
        }));
        Assert.Equal(5u, reader.ReadUE());
        reader.ReadByteAlignment();
        Assert.Equal(1, reader.BytePosition);
        Assert.Equal(0x1234u, reader.ReadBits(16));
    }

    [Fact]
    public void AWriterAppendsToItsOutput()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(AWriterAppendsToItsOutput));
        using var output = new PooledPayloadWriter(scope);
        output.Write([0x11, 0x22]);
        var writer = new HevcBitWriter(output);
        writer.WriteBits(0x3, 4);
        Assert.Equal(4, writer.BitPosition);
        writer.WriteBits(0x3, 4);
        Assert.Equal([0x11, 0x22, 0x33], output.WrittenSpan.ToArray());
    }

    private static byte[] Write(WriterAction action)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(HevcBitWriterTests));
        using var output = new PooledPayloadWriter(scope);
        var writer = new HevcBitWriter(output);
        action(ref writer);
        Assert.True(writer.IsByteAligned);
        return output.WrittenSpan.ToArray();
    }
}
