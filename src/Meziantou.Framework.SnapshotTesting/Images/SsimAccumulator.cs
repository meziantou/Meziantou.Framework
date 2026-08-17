using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;

namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Accumulates the statistics needed to compute the mean Structural Similarity Index (SSIM) of two images.
/// </summary>
/// <remarks>
/// <para>
/// Pixels are packed as <see cref="uint"/> values holding three 8-bit channels in bytes 0, 1 and 2; byte 3 is
/// ignored. The score is the unweighted mean of the three per-channel scores, so the channel order does not
/// matter: the same code serves ARGB pixels, where the ignored byte holds the alpha and bytes 2, 1 and 0 hold
/// R, G and B, and RGBA pixels, where bytes 0, 1 and 2 hold R, G and B.
/// </para>
/// <para>
/// <see cref="Add"/> can be called once with a whole image, or once per row for image types that do not expose
/// their pixels as a single contiguous buffer. It gives the same result either way: the pixels are processed in
/// blocks of <see cref="MaxChunkLength"/>, chosen so that every lane of the <see cref="float"/> accumulators stays
/// inside the range where <see cref="float"/> represents integers exactly, and each block is folded into
/// <see cref="double"/> fields that hold the exact sums. The score therefore depends only on the pixels, not on
/// how the caller splits them or on which vector width the hardware supports.
/// </para>
/// <para>
/// Variances and covariance are derived from the identity <c>Var(X) = E[X²] − E[X]²</c> in a single pass.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
internal struct SsimAccumulator
{
    private double _sumExpected0, _sumExpected1, _sumExpected2;
    private double _sumActual0, _sumActual1, _sumActual2;
    private double _sumExpectedSquared0, _sumExpectedSquared1, _sumExpectedSquared2;
    private double _sumActualSquared0, _sumActualSquared1, _sumActualSquared2;
    private double _sumCross0, _sumCross1, _sumCross2;
    private int _count;

    /// <summary>
    /// The largest number of pixels accumulated before the <see cref="float"/> vectors are folded into the
    /// <see cref="double"/> fields. The largest term added per pixel is a product of two channels, at most
    /// <c>255 × 255 = 65025</c>, and <see cref="float"/> represents integers exactly up to 2^24. A block of 256
    /// pixels totals at most <c>256 × 65025 = 16 646 400</c>, just under that limit, so no value reached while
    /// accumulating a block — in a single lane, or in any partial sum of the horizontal reduction that folds the
    /// lanes together — ever leaves the exactly representable range. Every block therefore contributes its exact
    /// integer sum, and the <see cref="double"/> totals stay exact as well. The value is a multiple of every
    /// vector width, so a full block never leaves a remainder for the narrower paths to pick up.
    /// </summary>
    private const int MaxChunkLength = 256;

    /// <summary>
    /// Accumulates the statistics of a chunk of pixels. Both spans must have the same length.
    /// </summary>
    public void Add(ReadOnlySpan<uint> expectedPixels, ReadOnlySpan<uint> actualPixels)
    {
        Debug.Assert(expectedPixels.Length == actualPixels.Length, "Both spans must contain the same number of pixels.");

        while (!expectedPixels.IsEmpty)
        {
            var length = Math.Min(expectedPixels.Length, MaxChunkLength);
            AddChunk(expectedPixels[..length], actualPixels[..length]);
            expectedPixels = expectedPixels[length..];
            actualPixels = actualPixels[length..];
        }
    }

    private void AddChunk(ReadOnlySpan<uint> expectedPixels, ReadOnlySpan<uint> actualPixels)
    {
        var pixelCount = expectedPixels.Length;

        double sumExpected0 = 0, sumExpected1 = 0, sumExpected2 = 0;
        double sumActual0 = 0, sumActual1 = 0, sumActual2 = 0;
        double sumExpectedSquared0 = 0, sumExpectedSquared1 = 0, sumExpectedSquared2 = 0;
        double sumActualSquared0 = 0, sumActualSquared1 = 0, sumActualSquared2 = 0;
        double sumCross0 = 0, sumCross1 = 0, sumCross2 = 0;

        ref var expectedRef = ref MemoryMarshal.GetReference(expectedPixels);
        ref var actualRef = ref MemoryMarshal.GetReference(actualPixels);
        var i = 0;

        // Vector512 path (AVX-512): 16 pixels per iteration
        if (Vector512.IsHardwareAccelerated && pixelCount >= Vector512<uint>.Count)
        {
            var vExpected0 = Vector512<float>.Zero; var vExpected1 = Vector512<float>.Zero; var vExpected2 = Vector512<float>.Zero;
            var vActual0 = Vector512<float>.Zero; var vActual1 = Vector512<float>.Zero; var vActual2 = Vector512<float>.Zero;
            var vExpectedSquared0 = Vector512<float>.Zero; var vExpectedSquared1 = Vector512<float>.Zero; var vExpectedSquared2 = Vector512<float>.Zero;
            var vActualSquared0 = Vector512<float>.Zero; var vActualSquared1 = Vector512<float>.Zero; var vActualSquared2 = Vector512<float>.Zero;
            var vCross0 = Vector512<float>.Zero; var vCross1 = Vector512<float>.Zero; var vCross2 = Vector512<float>.Zero;
            var mask = Vector512.Create(0x000000FFu);

            for (; i <= pixelCount - Vector512<uint>.Count; i += Vector512<uint>.Count)
            {
                var expectedPixel = Vector512.LoadUnsafe(ref expectedRef, (nuint)i);
                var actualPixel = Vector512.LoadUnsafe(ref actualRef, (nuint)i);
                var expected0 = Vector512.ConvertToSingle((expectedPixel & mask).AsInt32());
                var expected1 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(expectedPixel, 8) & mask).AsInt32());
                var expected2 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(expectedPixel, 16) & mask).AsInt32());
                var actual0 = Vector512.ConvertToSingle((actualPixel & mask).AsInt32());
                var actual1 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(actualPixel, 8) & mask).AsInt32());
                var actual2 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(actualPixel, 16) & mask).AsInt32());
                vExpected0 += expected0; vExpected1 += expected1; vExpected2 += expected2;
                vActual0 += actual0; vActual1 += actual1; vActual2 += actual2;
                vExpectedSquared0 += expected0 * expected0; vExpectedSquared1 += expected1 * expected1; vExpectedSquared2 += expected2 * expected2;
                vActualSquared0 += actual0 * actual0; vActualSquared1 += actual1 * actual1; vActualSquared2 += actual2 * actual2;
                vCross0 += expected0 * actual0; vCross1 += expected1 * actual1; vCross2 += expected2 * actual2;
            }

            sumExpected0 += Vector512.Sum(vExpected0); sumExpected1 += Vector512.Sum(vExpected1); sumExpected2 += Vector512.Sum(vExpected2);
            sumActual0 += Vector512.Sum(vActual0); sumActual1 += Vector512.Sum(vActual1); sumActual2 += Vector512.Sum(vActual2);
            sumExpectedSquared0 += Vector512.Sum(vExpectedSquared0); sumExpectedSquared1 += Vector512.Sum(vExpectedSquared1); sumExpectedSquared2 += Vector512.Sum(vExpectedSquared2);
            sumActualSquared0 += Vector512.Sum(vActualSquared0); sumActualSquared1 += Vector512.Sum(vActualSquared1); sumActualSquared2 += Vector512.Sum(vActualSquared2);
            sumCross0 += Vector512.Sum(vCross0); sumCross1 += Vector512.Sum(vCross1); sumCross2 += Vector512.Sum(vCross2);
        }

        // Vector256 path
        if (Vector256.IsHardwareAccelerated && i <= pixelCount - Vector256<uint>.Count)
        {
            var vExpected0 = Vector256<float>.Zero; var vExpected1 = Vector256<float>.Zero; var vExpected2 = Vector256<float>.Zero;
            var vActual0 = Vector256<float>.Zero; var vActual1 = Vector256<float>.Zero; var vActual2 = Vector256<float>.Zero;
            var vExpectedSquared0 = Vector256<float>.Zero; var vExpectedSquared1 = Vector256<float>.Zero; var vExpectedSquared2 = Vector256<float>.Zero;
            var vActualSquared0 = Vector256<float>.Zero; var vActualSquared1 = Vector256<float>.Zero; var vActualSquared2 = Vector256<float>.Zero;
            var vCross0 = Vector256<float>.Zero; var vCross1 = Vector256<float>.Zero; var vCross2 = Vector256<float>.Zero;
            var mask = Vector256.Create(0x000000FFu);

            for (; i <= pixelCount - Vector256<uint>.Count; i += Vector256<uint>.Count)
            {
                var expectedPixel = Vector256.LoadUnsafe(ref expectedRef, (nuint)i);
                var actualPixel = Vector256.LoadUnsafe(ref actualRef, (nuint)i);
                var expected0 = Vector256.ConvertToSingle((expectedPixel & mask).AsInt32());
                var expected1 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(expectedPixel, 8) & mask).AsInt32());
                var expected2 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(expectedPixel, 16) & mask).AsInt32());
                var actual0 = Vector256.ConvertToSingle((actualPixel & mask).AsInt32());
                var actual1 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(actualPixel, 8) & mask).AsInt32());
                var actual2 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(actualPixel, 16) & mask).AsInt32());
                vExpected0 += expected0; vExpected1 += expected1; vExpected2 += expected2;
                vActual0 += actual0; vActual1 += actual1; vActual2 += actual2;
                vExpectedSquared0 += expected0 * expected0; vExpectedSquared1 += expected1 * expected1; vExpectedSquared2 += expected2 * expected2;
                vActualSquared0 += actual0 * actual0; vActualSquared1 += actual1 * actual1; vActualSquared2 += actual2 * actual2;
                vCross0 += expected0 * actual0; vCross1 += expected1 * actual1; vCross2 += expected2 * actual2;
            }

            sumExpected0 += Vector256.Sum(vExpected0); sumExpected1 += Vector256.Sum(vExpected1); sumExpected2 += Vector256.Sum(vExpected2);
            sumActual0 += Vector256.Sum(vActual0); sumActual1 += Vector256.Sum(vActual1); sumActual2 += Vector256.Sum(vActual2);
            sumExpectedSquared0 += Vector256.Sum(vExpectedSquared0); sumExpectedSquared1 += Vector256.Sum(vExpectedSquared1); sumExpectedSquared2 += Vector256.Sum(vExpectedSquared2);
            sumActualSquared0 += Vector256.Sum(vActualSquared0); sumActualSquared1 += Vector256.Sum(vActualSquared1); sumActualSquared2 += Vector256.Sum(vActualSquared2);
            sumCross0 += Vector256.Sum(vCross0); sumCross1 += Vector256.Sum(vCross1); sumCross2 += Vector256.Sum(vCross2);
        }

        // Vector128 path (remainder after Vector256, or main path when only 128-bit SIMD is available)
        if (Vector128.IsHardwareAccelerated && i <= pixelCount - Vector128<uint>.Count)
        {
            var vExpected0 = Vector128<float>.Zero; var vExpected1 = Vector128<float>.Zero; var vExpected2 = Vector128<float>.Zero;
            var vActual0 = Vector128<float>.Zero; var vActual1 = Vector128<float>.Zero; var vActual2 = Vector128<float>.Zero;
            var vExpectedSquared0 = Vector128<float>.Zero; var vExpectedSquared1 = Vector128<float>.Zero; var vExpectedSquared2 = Vector128<float>.Zero;
            var vActualSquared0 = Vector128<float>.Zero; var vActualSquared1 = Vector128<float>.Zero; var vActualSquared2 = Vector128<float>.Zero;
            var vCross0 = Vector128<float>.Zero; var vCross1 = Vector128<float>.Zero; var vCross2 = Vector128<float>.Zero;
            var mask = Vector128.Create(0x000000FFu);

            for (; i <= pixelCount - Vector128<uint>.Count; i += Vector128<uint>.Count)
            {
                var expectedPixel = Vector128.LoadUnsafe(ref expectedRef, (nuint)i);
                var actualPixel = Vector128.LoadUnsafe(ref actualRef, (nuint)i);
                var expected0 = Vector128.ConvertToSingle((expectedPixel & mask).AsInt32());
                var expected1 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(expectedPixel, 8) & mask).AsInt32());
                var expected2 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(expectedPixel, 16) & mask).AsInt32());
                var actual0 = Vector128.ConvertToSingle((actualPixel & mask).AsInt32());
                var actual1 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(actualPixel, 8) & mask).AsInt32());
                var actual2 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(actualPixel, 16) & mask).AsInt32());
                vExpected0 += expected0; vExpected1 += expected1; vExpected2 += expected2;
                vActual0 += actual0; vActual1 += actual1; vActual2 += actual2;
                vExpectedSquared0 += expected0 * expected0; vExpectedSquared1 += expected1 * expected1; vExpectedSquared2 += expected2 * expected2;
                vActualSquared0 += actual0 * actual0; vActualSquared1 += actual1 * actual1; vActualSquared2 += actual2 * actual2;
                vCross0 += expected0 * actual0; vCross1 += expected1 * actual1; vCross2 += expected2 * actual2;
            }

            sumExpected0 += Vector128.Sum(vExpected0); sumExpected1 += Vector128.Sum(vExpected1); sumExpected2 += Vector128.Sum(vExpected2);
            sumActual0 += Vector128.Sum(vActual0); sumActual1 += Vector128.Sum(vActual1); sumActual2 += Vector128.Sum(vActual2);
            sumExpectedSquared0 += Vector128.Sum(vExpectedSquared0); sumExpectedSquared1 += Vector128.Sum(vExpectedSquared1); sumExpectedSquared2 += Vector128.Sum(vExpectedSquared2);
            sumActualSquared0 += Vector128.Sum(vActualSquared0); sumActualSquared1 += Vector128.Sum(vActualSquared1); sumActualSquared2 += Vector128.Sum(vActualSquared2);
            sumCross0 += Vector128.Sum(vCross0); sumCross1 += Vector128.Sum(vCross1); sumCross2 += Vector128.Sum(vCross2);
        }

        // Scalar remainder
        for (; i < pixelCount; i++)
        {
            double expected0 = (byte)expectedPixels[i], expected1 = (byte)(expectedPixels[i] >> 8), expected2 = (byte)(expectedPixels[i] >> 16);
            double actual0 = (byte)actualPixels[i], actual1 = (byte)(actualPixels[i] >> 8), actual2 = (byte)(actualPixels[i] >> 16);
            sumExpected0 += expected0; sumExpected1 += expected1; sumExpected2 += expected2;
            sumActual0 += actual0; sumActual1 += actual1; sumActual2 += actual2;
            sumExpectedSquared0 += expected0 * expected0; sumExpectedSquared1 += expected1 * expected1; sumExpectedSquared2 += expected2 * expected2;
            sumActualSquared0 += actual0 * actual0; sumActualSquared1 += actual1 * actual1; sumActualSquared2 += actual2 * actual2;
            sumCross0 += expected0 * actual0; sumCross1 += expected1 * actual1; sumCross2 += expected2 * actual2;
        }

        _sumExpected0 += sumExpected0; _sumExpected1 += sumExpected1; _sumExpected2 += sumExpected2;
        _sumActual0 += sumActual0; _sumActual1 += sumActual1; _sumActual2 += sumActual2;
        _sumExpectedSquared0 += sumExpectedSquared0; _sumExpectedSquared1 += sumExpectedSquared1; _sumExpectedSquared2 += sumExpectedSquared2;
        _sumActualSquared0 += sumActualSquared0; _sumActualSquared1 += sumActualSquared1; _sumActualSquared2 += sumActualSquared2;
        _sumCross0 += sumCross0; _sumCross1 += sumCross1; _sumCross2 += sumCross2;
        _count += pixelCount;
    }

    /// <summary>
    /// Computes the mean SSIM of every pixel accumulated so far. Values range from <c>0.0</c> (completely
    /// different) to <c>1.0</c> (identical).
    /// </summary>
    public readonly float ComputeMeanSsim()
    {
        var similarity0 = ComputeChannelSsim(_count, _sumExpected0, _sumActual0, _sumExpectedSquared0, _sumActualSquared0, _sumCross0);
        var similarity1 = ComputeChannelSsim(_count, _sumExpected1, _sumActual1, _sumExpectedSquared1, _sumActualSquared1, _sumCross1);
        var similarity2 = ComputeChannelSsim(_count, _sumExpected2, _sumActual2, _sumExpectedSquared2, _sumActualSquared2, _sumCross2);

        return (float)((similarity0 + similarity1 + similarity2) / 3.0);
    }

    private static double ComputeChannelSsim(
        int count,
        double sumExpected,
        double sumActual,
        double sumExpectedSquared,
        double sumActualSquared,
        double sumCross)
    {
        var expectedMean = sumExpected / count;
        var actualMean = sumActual / count;

        // Var(X) = E[X²] − E[X]²,  Cov(X,Y) = E[XY] − E[X]·E[Y]
        var expectedVariance = sumExpectedSquared / count - expectedMean * expectedMean;
        var actualVariance = sumActualSquared / count - actualMean * actualMean;
        var covariance = sumCross / count - expectedMean * actualMean;

        const double K1 = 0.01;
        const double K2 = 0.03;
        const double L = 255.0;
        const double C1 = K1 * L * K1 * L;
        const double C2 = K2 * L * K2 * L;

        var numerator = (2.0 * expectedMean * actualMean + C1) * (2.0 * covariance + C2);
        var denominator = (expectedMean * expectedMean + actualMean * actualMean + C1) * (expectedVariance + actualVariance + C2);

        return numerator / denominator;
    }
}
