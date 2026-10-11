using System.Runtime.InteropServices;
using System.Numerics;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The configuration of a hybrid integer (ISO/IEC 18181-1, section D.3.6): small values are a token of the entropy code,
/// larger ones keep their most and least significant bits in the token and the bits in between follow as raw bits.
/// </summary>
/// <param name="SplitExponent">The base-2 logarithm of the first token that has raw bits.</param>
/// <param name="MsbInToken">The number of most significant bits, after the leading one, kept in the token.</param>
/// <param name="LsbInToken">The number of least significant bits kept in the token.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct JpegXlHybridUintConfig(int SplitExponent, int MsbInToken, int LsbInToken)
{
    public static JpegXlHybridUintConfig Read(JpegXlBitReader reader, int logAlphabetSize)
    {
        var splitExponent = (int)reader.ReadBits(CeilingLog2(logAlphabetSize + 1));
        var msbInToken = 0;
        var lsbInToken = 0;
        if (splitExponent != logAlphabetSize)
        {
            msbInToken = (int)reader.ReadBits(CeilingLog2(splitExponent + 1));
            if (msbInToken > splitExponent)
                throw JpegXlFormat.Invalid("a hybrid integer configuration is not valid.");

            lsbInToken = (int)reader.ReadBits(CeilingLog2(splitExponent - msbInToken + 1));
        }

        if (splitExponent > logAlphabetSize || lsbInToken + msbInToken > splitExponent)
            throw JpegXlFormat.Invalid("a hybrid integer configuration is not valid.");

        return new JpegXlHybridUintConfig(splitExponent, msbInToken, lsbInToken);
    }

    /// <summary>Completes the value of a token with the raw bits that follow it.</summary>
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public uint ReadValue(JpegXlBitReader reader, uint token)
    {
        var split = 1u << SplitExponent;
        if (token < split)
            return token;

        var bitsInToken = MsbInToken + LsbInToken;

        // A stream whose tokens ask for more than 29 raw bits is not valid; the count is masked like the reference decoder does
        var count = (int)(((uint)(SplitExponent - bitsInToken) + ((token - split) >> bitsInToken)) & 31);
        var low = token & ((1u << LsbInToken) - 1);
        token >>= LsbInToken;
        var bits = reader.ReadBits(count);
        return (((((1u << MsbInToken) | (token & ((1u << MsbInToken) - 1))) << count) | bits) << LsbInToken) | low;
    }

    /// <summary>Returns the smallest number of bits that can hold values below <paramref name="value"/> (the ceiling of its base-2 logarithm).</summary>
    public static int CeilingLog2(int value) => value <= 1 ? 0 : BitOperations.Log2((uint)value - 1) + 1;
}
