namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The field encodings of a JPEG XL codestream (ISO/IEC 18181-1, section 9.2) on a least-significant-bit-first writer.</summary>
internal static class JpegXlBitWriterExtensions
{
    /// <summary>Writes <c>Bool()</c>.</summary>
    public static void WriteBool(this LsbBitWriter writer, bool value) => writer.WriteBits(value ? 1u : 0u, 1);

    /// <summary>Writes <c>U32(d0, d1, d2, d3)</c> with the first distribution that holds the value.</summary>
    public static void WriteU32(this LsbBitWriter writer, uint value, uint offset0, int bits0, uint offset1, int bits1, uint offset2, int bits2, uint offset3, int bits3)
    {
        if (TryWrite(writer, value, 0, offset0, bits0) || TryWrite(writer, value, 1, offset1, bits1) || TryWrite(writer, value, 2, offset2, bits2) || TryWrite(writer, value, 3, offset3, bits3))
            return;

        throw new InvalidOperationException($"The value {value.ToString(CultureInfo.InvariantCulture)} cannot be written with this U32 encoding.");

        static bool TryWrite(LsbBitWriter writer, uint value, uint selector, uint offset, int bits)
        {
            if (value < offset || (ulong)(value - offset) >= 1UL << bits)
                return false;

            writer.WriteBits(selector, 2);
            writer.WriteBits(value - offset, bits);
            return true;
        }
    }

    /// <summary>Writes <c>U64()</c>.</summary>
    public static void WriteU64(this LsbBitWriter writer, ulong value)
    {
        if (value == 0)
        {
            writer.WriteBits(0, 2);
        }
        else if (value <= 16)
        {
            writer.WriteBits(1, 2);
            writer.WriteBits((uint)value - 1, 4);
        }
        else if (value <= 272)
        {
            writer.WriteBits(2, 2);
            writer.WriteBits((uint)value - 17, 8);
        }
        else
        {
            writer.WriteBits(3, 2);
            writer.WriteBits((uint)(value & 0xFFF), 12);
            value >>= 12;
            var shift = 12;
            while (value != 0)
            {
                writer.WriteBits(1, 1);
                if (shift == 60)
                {
                    writer.WriteBits((uint)(value & 0xF), 4);
                    return;
                }

                writer.WriteBits((uint)(value & 0xFF), 8);
                value >>= 8;
                shift += 8;
            }

            writer.WriteBits(0, 1);
        }
    }

    /// <summary>Writes <c>Enum()</c>.</summary>
    public static void WriteEnum(this LsbBitWriter writer, int value) => writer.WriteU32((uint)value, 0, 0, 1, 0, 2, 4, 18, 6);

    /// <summary>Pads with zero bits to the next byte boundary.</summary>
    public static void ZeroPadToByte(this LsbBitWriter writer) => writer.Finish();
}
