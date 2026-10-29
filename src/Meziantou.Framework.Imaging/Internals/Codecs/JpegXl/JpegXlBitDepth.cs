using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>The <c>BitDepth</c> bundle (ISO/IEC 18181-1, section A.3): how the samples of a channel are to be interpreted.</summary>
/// <param name="BitsPerSample">The number of bits of an integer sample, or the total number of bits of a floating-point sample.</param>
/// <param name="ExponentBits">The number of exponent bits of a floating-point sample; 0 for integer samples.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlBitDepth(int BitsPerSample, int ExponentBits)
{
    /// <summary>Gets the default depth: 8-bit integers.</summary>
    public static JpegXlBitDepth Default => new(8, 0);

    /// <summary>Gets a value indicating whether the samples are floating-point numbers.</summary>
    public bool IsFloat => ExponentBits != 0;

    /// <summary>Gets the sample that stands for 1: the largest integer, or the encoding of 1 in the floating-point format.</summary>
    public int One => IsFloat ? ((1 << (ExponentBits - 1)) - 1) << MantissaBits : (int)((1L << BitsPerSample) - 1);

    private int MantissaBits => BitsPerSample - ExponentBits - 1;

    public static JpegXlBitDepth Read(JpegXlBitReader reader)
    {
        if (!reader.ReadBool())
        {
            var bits = (int)reader.ReadU32(8, 0, 10, 0, 12, 0, 1, 6);
            if (bits > 31)
                throw JpegXlFormat.Invalid("the bit depth of integer samples is larger than 31.");

            return new JpegXlBitDepth(bits, 0);
        }

        var floatBits = (int)reader.ReadU32(32, 0, 16, 0, 24, 0, 1, 6);
        var exponentBits = (int)reader.ReadBits(4) + 1;
        var mantissaBits = floatBits - exponentBits - 1;
        if (exponentBits is < 2 or > 8 || mantissaBits is < 2 or > 23)
            throw JpegXlFormat.Invalid("the bit depth of floating-point samples is not valid.");

        return new JpegXlBitDepth(floatBits, exponentBits);
    }

    /// <summary>
    /// Converts samples to floating-point numbers, where 1 is the largest integer sample: integers are scaled by their
    /// depth, floating-point samples are the numbers their bits encode.
    /// </summary>
    /// <param name="source">The samples.</param>
    /// <param name="destination">The converted samples, as many as <paramref name="source"/>.</param>
    public void ToSingle(ReadOnlySpan<int> source, Span<float> destination)
    {
        destination = destination[..source.Length];
        if (!IsFloat)
        {
            // A 31-bit maximum does not fit the arithmetic of 32-bit integers
            var scale = 1f / ((1L << BitsPerSample) - 1);
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = source[i] * scale;
            }

            return;
        }

        if (BitsPerSample == 32)
        {
            // The validation of the depth leaves 8 exponent bits and 23 mantissa bits: the samples are single-precision numbers
            for (var i = 0; i < source.Length; i++)
            {
                destination[i] = BitConverter.Int32BitsToSingle(source[i]);
            }

            return;
        }

        for (var i = 0; i < source.Length; i++)
        {
            destination[i] = ToSingle(source[i]);
        }
    }

    /// <summary>Converts the bits of a floating-point sample of less than 32 bits to the number they encode, as libjxl does.</summary>
    private float ToSingle(int sample)
    {
        var mantissaBits = MantissaBits;
        var signShift = BitsPerSample - 1;
        var bits = (uint)sample;
        var sign = (bits >> signShift) != 0 ? 0x8000_0000u : 0;
        bits &= (1u << signShift) - 1;
        if (bits == 0)
            return BitConverter.UInt32BitsToSingle(sign);

        var exponent = (int)(bits >> mantissaBits);
        var mantissa = (bits & ((1u << mantissaBits) - 1)) << (23 - mantissaBits);
        if (exponent == 0 && ExponentBits < 8)
        {
            // A subnormal number is normal in single precision, whose exponent range is larger
            while ((mantissa & 0x80_0000) == 0)
            {
                mantissa <<= 1;
                exponent--;
            }

            exponent++;
            mantissa &= 0x7F_FFFF;
        }

        exponent += 127 - ((1 << (ExponentBits - 1)) - 1);
        return BitConverter.UInt32BitsToSingle(sign | ((uint)exponent << 23) | mantissa);
    }
}
