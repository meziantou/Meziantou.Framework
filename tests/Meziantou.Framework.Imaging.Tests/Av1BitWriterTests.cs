using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The writer of the descriptors of the AV1 specification (section 4.10): the expected bytes are worked out by hand from
/// the definition of each descriptor, and every value is read back by <see cref="Av1BitReader"/>, whose own tests are
/// hand-computed too.
/// </summary>
public sealed class Av1BitWriterTests
{
    [Fact]
    public void FixedWidthNumbersAreWrittenMostSignificantBitFirst()
    {
        // 1 011 0010 | 0100 1111 1010 | 0101 0011 1100
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            writer.WriteFlag(true);
            writer.WriteBits(0b011, 3);
            writer.WriteBits(0b0010, 4);
            Assert.Equal(8, writer.BitPosition);
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
        // 0xDEADBEEF << 1 = 0x1_BD5B_7DDE, after seven 1 bits: 1111111 1 1011 1101 0101 1011 0111 1101 1101 1110
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            writer.WriteBits(0x7F, 7);
            writer.WriteBits(0xDEADBEEF, 32);
            writer.ByteAlign();
        });

        Assert.Equal([0xFF, 0xBD, 0x5B, 0x7D, 0xDE], bytes);
    }

    [Fact]
    public void SignedNumbersAreTheBottomBitsOfTheirTwosComplementForm()
    {
        // su(4): -8 is 1000, -1 is 1111, 7 is 0111, 0 is 0000
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            writer.WriteSigned(-8, 4);
            writer.WriteSigned(-1, 4);
            writer.WriteSigned(7, 4);
            writer.WriteSigned(0, 4);
        });

        Assert.Equal([0b1000_1111, 0b0111_0000], bytes);
        var reader = new Av1BitReader(bytes);
        Assert.Equal(-8, reader.ReadSigned(4));
        Assert.Equal(-1, reader.ReadSigned(4));
        Assert.Equal(7, reader.ReadSigned(4));
        Assert.Equal(0, reader.ReadSigned(4));
    }

    [Fact]
    public void NonSymmetricNumbersUseTheShortFormForTheFirstValues()
    {
        // ns(5): w = 3 and m = 3, so 0, 1 and 2 take 2 bits (00, 01, 10) and 3 and 4 take 3 bits (110, 111)
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            for (var value = 0; value < 5; value++)
            {
                writer.WriteNonSymmetric(value, 5);
            }

            writer.ByteAlign();
        });

        // 00 01 10 110 111 0000
        Assert.Equal([0b0001_1011, 0b0111_0000], bytes);
    }

    [Fact]
    public void NonSymmetricNumbersAreReadBack()
    {
        for (var valueCount = 1; valueCount <= 70; valueCount++)
        {
            var count = valueCount;
            var bytes = Write((ref Av1BitWriter writer) =>
            {
                for (var value = 0; value < count; value++)
                {
                    writer.WriteNonSymmetric(value, count);
                }

                writer.ByteAlign();
            });

            var reader = new Av1BitReader(bytes);
            for (var value = 0; value < valueCount; value++)
            {
                Assert.Equal(value, reader.ReadNonSymmetric(valueCount));
            }
        }
    }

    [Fact]
    public void VariableLengthNumbersCountTheirBitsWithLeadingZeros()
    {
        // uvlc: 0 is 1; 1 is 010; 2 is 011; 6 is 00111; 7 is 0001000
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            writer.WriteUvlc(0);
            writer.WriteUvlc(1);
            writer.WriteUvlc(2);
            writer.WriteUvlc(6);
            writer.WriteUvlc(7);
            writer.ByteAlign();
        });

        // 1 010 011 0 | 0111 0001 | 000 00000
        Assert.Equal([0b1010_0110, 0b0111_0001, 0b0000_0000], bytes);

        uint[] values = [0, 1, 2, 6, 7, 254, 255, 256, 65535, 1u << 31, uint.MaxValue - 1];
        var roundTrip = Write((ref Av1BitWriter writer) =>
        {
            foreach (var value in values)
            {
                writer.WriteUvlc(value);
            }

            writer.ByteAlign();
        });

        var reader = new Av1BitReader(roundTrip);
        foreach (var value in values)
        {
            Assert.Equal(value, reader.ReadUvlc());
        }
    }

    [Fact]
    public void LittleEndianAndLeb128NumbersAreWrittenLeastSignificantGroupFirst()
    {
        var bytes = Write((ref Av1BitWriter writer) =>
        {
            writer.WriteLittleEndian(0x00C0FFEE, 3);
            writer.WriteLeb128(0);
            writer.WriteLeb128(127);
            writer.WriteLeb128(128);
            writer.WriteLeb128(624485); // the classic example: E5 8E 26
            writer.WriteLeb128(uint.MaxValue);
        });

        Assert.Equal([0xEE, 0xFF, 0xC0, 0x00, 0x7F, 0x80, 0x01, 0xE5, 0x8E, 0x26, 0xFF, 0xFF, 0xFF, 0xFF, 0x0F], bytes);
        Assert.Equal(1, Av1BitWriter.GetLeb128Length(127));
        Assert.Equal(2, Av1BitWriter.GetLeb128Length(128));
        Assert.Equal(3, Av1BitWriter.GetLeb128Length(624485));
        Assert.Equal(5, Av1BitWriter.GetLeb128Length(uint.MaxValue));

        var reader = new Av1BitReader(bytes);
        Assert.Equal(0x00C0FFEEu, reader.ReadLittleEndian(3));
        Assert.Equal(0u, reader.ReadLeb128());
        Assert.Equal(127u, reader.ReadLeb128());
        Assert.Equal(128u, reader.ReadLeb128());
        Assert.Equal(624485u, reader.ReadLeb128());
        Assert.Equal(uint.MaxValue, reader.ReadLeb128());
    }

    [Fact]
    public void TrailingBitsAreAOneThenZerosUpToTheNextByte()
    {
        Assert.Equal([0b1011_0000], Write((ref Av1BitWriter writer) =>
        {
            writer.WriteBits(0b101, 3);
            writer.WriteTrailingBits();
        }));

        // On a byte boundary the trailing bits are a whole byte
        Assert.Equal([0xAB, 0x80], Write((ref Av1BitWriter writer) =>
        {
            writer.WriteBits(0xAB, 8);
            writer.WriteTrailingBits();
        }));
    }

    [Fact]
    public void UnitsHaveASizeFieldAndNoExtension()
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(UnitsHaveASizeFieldAndNoExtension));
        using var output = new PooledPayloadWriter(scope);
        Av1ObuWriter.WriteTemporalDelimiter(output);
        var payload = new byte[200];
        payload[0] = 0x42;
        Av1ObuWriter.Write(output, Av1Constants.ObuFrame, payload);

        // obu_forbidden_bit 0, obu_type 2 (0010), obu_extension_flag 0, obu_has_size_field 1, obu_reserved_1bit 0, then the size
        var data = output.WrittenSpan;
        Assert.Equal([0b0001_0010, 0x00], data[..2].ToArray());

        // obu_type 6 (0110); 200 is C8 01 in leb128
        Assert.Equal([0b0011_0010, 0xC8, 0x01, 0x42], data.Slice(2, 4).ToArray());
        Assert.Equal(2 + 3 + 200, output.Length);

        var first = Av1ObuHeader.Read(data);
        Assert.Equal((Av1Constants.ObuTemporalDelimiter, false, 2, 0), (first.Type, first.HasExtension, first.HeaderLength, first.PayloadLength));
        var second = Av1ObuHeader.Read(data[first.TotalLength..]);
        Assert.Equal((Av1Constants.ObuFrame, false, 3, 200), (second.Type, second.HasExtension, second.HeaderLength, second.PayloadLength));
    }

    private delegate void WriteAction(ref Av1BitWriter writer);

    private static byte[] Write(WriteAction action)
    {
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(Av1BitWriterTests));
        using var output = new PooledPayloadWriter(scope);
        var writer = new Av1BitWriter(output);
        action(ref writer);
        return output.WrittenSpan.ToArray();
    }
}
