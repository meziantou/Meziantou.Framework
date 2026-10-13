using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The descriptors of Rec. ITU-T H.265 (clauses 7.2 and 9.2): every expected value is worked out by hand from the
/// definition of the descriptor, on bit patterns written out in the comments.
/// </summary>
public sealed class HevcBitReaderTests
{
    [Fact]
    public void FixedWidthNumbersAreReadMostSignificantBitFirst()
    {
        // 1 011 0010 | 0100 1111 1010 | 0101 0011 1100
        var reader = new HevcBitReader([0b1011_0010, 0b0100_1111, 0b1010_0101, 0b0011_1100]);
        Assert.Equal(32, reader.RemainingBits);
        Assert.True(reader.IsByteAligned);
        Assert.True(reader.ReadFlag());
        Assert.False(reader.IsByteAligned);
        Assert.Equal(0b011u, reader.ReadBits(3));
        Assert.Equal(0b0010u, reader.ReadBits(4));
        Assert.Equal(8, reader.BitPosition);
        Assert.Equal(0u, reader.ReadBits(0));
        Assert.Equal(0b0100_1111_1010u, reader.ReadBits(12));
        Assert.Equal(3, reader.BytePosition);
        Assert.Equal(0b0101_0011_1100u, reader.ReadBits(12));
        Assert.Equal(0, reader.RemainingBits);

        // 32 bits after 7 other bits: 0xDEADBEEF << 1 = 0x1_BD5B_7DDE after seven 1 bits
        var shifted = new HevcBitReader([0xFF, 0xBD, 0x5B, 0x7D, 0xDE]);
        Assert.Equal(0x7Fu, shifted.ReadBits(7));
        Assert.Equal(0xDEADBEEFu, shifted.ReadBits(32));
    }

    [Fact]
    public void UnsignedExpGolombCodesAreTheCodesOfTable9_2()
    {
        // Table 9-2: 1 | 010 | 011 | 00100 | 00101 | 00110 | 00111 | 0001000 | 0001001, then padding
        // 1010 0110 | 0100 0010 | 1001 1000 | 1110 0010 | 0000 0100 | 1000 0000
        var reader = new HevcBitReader([0b1010_0110, 0b0100_0010, 0b1001_1000, 0b1110_0010, 0b0000_0100, 0b1000_0000]);
        for (var expected = 0u; expected <= 8; expected++)
        {
            Assert.Equal(expected, reader.ReadUE());
        }

        Assert.Equal(41, reader.BitPosition);

        // The largest value, 2^32 - 2: 31 zero bits, a one bit, then 31 one bits
        var largest = new HevcBitReader([0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0xFE]);
        Assert.Equal(uint.MaxValue - 1, largest.ReadUE());
        Assert.Equal(63, largest.BitPosition);
    }

    [Fact]
    public void SignedExpGolombCodesAlternateTheSigns()
    {
        // Table 9-3: the code numbers 0 to 6 are 0, 1, -1, 2, -2, 3, -3
        // 1 | 010 | 011 | 00100 | 00101 | 00110 | 00111 -> 1010 0110 | 0100 0010 | 1001 1000 | 111
        var reader = new HevcBitReader([0b1010_0110, 0b0100_0010, 0b1001_1000, 0b1110_0000]);
        int[] expected = [0, 1, -1, 2, -2, 3, -3];
        foreach (var value in expected)
        {
            Assert.Equal(value, reader.ReadSE());
        }

        // The code numbers 2^32 - 3 and 2^32 - 2 are the largest and the smallest values: 2^31 - 1 and -(2^31 - 1)
        var extremes = new HevcBitReader([0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0xFC, 0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0xFE]);
        Assert.Equal(int.MaxValue, extremes.ReadSE());
        extremes.Skip(1);
        Assert.Equal(-int.MaxValue, extremes.ReadSE());
    }

    [Fact]
    public void RangesOfExpGolombCodesAreChecked()
    {
        // 00101 is 4
        Assert.Equal(4, new HevcBitReader([0b0010_1000]).ReadUE(4, "x"));
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0b0010_1000]).ReadUE(3, "x"));

        // 00101 is -2
        Assert.Equal(-2, new HevcBitReader([0b0010_1000]).ReadSE(-2, 2, "x"));
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0b0010_1000]).ReadSE(-1, 2, "x"));
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0b0010_0000]).ReadSE(-2, 1, "x"));
    }

    [Fact]
    public void APrefixOfMoreThan32ZeroBitsIsNotACode()
    {
        // 32 zero bits and a one bit: the value would be 2^32 - 1 or more
        var exception = Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0, 0, 0, 0, 0x80, 0, 0, 0, 0]).ReadUE());
        Assert.Equal(ImageFormat.Heic, exception.Format);

        // 33 zero bits: the prefix is not read further
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader(new byte[64]).ReadUE());
    }

    [Fact]
    public void ReadingPastTheEndIsAnError()
    {
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0xFF, 0xFF]).ReadBits(17));
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([]).ReadFlag());
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0xFF]).Skip(9));

        // The suffix of a code is missing: 0001 needs three more bits
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0b0000_0001]).ReadUE());

        // Only zero bits: the prefix never ends
        Assert.Throws<InvalidImageContentException>(() => new HevcBitReader([0, 0]).ReadUE());
    }

    [Fact]
    public void MoreRbspDataIsTrueBeforeTheStopBit()
    {
        // 101 then the stop bit and four alignment bits: 1011 0000
        var reader = new HevcBitReader([0b1011_0000]);
        Assert.True(reader.MoreRbspData());
        reader.Skip(3);
        Assert.False(reader.MoreRbspData());
        reader.ReadRbspTrailingBits();
        Assert.Equal(0, reader.RemainingBits);

        // The stop bit is the last bit of the second byte: the first byte and seven bits are data
        var aligned = new HevcBitReader([0x00, 0b0000_0001]);
        aligned.Skip(14);
        Assert.True(aligned.MoreRbspData());
        aligned.Skip(1);
        Assert.False(aligned.MoreRbspData());
        aligned.ReadRbspTrailingBits();

        // Extension data are skipped up to the stop bit
        var extension = new HevcBitReader([0xAB, 0xCD, 0b0110_1000]);
        extension.Skip(3);
        extension.SkipExtensionData();
        Assert.Equal(20, extension.BitPosition);
        extension.ReadRbspTrailingBits();
    }

    [Fact]
    public void TrailingBitsAreAOneBitThenZeroBitsUpToTheEnd()
    {
        // No stop bit
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var reader = new HevcBitReader([0b0100_0000]);
            reader.ReadRbspTrailingBits();
        });

        // An alignment bit that is not zero
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var reader = new HevcBitReader([0b1000_0100]);
            reader.ReadRbspTrailingBits();
        });

        // A byte after the trailing bits
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var reader = new HevcBitReader([0b1000_0000, 0x00]);
            reader.ReadRbspTrailingBits();
        });

        // No bit at all
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var reader = new HevcBitReader([0xFF]);
            reader.Skip(8);
            reader.ReadRbspTrailingBits();
        });
    }

    [Fact]
    public void ByteAlignmentIsAOneBitThenZeroBitsUpToTheNextByte()
    {
        // 11 then alignment_bit_equal_to_one and five zero bits; the next byte is not read
        var reader = new HevcBitReader([0b1110_0000, 0xFF]);
        reader.Skip(2);
        reader.ReadByteAlignment();
        Assert.Equal(8, reader.BitPosition);

        // On a byte: a whole byte 1000 0000
        reader = new HevcBitReader([0x80, 0xFF]);
        reader.ReadByteAlignment();
        Assert.Equal(8, reader.BitPosition);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            var invalid = new HevcBitReader([0b1100_0000]);
            invalid.Skip(2);
            invalid.ReadByteAlignment();
        });
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var invalid = new HevcBitReader([0b1110_0100]);
            invalid.Skip(2);
            invalid.ReadByteAlignment();
        });
    }
}
