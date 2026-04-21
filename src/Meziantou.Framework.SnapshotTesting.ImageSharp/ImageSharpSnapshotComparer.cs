using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

internal sealed class ImageSharpSnapshotComparer(ImageComparisonSettings? settings) : ISnapshotComparer
{
    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        using var expectedImage = Image.Load<Rgba32>(expected.Data);
        using var actualImage = Image.Load<Rgba32>(actual.Data);

        if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
            return false;

        var threshold = settings?.SimilarityThreshold;
        if (threshold is null)
            return ExactEquals(expectedImage, actualImage);

        var ssim = ComputeMeanSSIM(expectedImage, actualImage);
        return ssim >= threshold.Value;
    }

    private static bool ExactEquals(Image<Rgba32> expected, Image<Rgba32> actual)
    {
        var equal = true;
        expected.ProcessPixelRows(actual, (expectedAccessor, actualAccessor) =>
        {
            for (var y = 0; y < expectedAccessor.Height && equal; y++)
            {
                var expectedRow = MemoryMarshal.AsBytes(expectedAccessor.GetRowSpan(y));
                var actualRow = MemoryMarshal.AsBytes(actualAccessor.GetRowSpan(y));
                if (!expectedRow.SequenceEqual(actualRow))
                    equal = false;
            }
        });
        return equal;
    }

    /// <summary>
    /// Computes the mean Structural Similarity Index (SSIM) across R, G, B channels.
    /// Iterates pixel rows via <see cref="SixLabors.ImageSharp.PixelFormats.Rgba32"/> accessors,
    /// casting each row to <see cref="uint"/> spans and using <see cref="Vector512{T}"/>/<see cref="Vector256{T}"/>/<see cref="Vector128{T}"/>
    /// intrinsics to extract channels and accumulate statistics with zero heap allocations.
    /// Variances and covariance are derived from the identity <c>Var(X) = E[X²] − E[X]²</c> in a single pass.
    /// </summary>
    private static float ComputeMeanSSIM(Image<Rgba32> img1, Image<Rgba32> img2)
    {
        var pixelCount = img1.Width * img1.Height;

        double sumR1 = 0, sumG1 = 0, sumB1 = 0;
        double sumR2 = 0, sumG2 = 0, sumB2 = 0;
        double sumR1Sq = 0, sumG1Sq = 0, sumB1Sq = 0;
        double sumR2Sq = 0, sumG2Sq = 0, sumB2Sq = 0;
        double sumR12 = 0, sumG12 = 0, sumB12 = 0;

        img1.ProcessPixelRows(img2, (acc1, acc2) =>
        {
            for (var y = 0; y < acc1.Height; y++)
            {
                var row1 = MemoryMarshal.Cast<Rgba32, uint>(acc1.GetRowSpan(y));
                var row2 = MemoryMarshal.Cast<Rgba32, uint>(acc2.GetRowSpan(y));
                var width = row1.Length;
                var x = 0;

                // Vector512 path (AVX-512): 16 pixels per iteration
                if (Vector512.IsHardwareAccelerated && width >= Vector512<uint>.Count)
                {
                    var vR1 = Vector512<float>.Zero; var vG1 = Vector512<float>.Zero; var vB1 = Vector512<float>.Zero;
                    var vR2 = Vector512<float>.Zero; var vG2 = Vector512<float>.Zero; var vB2 = Vector512<float>.Zero;
                    var vR1Sq = Vector512<float>.Zero; var vG1Sq = Vector512<float>.Zero; var vB1Sq = Vector512<float>.Zero;
                    var vR2Sq = Vector512<float>.Zero; var vG2Sq = Vector512<float>.Zero; var vB2Sq = Vector512<float>.Zero;
                    var vR12 = Vector512<float>.Zero; var vG12 = Vector512<float>.Zero; var vB12 = Vector512<float>.Zero;
                    var mask = Vector512.Create(0x000000FFu);

                    ref var ref1 = ref MemoryMarshal.GetReference(row1);
                    ref var ref2 = ref MemoryMarshal.GetReference(row2);
                    for (; x <= width - Vector512<uint>.Count; x += Vector512<uint>.Count)
                    {
                        var p1 = Vector512.LoadUnsafe(ref ref1, (nuint)x);
                        var p2 = Vector512.LoadUnsafe(ref ref2, (nuint)x);
                        var r1 = Vector512.ConvertToSingle((p1 & mask).AsInt32());
                        var g1 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(p1, 8) & mask).AsInt32());
                        var b1 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(p1, 16) & mask).AsInt32());
                        var r2 = Vector512.ConvertToSingle((p2 & mask).AsInt32());
                        var g2 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(p2, 8) & mask).AsInt32());
                        var b2 = Vector512.ConvertToSingle((Vector512.ShiftRightLogical(p2, 16) & mask).AsInt32());
                        vR1 += r1; vG1 += g1; vB1 += b1;
                        vR2 += r2; vG2 += g2; vB2 += b2;
                        vR1Sq += r1 * r1; vG1Sq += g1 * g1; vB1Sq += b1 * b1;
                        vR2Sq += r2 * r2; vG2Sq += g2 * g2; vB2Sq += b2 * b2;
                        vR12 += r1 * r2; vG12 += g1 * g2; vB12 += b1 * b2;
                    }

                    sumR1 += Vector512.Sum(vR1); sumG1 += Vector512.Sum(vG1); sumB1 += Vector512.Sum(vB1);
                    sumR2 += Vector512.Sum(vR2); sumG2 += Vector512.Sum(vG2); sumB2 += Vector512.Sum(vB2);
                    sumR1Sq += Vector512.Sum(vR1Sq); sumG1Sq += Vector512.Sum(vG1Sq); sumB1Sq += Vector512.Sum(vB1Sq);
                    sumR2Sq += Vector512.Sum(vR2Sq); sumG2Sq += Vector512.Sum(vG2Sq); sumB2Sq += Vector512.Sum(vB2Sq);
                    sumR12 += Vector512.Sum(vR12); sumG12 += Vector512.Sum(vG12); sumB12 += Vector512.Sum(vB12);
                }

                // Vector256 path
                if (Vector256.IsHardwareAccelerated && width >= Vector256<uint>.Count)
                {
                    var vR1 = Vector256<float>.Zero; var vG1 = Vector256<float>.Zero; var vB1 = Vector256<float>.Zero;
                    var vR2 = Vector256<float>.Zero; var vG2 = Vector256<float>.Zero; var vB2 = Vector256<float>.Zero;
                    var vR1Sq = Vector256<float>.Zero; var vG1Sq = Vector256<float>.Zero; var vB1Sq = Vector256<float>.Zero;
                    var vR2Sq = Vector256<float>.Zero; var vG2Sq = Vector256<float>.Zero; var vB2Sq = Vector256<float>.Zero;
                    var vR12 = Vector256<float>.Zero; var vG12 = Vector256<float>.Zero; var vB12 = Vector256<float>.Zero;
                    var mask = Vector256.Create(0x000000FFu);

                    ref var ref1 = ref MemoryMarshal.GetReference(row1);
                    ref var ref2 = ref MemoryMarshal.GetReference(row2);
                    for (; x <= width - Vector256<uint>.Count; x += Vector256<uint>.Count)
                    {
                        var p1 = Vector256.LoadUnsafe(ref ref1, (nuint)x);
                        var p2 = Vector256.LoadUnsafe(ref ref2, (nuint)x);
                        var r1 = Vector256.ConvertToSingle((p1 & mask).AsInt32());
                        var g1 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(p1, 8) & mask).AsInt32());
                        var b1 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(p1, 16) & mask).AsInt32());
                        var r2 = Vector256.ConvertToSingle((p2 & mask).AsInt32());
                        var g2 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(p2, 8) & mask).AsInt32());
                        var b2 = Vector256.ConvertToSingle((Vector256.ShiftRightLogical(p2, 16) & mask).AsInt32());
                        vR1 += r1; vG1 += g1; vB1 += b1;
                        vR2 += r2; vG2 += g2; vB2 += b2;
                        vR1Sq += r1 * r1; vG1Sq += g1 * g1; vB1Sq += b1 * b1;
                        vR2Sq += r2 * r2; vG2Sq += g2 * g2; vB2Sq += b2 * b2;
                        vR12 += r1 * r2; vG12 += g1 * g2; vB12 += b1 * b2;
                    }

                    sumR1 += Vector256.Sum(vR1); sumG1 += Vector256.Sum(vG1); sumB1 += Vector256.Sum(vB1);
                    sumR2 += Vector256.Sum(vR2); sumG2 += Vector256.Sum(vG2); sumB2 += Vector256.Sum(vB2);
                    sumR1Sq += Vector256.Sum(vR1Sq); sumG1Sq += Vector256.Sum(vG1Sq); sumB1Sq += Vector256.Sum(vB1Sq);
                    sumR2Sq += Vector256.Sum(vR2Sq); sumG2Sq += Vector256.Sum(vG2Sq); sumB2Sq += Vector256.Sum(vB2Sq);
                    sumR12 += Vector256.Sum(vR12); sumG12 += Vector256.Sum(vG12); sumB12 += Vector256.Sum(vB12);
                }

                // Vector128 path (remainder after Vector256, or main path when only 128-bit SIMD is available)
                if (Vector128.IsHardwareAccelerated && x <= width - Vector128<uint>.Count)
                {
                    var vR1 = Vector128<float>.Zero; var vG1 = Vector128<float>.Zero; var vB1 = Vector128<float>.Zero;
                    var vR2 = Vector128<float>.Zero; var vG2 = Vector128<float>.Zero; var vB2 = Vector128<float>.Zero;
                    var vR1Sq = Vector128<float>.Zero; var vG1Sq = Vector128<float>.Zero; var vB1Sq = Vector128<float>.Zero;
                    var vR2Sq = Vector128<float>.Zero; var vG2Sq = Vector128<float>.Zero; var vB2Sq = Vector128<float>.Zero;
                    var vR12 = Vector128<float>.Zero; var vG12 = Vector128<float>.Zero; var vB12 = Vector128<float>.Zero;
                    var mask = Vector128.Create(0x000000FFu);

                    ref var ref1 = ref MemoryMarshal.GetReference(row1);
                    ref var ref2 = ref MemoryMarshal.GetReference(row2);
                    for (; x <= width - Vector128<uint>.Count; x += Vector128<uint>.Count)
                    {
                        var p1 = Vector128.LoadUnsafe(ref ref1, (nuint)x);
                        var p2 = Vector128.LoadUnsafe(ref ref2, (nuint)x);
                        var r1 = Vector128.ConvertToSingle((p1 & mask).AsInt32());
                        var g1 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(p1, 8) & mask).AsInt32());
                        var b1 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(p1, 16) & mask).AsInt32());
                        var r2 = Vector128.ConvertToSingle((p2 & mask).AsInt32());
                        var g2 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(p2, 8) & mask).AsInt32());
                        var b2 = Vector128.ConvertToSingle((Vector128.ShiftRightLogical(p2, 16) & mask).AsInt32());
                        vR1 += r1; vG1 += g1; vB1 += b1;
                        vR2 += r2; vG2 += g2; vB2 += b2;
                        vR1Sq += r1 * r1; vG1Sq += g1 * g1; vB1Sq += b1 * b1;
                        vR2Sq += r2 * r2; vG2Sq += g2 * g2; vB2Sq += b2 * b2;
                        vR12 += r1 * r2; vG12 += g1 * g2; vB12 += b1 * b2;
                    }

                    sumR1 += Vector128.Sum(vR1); sumG1 += Vector128.Sum(vG1); sumB1 += Vector128.Sum(vB1);
                    sumR2 += Vector128.Sum(vR2); sumG2 += Vector128.Sum(vG2); sumB2 += Vector128.Sum(vB2);
                    sumR1Sq += Vector128.Sum(vR1Sq); sumG1Sq += Vector128.Sum(vG1Sq); sumB1Sq += Vector128.Sum(vB1Sq);
                    sumR2Sq += Vector128.Sum(vR2Sq); sumG2Sq += Vector128.Sum(vG2Sq); sumB2Sq += Vector128.Sum(vB2Sq);
                    sumR12 += Vector128.Sum(vR12); sumG12 += Vector128.Sum(vG12); sumB12 += Vector128.Sum(vB12);
                }

                // Scalar remainder
                for (; x < width; x++)
                {
                    double r1 = (byte)row1[x], g1 = (byte)(row1[x] >> 8), b1 = (byte)(row1[x] >> 16);
                    double r2 = (byte)row2[x], g2 = (byte)(row2[x] >> 8), b2 = (byte)(row2[x] >> 16);
                    sumR1 += r1; sumG1 += g1; sumB1 += b1;
                    sumR2 += r2; sumG2 += g2; sumB2 += b2;
                    sumR1Sq += r1 * r1; sumG1Sq += g1 * g1; sumB1Sq += b1 * b1;
                    sumR2Sq += r2 * r2; sumG2Sq += g2 * g2; sumB2Sq += b2 * b2;
                    sumR12 += r1 * r2; sumG12 += g1 * g2; sumB12 += b1 * b2;
                }
            }
        });

        var ssimR = ComputeChannelSSIM(pixelCount, sumR1, sumR2, sumR1Sq, sumR2Sq, sumR12);
        var ssimG = ComputeChannelSSIM(pixelCount, sumG1, sumG2, sumG1Sq, sumG2Sq, sumG12);
        var ssimB = ComputeChannelSSIM(pixelCount, sumB1, sumB2, sumB1Sq, sumB2Sq, sumB12);
        return (float)((ssimR + ssimG + ssimB) / 3.0);
    }

    private static double ComputeChannelSSIM(int count, double sum1, double sum2, double sumSq1, double sumSq2, double sumCross)
    {
        var mean1 = sum1 / count;
        var mean2 = sum2 / count;

        // Var(X) = E[X²] − E[X]²,  Cov(X,Y) = E[XY] − E[X]·E[Y]
        var sigma1Sq = sumSq1 / count - mean1 * mean1;
        var sigma2Sq = sumSq2 / count - mean2 * mean2;
        var sigma12 = sumCross / count - mean1 * mean2;

        const double K1 = 0.01;
        const double K2 = 0.03;
        const double L = 255.0;
        const double C1 = K1 * L * K1 * L;
        const double C2 = K2 * L * K2 * L;

        var numerator = (2.0 * mean1 * mean2 + C1) * (2.0 * sigma12 + C2);
        var denominator = (mean1 * mean1 + mean2 * mean2 + C1) * (sigma1Sq + sigma2Sq + C2);

        return numerator / denominator;
    }
}
