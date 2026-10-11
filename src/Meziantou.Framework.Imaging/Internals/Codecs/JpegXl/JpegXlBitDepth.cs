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
}
