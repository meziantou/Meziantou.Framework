using System.Runtime.CompilerServices;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The sRGB transfer function of <see cref="SrgbTransfer"/>, evaluated without any function of the platform, for the
/// values that filters convert one by one: the same bits on every platform and framework version, and several times
/// faster than <see cref="Math.Pow(double, double)"/>.
/// </summary>
/// <remarks>
/// <para>
/// The linear segments are computed as <see cref="SrgbTransfer"/> computes them. The power of the other segment
/// (<c>x^(5/12)</c> to encode, <c>x^(12/5)</c> to decode, <c>x</c> being the base of the power of
/// <see cref="SrgbTransfer"/>) is split by the exponent of its base, <c>x = m 2^e</c> with <c>m</c> in [1, 2):
/// <c>2^(p e)</c> comes from a table of the exponents the function reaches, and <c>m^p</c> from a polynomial of degree 5
/// on each of 256 equal segments of [1, 2), the Taylor series of <c>m^p</c> at the start of the segment, evaluated
/// with the fraction of the segment, which is exact (the low 44 bits of the significand).
/// </para>
/// <para>
/// The tables are built once per process (static, shared, 24 KB, never charged to an image scope) with additions,
/// multiplications and divisions only, which are correctly rounded on every platform: <c>m^(5/12)</c> is the twelfth
/// root of <c>m^5</c>, <c>m^(12/5)</c> is <c>m^2</c> times the fifth root of <c>m^2</c> (powers of the starts of the
/// segments, which have 9 significant bits, so they are exact), and the roots are found by Newton's method, from above.
/// </para>
/// <para>
/// The remainder of the series is at most <c>h^6 / 720</c> times the largest sixth derivative on the segment, with
/// <c>h = 1/256</c>: <c>8e-17</c> of the value for <c>m^(5/12)</c> and <c>2e-17</c> for <c>m^(12/5)</c>, below the
/// rounding of a double. Measured against the exact function (integer roots of big integers) on 22 million inputs, the
/// largest error is <c>3.3e-16</c> to encode and <c>7.2e-16</c> to decode, where <see cref="SrgbTransfer"/> has
/// <c>2.8e-16</c> and <c>4.4e-16</c> (on macOS Arm64); on every float from 0 to 1, the result rounded to a float, as
/// filters keep it, is the one of <see cref="SrgbTransfer"/> there. The tests check a bound of <c>1e-15</c> against the decimal reference.
/// </para>
/// </remarks>
internal static class DeterministicSrgbTransfer
{
    // The bits of the significand that number the segments of [1, 2), and those of the fraction of a segment
    private const int SegmentBits = 8;
    private const int SegmentCount = 1 << SegmentBits;
    private const int FractionBits = 52 - SegmentBits;
    private const long FractionMask = (1L << FractionBits) - 1;
    private const double FractionScale = 1.0 / (1L << FractionBits);

    // The number of coefficients of the polynomial of a segment
    private const int CoefficientCount = 6;

    // The smallest exponents of the bases of the powers: 2^-9 < 0.0031308 to encode, 2^-4 < (0.04045 + 0.055) / 1.055 to decode
    private const int EncodeMinExponent = -9;
    private const int DecodeMinExponent = -4;

    private static PowerTable? s_encode;
    private static PowerTable? s_decode;

    /// <summary>Converts an encoded value in [0, 1] (or not a number) to linear light.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Decode(double value)
    {
        if (!(value > 0.04045))
            return value / 12.92;

        return (s_decode ??= CreateDecodeTable()).Evaluate((value + 0.055) / 1.055);
    }

    /// <summary>Converts linear light in [0, 1] (or not a number) to an encoded value.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static double Encode(double value)
    {
        if (!(value > 0.0031308))
            return value * 12.92;

        return (s_encode ??= CreateEncodeTable()).Evaluate(value) - 0.055;
    }

    // x^(5/12), multiplied by 1.055
    private static PowerTable CreateEncodeTable()
    {
        var scales = new double[1 - EncodeMinExponent];
        for (var exponent = EncodeMinExponent; exponent <= 0; exponent++)
        {
            scales[exponent - EncodeMinExponent] = 1.055 * Root(Math.ScaleB(1, 5 * exponent), 12);
        }

        return new PowerTable(CreateSegments(m => Root(m * m * m * m * m, 12), 5.0 / 12), scales, EncodeMinExponent);
    }

    // x^(12/5)
    private static PowerTable CreateDecodeTable()
    {
        var scales = new double[1 - DecodeMinExponent];
        for (var exponent = DecodeMinExponent; exponent <= 0; exponent++)
        {
            var square = Math.ScaleB(1, 2 * exponent);
            scales[exponent - DecodeMinExponent] = square * Root(square, 5);
        }

        return new PowerTable(CreateSegments(m => m * m * Root(m * m, 5), 12.0 / 5), scales, DecodeMinExponent);
    }

    /// <summary>Gets the coefficients of the polynomial of each segment of [1, 2), in the fraction of the segment, from the constant one.</summary>
    /// <param name="power">The power <c>m^p</c>, to the last bit or so for the starts of the segments.</param>
    /// <param name="exponent">The exponent <c>p</c>.</param>
    private static double[] CreateSegments(Func<double, double> power, double exponent)
    {
        var coefficients = new double[CoefficientCount * SegmentCount];
        for (var segment = 0; segment < SegmentCount; segment++)
        {
            // The Taylor series of (m0 + h t)^p at t = 0: m0^p times the binomial coefficients of p, times (h / m0)^k
            var start = 1 + ((double)segment / SegmentCount);
            var ratio = 1.0 / SegmentCount / start;
            var coefficient = power(start);
            for (var k = 0; k < CoefficientCount; k++)
            {
                coefficients[(CoefficientCount * segment) + k] = coefficient;
                coefficient = coefficient * (exponent - k) / (k + 1) * ratio;
            }
        }

        return coefficients;
    }

    /// <summary>Gets the positive root <c>y</c> of <c>y^n = value</c>, for a positive value, to the last bit or so.</summary>
    /// <remarks>
    /// Newton's method from the tangent at 1 of <c>value^(1/n)</c>, which is above the root: the iterates decrease to the
    /// root, and the last one that decreases is the result.
    /// </remarks>
    private static double Root(double value, int n)
    {
        var root = 1 + ((value - 1) / n);
        while (true)
        {
            var power = root;
            for (var i = 2; i < n; i++)
            {
                power *= root;
            }

            var next = root - (((power * root) - value) / (n * power));
            if (!(next < root))
                return root;

            root = next;
        }
    }

    /// <summary>A power of a value from 2^MinExponent (included) to 2 (excluded): a polynomial per segment of the significand, times a power of two per exponent.</summary>
    private sealed class PowerTable
    {
        // The coefficients of the polynomial of each segment, from the constant one, and the power of two of each exponent
        private readonly double[] _coefficients;
        private readonly double[] _scales;
        private readonly int _minExponent;

        public PowerTable(double[] coefficients, double[] scales, int minExponent)
        {
            _coefficients = coefficients;
            _scales = scales;
            _minExponent = minExponent;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double Evaluate(double value)
        {
            var bits = BitConverter.DoubleToInt64Bits(value);
            var coefficients = _coefficients.AsSpan(CoefficientCount * ((int)(bits >> FractionBits) & (SegmentCount - 1)), CoefficientCount);
            var fraction = (bits & FractionMask) * FractionScale;
            var polynomial = coefficients[5];
            polynomial = coefficients[4] + (fraction * polynomial);
            polynomial = coefficients[3] + (fraction * polynomial);
            polynomial = coefficients[2] + (fraction * polynomial);
            polynomial = coefficients[1] + (fraction * polynomial);
            polynomial = coefficients[0] + (fraction * polynomial);
            return polynomial * _scales[(int)(bits >> 52) - 1023 - _minExponent];
        }
    }
}
