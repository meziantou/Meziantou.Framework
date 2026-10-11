using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The descriptors of the AV1 specification (section 4.10): every expected value is worked out by hand from the definition of
/// the descriptor, on bit patterns written out in the comments.
/// </summary>
public sealed class Av1BitReaderTests
{
    [Fact]
    public void FixedWidthNumbersAreReadMostSignificantBitFirst()
    {
        // 1 011 0010 | 0100 1111 1010 | 0101 0011 1100
        var reader = new Av1BitReader([0b1011_0010, 0b0100_1111, 0b1010_0101, 0b0011_1100]);
        Assert.Equal(32, reader.RemainingBits);
        Assert.True(reader.ReadFlag());
        Assert.Equal(0b011u, reader.ReadBits(3));
        Assert.Equal(0b0010u, reader.ReadBits(4));
        Assert.Equal(8, reader.BitPosition);
        Assert.Equal(0u, reader.ReadBits(0));
        Assert.Equal(8, reader.BitPosition);
        Assert.Equal(0b0100_1111_1010u, reader.ReadBits(12));
        Assert.Equal(20, reader.BitPosition);
        Assert.Equal(3, reader.BytePosition); // the third byte is partly consumed
        Assert.Equal(12, reader.RemainingBits);
        Assert.Equal(0b0101_0011_1100u, reader.ReadBits(12));
        Assert.Equal(0, reader.RemainingBits);
        Assert.Equal(4, reader.BytePosition);
    }

    [Fact]
    public void ThirtyTwoBitsAreReadAtAnyBitOffset()
    {
        var aligned = new Av1BitReader([0xDE, 0xAD, 0xBE, 0xEF]);
        Assert.Equal(0xDEADBEEFu, aligned.ReadBits(32));

        // The same 32 bits after 4 and after 7 other bits: 5 bytes are involved
        var shiftedBy4 = new Av1BitReader([0x0D, 0xEA, 0xDB, 0xEE, 0xF0]);
        Assert.Equal(0u, shiftedBy4.ReadBits(4));
        Assert.Equal(0xDEADBEEFu, shiftedBy4.ReadBits(32));

        // 0xDEADBEEF << 1 = 0x1_BD5B_7DDE, after seven 1 bits: 1111111 1 1011 1101 0101 1011 0111 1101 1101 1110
        var shiftedBy7 = new Av1BitReader([0xFF, 0xBD, 0x5B, 0x7D, 0xDE]);
        Assert.Equal(0x7Fu, shiftedBy7.ReadBits(7));
        Assert.Equal(0xDEADBEEFu, shiftedBy7.ReadBits(32));
        Assert.Equal(1, shiftedBy7.RemainingBits);
    }

    [Fact]
    public void SignedNumbersAreTwoComplementOfTheirWidth()
    {
        // su(4): 1111 = -1, 0111 = 7, 1000 = -8; su(7): 1000000 = -64; su(2): 01 = 1, 10 = -2; su(1): 1 = -1
        // 1111 0111 | 1000 1000 | 000 01 10 1
        var reader = new Av1BitReader([0b1111_0111, 0b1000_1000, 0b0000_1101]);
        Assert.Equal(-1, reader.ReadSigned(4));
        Assert.Equal(7, reader.ReadSigned(4));
        Assert.Equal(-8, reader.ReadSigned(4));
        Assert.Equal(-64, reader.ReadSigned(7));
        Assert.Equal(1, reader.ReadSigned(2));
        Assert.Equal(-2, reader.ReadSigned(2));
        Assert.Equal(-1, reader.ReadSigned(1));
        Assert.Equal(0, reader.RemainingBits);

        var widest = new Av1BitReader([0xFF, 0xFF, 0xFF, 0xFF, 0x80, 0x00, 0x00, 0x00, 0x7F, 0xFF, 0xFF, 0xFF]);
        Assert.Equal(-1, widest.ReadSigned(32));
        Assert.Equal(int.MinValue, widest.ReadSigned(32));
        Assert.Equal(int.MaxValue, widest.ReadSigned(32));
    }

    [Fact]
    public void NonSymmetricNumbersUseTheShortCodesFirst()
    {
        // ns(5): w = 3, m = 8 - 5 = 3. The values below m take w - 1 = 2 bits (00, 01, 10); the others take 3 bits:
        // v = 11 (3), then (v << 1) - m + extra_bit = 3 + extra_bit, so 110 = 3 and 111 = 4
        // 00 01 10 110 111 = 0001 1011 0111
        var five = new Av1BitReader([0b0001_1011, 0b0111_0000]);
        Assert.Equal(0, five.ReadNonSymmetric(5));
        Assert.Equal(1, five.ReadNonSymmetric(5));
        Assert.Equal(2, five.ReadNonSymmetric(5));
        Assert.Equal(3, five.ReadNonSymmetric(5));
        Assert.Equal(4, five.ReadNonSymmetric(5));
        Assert.Equal(12, five.BitPosition);

        // ns(1): w = 1, m = 1, v = f(0) = 0: no bit is read
        var one = new Av1BitReader([0xFF]);
        Assert.Equal(0, one.ReadNonSymmetric(1));
        Assert.Equal(0, one.BitPosition);

        // ns(8): w = 4, m = 8: every value takes 3 bits. ns(3): w = 2, m = 1: 0 = "0", 1 = "10", 2 = "11"
        // 101 111 0 10 11 = 1011 1101 011
        var mixed = new Av1BitReader([0b1011_1101, 0b0110_0000]);
        Assert.Equal(5, mixed.ReadNonSymmetric(8));
        Assert.Equal(7, mixed.ReadNonSymmetric(8));
        Assert.Equal(0, mixed.ReadNonSymmetric(3));
        Assert.Equal(1, mixed.ReadNonSymmetric(3));
        Assert.Equal(2, mixed.ReadNonSymmetric(3));
        Assert.Equal(11, mixed.BitPosition);
    }

    [Fact]
    public void VariableLengthNumbersCountTheirLeadingZeros()
    {
        // uvlc: n leading zeros, a one, then n bits; the value is those n bits + 2^n - 1
        // "1" = 0, "010" = 1, "011" = 2, "00100" = 3, "00111" = 6: 1 010 011 00100 00111 = 1010 0110 0100 0011 1
        var reader = new Av1BitReader([0b1010_0110, 0b0100_0011, 0b1000_0000]);
        Assert.Equal(0u, reader.ReadUvlc());
        Assert.Equal(1u, reader.ReadUvlc());
        Assert.Equal(2u, reader.ReadUvlc());
        Assert.Equal(3u, reader.ReadUvlc());
        Assert.Equal(6u, reader.ReadUvlc());
        Assert.Equal(17, reader.BitPosition);

        // 31 leading zeros, a one, then 31 ones: 2^31 - 1 + 2^31 - 1 = 2^32 - 2
        var largest = new Av1BitReader([0x00, 0x00, 0x00, 0x01, 0xFF, 0xFF, 0xFF, 0xFE]);
        Assert.Equal(uint.MaxValue - 1, largest.ReadUvlc());
        Assert.Equal(63, largest.BitPosition);

        // 32 leading zeros or more: 2^32 - 1, and nothing is read after the one bit
        var saturated = new Av1BitReader([0x00, 0x00, 0x00, 0x00, 0x00, 0x40, 0xFF]);
        Assert.Equal(uint.MaxValue, saturated.ReadUvlc());
        Assert.Equal(42, saturated.BitPosition);
    }

    [Fact]
    public void LittleEndianNumbersAreReadByteByByte()
    {
        var reader = new Av1BitReader([0x12, 0x34, 0x12, 0x56, 0x34, 0x12, 0x78, 0x56, 0x34, 0x12]);
        Assert.Equal(0x12u, reader.ReadLittleEndian(1));
        Assert.Equal(0x1234u, reader.ReadLittleEndian(2));
        Assert.Equal(0x123456u, reader.ReadLittleEndian(3));
        Assert.Equal(0x12345678u, reader.ReadLittleEndian(4));
        Assert.Equal(0, reader.RemainingBits);

        var largest = new Av1BitReader([0xFF, 0xFF, 0xFF, 0xFF]);
        Assert.Equal(uint.MaxValue, largest.ReadLittleEndian(4));
    }

    [Theory]
    [InlineData(new byte[] { 0x00 }, 0u, 1)]
    [InlineData(new byte[] { 0x7F }, 127u, 1)]
    [InlineData(new byte[] { 0x80, 0x01 }, 128u, 2)] // 0 + (1 << 7)
    [InlineData(new byte[] { 0xE5, 0x8E, 0x26 }, 624485u, 3)] // 0x65 + (0x0E << 7) + (0x26 << 14)
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x0F }, uint.MaxValue, 5)] // 28 one bits, then 4 more
    [InlineData(new byte[] { 0x81, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 }, 1u, 8)] // the longest form: 8 bytes, padded with zero groups
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x88, 0x80, 0x80, 0x00 }, 0x80000000u, 8)] // 8 << 28 in the fifth group
    [InlineData(new byte[] { 0x05, 0xFF }, 5u, 1)] // stops at the first byte without the continuation bit
    public void Leb128NumbersAreReadLeastSignificantGroupFirst(byte[] data, uint expected, int expectedLength)
    {
        var reader = new Av1BitReader(data);
        Assert.Equal(expected, reader.ReadLeb128());
        Assert.Equal(expectedLength * 8, reader.BitPosition);
    }

    [Theory]
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x10 })] // 1 << 32: one more than the largest value
    [InlineData(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0x1F })] // 2^33 - 1
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x01 })] // 1 << 49 in the eighth byte
    [InlineData(new byte[] { 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x80, 0x00 })] // the eighth byte announces a ninth one
    [InlineData(new byte[] { 0x80 })] // truncated after a continuation bit
    [InlineData(new byte[] { 0xFF, 0xFF })]
    [InlineData(new byte[] { })]
    public void InvalidLeb128NumbersAreRejected(byte[] data)
    {
        var exception = Assert.Throws<InvalidImageContentException>(() =>
        {
            var reader = new Av1BitReader(data);
            reader.ReadLeb128();
        });
        Assert.Equal(ImageFormat.Avif, exception.Format);
    }

    [Fact]
    public void MissingBitsAreNeverReadAsZeros()
    {
        byte[] data = [0xAB, 0xCD];
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader(data).ReadBits(17));
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader([]).ReadFlag());
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader(data).ReadSigned(32));
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader(data).ReadLittleEndian(3));
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader(data).Skip(17));

        // ns(200): w = 8, m = 56. The 7 bits left after 9 bits are 100 1101 = 77 >= m: the extra bit is missing
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var truncated = new Av1BitReader(data);
            truncated.Skip(9);
            truncated.ReadNonSymmetric(200);
        });

        // uvlc: the one bit that ends the leading zeros is missing, or the value bits are
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader([0x00, 0x00]).ReadUvlc());
        Assert.Throws<InvalidImageContentException>(() => new Av1BitReader([0x01]).ReadUvlc());

        // A read that fits is not affected by the end of the data
        var reader = new Av1BitReader(data);
        Assert.Equal(0xABCDu, reader.ReadBits(16));
        var exception = Assert.Throws<InvalidImageContentException>(() =>
        {
            var exhausted = new Av1BitReader(data);
            exhausted.Skip(16);
            exhausted.ReadBits(1);
        });
        Assert.Equal(ImageFormat.Avif, exception.Format);
    }

    [Fact]
    public void SkippingAndAligning()
    {
        var reader = new Av1BitReader([0xFF, 0b0000_0101, 0xFF]);
        reader.ByteAlign(); // already aligned
        Assert.Equal(0, reader.BitPosition);
        reader.Skip(13);
        Assert.Equal(0b101u, reader.ReadBits(3));
        reader.Skip(1);
        reader.ByteAlign();
        Assert.Equal(24, reader.BitPosition);
        Assert.Equal(0, reader.RemainingBits);
    }

    [Fact]
    public void TrailingBitsStartWithAOne()
    {
        // trailing_bits(): a one bit, then zero bits up to the end of the payload
        var reader = new Av1BitReader([0b1011_0000, 0x00]);
        Assert.Equal(0b101u, reader.ReadBits(3));
        reader.ReadTrailingBits();
        Assert.Equal(0, reader.RemainingBits);

        Assert.Throws<InvalidImageContentException>(() =>
        {
            var missing = new Av1BitReader([0b1010_0000]);
            missing.ReadBits(3);
            missing.ReadTrailingBits();
        });
        Assert.Throws<InvalidImageContentException>(() =>
        {
            var exhausted = new Av1BitReader([0xFF]);
            exhausted.ReadBits(8);
            exhausted.ReadTrailingBits();
        });
    }
}
