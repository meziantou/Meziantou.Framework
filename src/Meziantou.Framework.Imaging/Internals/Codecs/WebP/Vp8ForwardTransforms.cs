namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The forward transforms of the VP8 encoder, matched to the scaling of the decoder's inverse transforms
/// (<see cref="Vp8Transforms"/>): VP8 coefficients are twice the orthonormal 4x4 DCT-II coefficients (a DC-only block decodes
/// to <c>dc / 8</c> per pixel), and the Y2 block is <c>M D M / 2</c> where <c>M</c> is the 4x4 Walsh-Hadamard matrix of the
/// inverse transform (<c>M M = 4 I</c>, the inverse computes <c>M Y2 M / 8</c>). Computed in fixed point (12-bit basis) and
/// rounded to nearest.
/// </summary>
internal static class Vp8ForwardTransforms
{
    // The DCT basis in fixed point with 12 fractional bits
    private const int BasisBits = 12;
    private static readonly int[] DctBasis = CreateDctBasis();

    private static ReadOnlySpan<sbyte> Hadamard => [1, 1, 1, 1, 1, 1, -1, -1, 1, -1, -1, 1, 1, -1, 1, -1];

    /// <summary>Computes the VP8 DCT coefficients (raster order) of a 4x4 residual block.</summary>
    /// <param name="residual">The 16 residuals (source minus prediction), raster order.</param>
    /// <param name="coefficients">The 16 coefficients, raster order.</param>
    public static void ForwardDct(ReadOnlySpan<int> residual, Span<int> coefficients)
    {
        var basis = DctBasis.AsSpan(0, 16);
        residual = residual[..16];
        coefficients = coefficients[..16];
        Span<int> temp = stackalloc int[16];

        // Rows, then columns: C R C^T
        for (var y = 0; y < 4; y++)
        {
            var r0 = residual[y * 4];
            var r1 = residual[(y * 4) + 1];
            var r2 = residual[(y * 4) + 2];
            var r3 = residual[(y * 4) + 3];
            for (var u = 0; u < 4; u++)
            {
                temp[(y * 4) + u] = (basis[u * 4] * r0) + (basis[(u * 4) + 1] * r1) + (basis[(u * 4) + 2] * r2) + (basis[(u * 4) + 3] * r3);
            }
        }

        // The coefficients are twice the orthonormal ones, rounded to nearest (ties away from zero)
        const int Shift = (2 * BasisBits) - 1;
        const long Half = 1L << (Shift - 1);
        for (var v = 0; v < 4; v++)
        {
            for (var u = 0; u < 4; u++)
            {
                var sum = ((long)basis[v * 4] * temp[u]) + ((long)basis[(v * 4) + 1] * temp[4 + u]) + ((long)basis[(v * 4) + 2] * temp[8 + u]) + ((long)basis[(v * 4) + 3] * temp[12 + u]);
                coefficients[(v * 4) + u] = (int)(sum >= 0 ? (sum + Half) >> Shift : -((-sum + Half) >> Shift));
            }
        }
    }

    /// <summary>Computes the Y2 coefficients (raster order) from the 16 DC coefficients of the luma blocks (block raster order).</summary>
    public static void ForwardWalshHadamard(ReadOnlySpan<int> dc, Span<int> y2)
    {
        var m = Hadamard;
        Span<int> temp = stackalloc int[16];
        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += m[(i * 4) + k] * dc[(k * 4) + j];
                }

                temp[(i * 4) + j] = sum;
            }
        }

        for (var i = 0; i < 4; i++)
        {
            for (var j = 0; j < 4; j++)
            {
                var sum = 0;
                for (var k = 0; k < 4; k++)
                {
                    sum += temp[(i * 4) + k] * m[(k * 4) + j];
                }

                // Halved with rounding to nearest, ties away from zero
                y2[(i * 4) + j] = sum >= 0 ? (sum + 1) >> 1 : -((-sum + 1) >> 1);
            }
        }
    }

    private static int[] CreateDctBasis()
    {
        var basis = new int[16];
        for (var u = 0; u < 4; u++)
        {
            var scale = u == 0 ? Math.Sqrt(0.25) : Math.Sqrt(0.5);
            for (var x = 0; x < 4; x++)
            {
                basis[(u * 4) + x] = (int)Math.Round(scale * Math.Cos((2 * x + 1) * u * Math.PI / 8) * (1 << BasisBits), MidpointRounding.AwayFromZero);
            }
        }

        return basis;
    }
}
