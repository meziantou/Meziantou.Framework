namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the encoder: the inverses of the transforms of the decoding process (AV1 specification,
/// section 7.13), which is the only direction that the specification describes.
/// </summary>
internal static class Av1ForwardTransform
{
    // The fractional bits of the forward bases, and of the intermediate values after the column pass
    private const int BasisBits = 20;
    private const int IntermediateBits = 8;

    private static readonly int[]?[] DctBases = CreateBases(Av1InverseTransform.Kind.Dct, 6);
    private static readonly int[]?[] AdstBases = CreateBases(Av1InverseTransform.Kind.Adst, 4);
    private static readonly int[] SquaredGains = MeasureSquaredGains();

    /// <summary>
    /// Returns the squared gain of the transforms of a size, with 8 fractional bits: the ratio between the sum of the
    /// squares of the coefficients of a residual and the sum of the squares of its samples. The transforms of a size have
    /// the same gain whatever their type, so a squared error of coefficients divided by the gain is the squared error of
    /// the samples, which is how the transform types of a block are compared without reconstructing it.
    /// </summary>
    public static int GetSquaredGain(int txSz) => SquaredGains[txSz];

    /// <summary>
    /// Transforms the residual of a transform block into coefficients, in the scale of the dequantized coefficients: the
    /// coefficients that the inverse transform process (section 7.13.3) turns back into the residual, up to its rounding.
    /// </summary>
    /// <remarks>
    /// <para>
    /// The one-dimensional transforms of the specification are orthogonal up to the precision of their integer rotations:
    /// the forward transform of a kernel is the transposed kernel, divided by its gain. The bases are not written out: they
    /// are measured on the inverse kernels of the decoder (the response to each coefficient), so that the two directions
    /// cannot disagree. The transform is a plain product by the basis, in integers.
    /// </para>
    /// <para>
    /// The inverse process scales the coefficients of a 2:1 rectangle by 1/sqrt(2), shifts the output of the row transforms
    /// by <c>Transform_Row_Shift</c> and the output of the column transforms by 4, and mirrors the residual of the
    /// <c>FLIPADST</c> types: the forward transform undoes each of these.
    /// </para>
    /// </remarks>
    /// <param name="residual">The residual: height rows of width samples.</param>
    /// <param name="coefficients"><c>Dequant</c>: Min(32, height) rows of Min(32, width) coefficients.</param>
    /// <param name="txSz">The transform size.</param>
    /// <param name="txType">The transform type (<c>PlaneTxType</c>).</param>
    public static void Forward(ReadOnlySpan<int> residual, Span<int> coefficients, int txSz, int txType)
    {
        var log2W = Av1Tables.TxWidthLog2[txSz];
        var log2H = Av1Tables.TxHeightLog2[txSz];
        var w = 1 << log2W;
        var h = 1 << log2H;
        var tw = Math.Min(32, w);
        var th = Math.Min(32, h);
        var rowShift = Av1Tables.TransformRowShift[txSz];
        var isRectangular2To1 = Math.Abs(log2W - log2H) is 1;
        var rowKind = Av1InverseTransform.GetRowKind(txType);
        var colKind = Av1InverseTransform.GetColumnKind(txType);
        var flipUD = txType is Av1Constants.FlipadstDct or Av1Constants.FlipadstAdst or Av1Constants.VFlipadst or Av1Constants.FlipadstFlipadst;
        var flipLR = txType is Av1Constants.DctFlipadst or Av1Constants.AdstFlipadst or Av1Constants.HFlipadst or Av1Constants.FlipadstFlipadst;

        // The columns: th outputs per column, with IntermediateBits fractional bits
        Span<long> intermediate = stackalloc long[th * w];
        var colBasis = GetBasis(colKind, log2H);
        var colIdentity = GetIdentityMultiplier(log2H);
        for (var c = 0; c < w; c++)
        {
            var sourceColumn = flipLR ? w - 1 - c : c;
            for (var k = 0; k < th; k++)
            {
                long sum;
                if (colBasis is null)
                {
                    sum = residual[(k * w) + sourceColumn] * colIdentity;
                }
                else
                {
                    sum = 0;
                    var basis = colBasis.AsSpan(k * h, h);
                    if (flipUD)
                    {
                        for (var r = 0; r < h; r++)
                        {
                            sum += (long)residual[((h - 1 - r) * w) + sourceColumn] * basis[r];
                        }
                    }
                    else
                    {
                        for (var r = 0; r < h; r++)
                        {
                            sum += (long)residual[(r * w) + sourceColumn] * basis[r];
                        }
                    }
                }

                intermediate[(k * w) + c] = Round(sum, BasisBits - IntermediateBits);
            }
        }

        // The rows, then the scale of the inverse process: 2^(4 + rowShift), and sqrt(2) for a 2:1 rectangle
        var rowBasis = GetBasis(rowKind, log2W);
        var rowIdentity = GetIdentityMultiplier(log2W);
        var shift = BasisBits + IntermediateBits - 4 - rowShift;
        for (var k = 0; k < th; k++)
        {
            var line = intermediate.Slice(k * w, w);
            for (var l = 0; l < tw; l++)
            {
                long sum;
                if (rowBasis is null)
                {
                    sum = line[l] * rowIdentity;
                }
                else
                {
                    sum = 0;
                    var basis = rowBasis.AsSpan(l * w, w);
                    for (var c = 0; c < w; c++)
                    {
                        sum += line[c] * basis[c];
                    }
                }

                if (isRectangular2To1)
                    sum = Round(sum * 5793, 12);

                coefficients[(k * tw) + l] = (int)Round(sum, shift);
            }
        }
    }

    private static int[]? GetBasis(Av1InverseTransform.Kind kind, int n) => kind switch
    {
        Av1InverseTransform.Kind.Dct => DctBases[n],
        Av1InverseTransform.Kind.Adst => AdstBases[n],
        _ => null,
    };

    // The inverse of the scale of the inverse identity transform (section 7.13.2.15): sqrt(2), 2, 2 sqrt(2) and 4
    private static long GetIdentityMultiplier(int n) => n switch
    {
        2 => (4096L << BasisBits) / 5793,
        3 => 1L << (BasisBits - 1),
        4 => (4096L << BasisBits) / 11586,
        _ => 1L << (BasisBits - 2),
    };

    private static long Round(long value, int bits) => (value + (1L << (bits - 1))) >> bits;

    // The forward bases of the kernels of 2^2 to 2^maxN points: for each of the first 32 coefficients, the response of the
    // inverse kernel to this coefficient alone, divided by its squared norm
    private static int[]?[] CreateBases(Av1InverseTransform.Kind kind, int maxN)
    {
        const int Impulse = 1 << 14;
        var bases = new int[]?[maxN + 1];
        Span<int> t = stackalloc int[64];
        for (var n = 2; n <= maxN; n++)
        {
            var length = 1 << n;
            var count = Math.Min(length, 32);
            var basis = new int[count * length];
            for (var k = 0; k < count; k++)
            {
                var response = t[..length];
                response.Clear();
                response[k] = Impulse;
                if (kind is Av1InverseTransform.Kind.Dct)
                {
                    Av1InverseTransform.InverseDct(response, n, 24);
                }
                else
                {
                    Av1InverseTransform.InverseAdst(response, n, 24);
                }

                long norm = 0;
                foreach (var value in response)
                {
                    norm += (long)value * value;
                }

                // response / Impulse is the basis function b: b / |b|^2 with BasisBits fractional bits
                for (var i = 0; i < length; i++)
                {
                    var numerator = (long)response[i] * Impulse << BasisBits;
                    basis[(k * length) + i] = (int)((numerator + (numerator < 0 ? -norm / 2 : norm / 2)) / norm);
                }
            }

            bases[n] = basis;
        }

        return bases;
    }

    // The gains are measured like the bases: on the transform of a residual, here a fixed sequence of samples
    private static int[] MeasureSquaredGains()
    {
        var gains = new int[Av1Constants.TxSizesAll];
        Span<int> residual = stackalloc int[64 * 64];
        Span<int> coefficients = stackalloc int[32 * 32];
        for (var txSz = 0; txSz < gains.Length; txSz++)
        {
            int w = Av1Tables.TxWidth[txSz];
            int h = Av1Tables.TxHeight[txSz];
            var state = 12345u;
            long samples = 0;
            for (var i = 0; i < w * h; i++)
            {
                // A linear congruential generator: the same samples on every platform
                state = (state * 1664525) + 1013904223;
                residual[i] = (int)(state >> 24) - 128;
                samples += (long)residual[i] * residual[i];
            }

            var count = Math.Min(32, w) * Math.Min(32, h);
            Forward(residual[..(w * h)], coefficients[..count], txSz, Av1Constants.DctDct);
            long energy = 0;
            foreach (var coefficient in coefficients[..count])
            {
                energy += (long)coefficient * coefficient;
            }

            gains[txSz] = (int)Math.Max(1, ((energy << 8) + (samples / 2)) / samples);
        }

        return gains;
    }

    /// <summary>
    /// Transforms the 4x4 residual of a lossless block into the coefficients that the inverse Walsh-Hadamard transform turns
    /// back into exactly the same residual (the inverse of section 7.13.3 with <c>Lossless</c> equal to 1).
    /// </summary>
    /// <remarks>
    /// The inverse process transforms the rows then the columns, so the forward process inverts the columns then the rows.
    /// The dequantized coefficients of a lossless block are 4 times the coded ones (the quantizers of the quantizer index 0
    /// are 4), and the inverse row transform starts by dividing them by 4: the coded coefficients are the outputs of this
    /// method.
    /// </remarks>
    /// <param name="residual">The residual: 4 rows of 4 samples.</param>
    /// <param name="coefficients">The coefficients: 4 rows of 4 values.</param>
    public static void ForwardWalshHadamard4x4(ReadOnlySpan<int> residual, Span<int> coefficients)
    {
        Span<int> t = stackalloc int[4];
        for (var j = 0; j < 4; j++)
        {
            for (var i = 0; i < 4; i++)
            {
                t[i] = residual[(i * 4) + j];
            }

            ForwardWalshHadamard(t);
            for (var i = 0; i < 4; i++)
            {
                coefficients[(i * 4) + j] = t[i];
            }
        }

        for (var i = 0; i < 4; i++)
        {
            ForwardWalshHadamard(coefficients.Slice(i * 4, 4));
        }
    }

    /// <summary>
    /// The forward Walsh-Hadamard transform of 4 points: the steps of the inverse transform (section 7.13.2.10) undone in the
    /// reverse order. Every step adds to a value a function of the others, so each one is exactly reversible.
    /// </summary>
    internal static void ForwardWalshHadamard(Span<int> t)
    {
        // The inverse transform ends with T[ 0 ] = a, T[ 1 ] = b, T[ 2 ] = c, T[ 3 ] = d
        var a = t[0];
        var b = t[1];
        var c = t[2];
        var d = t[3];

        // d += c and a -= b
        d -= c;
        a += b;

        // c = e - c and b = e - b, with e = (a - d) >> 1 computed from the values that these steps do not change
        var e = (a - d) >> 1;
        c = e - c;
        b = e - b;

        // d -= b and a += c
        d += b;
        a -= c;

        // The inverse transform starts with a = T[ 0 ], c = T[ 1 ], d = T[ 2 ], b = T[ 3 ]
        t[0] = a;
        t[1] = c;
        t[2] = d;
        t[3] = b;
    }
}
