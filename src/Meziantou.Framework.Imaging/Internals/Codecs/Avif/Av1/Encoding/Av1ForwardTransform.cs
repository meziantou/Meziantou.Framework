namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the encoder: the inverses of the transforms of the decoding process (AV1 specification,
/// section 7.13), which is the only direction that the specification describes.
/// </summary>
internal static class Av1ForwardTransform
{
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
