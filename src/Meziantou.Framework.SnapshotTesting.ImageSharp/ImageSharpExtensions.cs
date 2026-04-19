using System.Numerics.Tensors;
using System.Runtime.InteropServices;
using Meziantou.Framework.HumanReadable;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace Meziantou.Framework.SnapshotTesting.ImageSharp;

public static class SnapsthotSettingsImageSharpExtensions
{
    extension(SnapshotSettings snapshotSettings)
    {
        public void AddImageSharp(ImageComparisonSettings? settings = null)
        {
            snapshotSettings.AddConverter(new Rgba32HumanReadableConverter());
            snapshotSettings.Serializers.Add(new ImageSnapshotSerializer());

            var comparer = new ImageSharpSnapshotComparer(settings);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Bmp, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Png, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Jpeg, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Tiff, comparer);
            snapshotSettings.SetSnapshotComparer(SnapshotType.Webp, comparer);
        }
    }
}

public sealed class ImageComparisonSettings
{
    public float? SimilarityThreshold { get; set; }
}

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
    /// Iterates pixel rows via <see cref="SixLabors.ImageSharp.PixelFormats.Rgba32"/> accessors to avoid
    /// copying the full image into a buffer, using per-row float buffers and
    /// <see cref="System.Numerics.Tensors.TensorPrimitives"/> for SIMD-accelerated row sums.
    /// Variances and covariance are derived from the identity
    /// <c>Var(X) = E[X²] − E[X]²</c> in a single pass.
    /// </summary>
    private static float ComputeMeanSSIM(Image<Rgba32> img1, Image<Rgba32> img2)
    {
        var width = img1.Width;
        var pixelCount = width * img1.Height;

        // Per-row channel buffers; reused for every row to keep allocations small.
        var rowR1 = new float[width];
        var rowG1 = new float[width];
        var rowB1 = new float[width];
        var rowR2 = new float[width];
        var rowG2 = new float[width];
        var rowB2 = new float[width];
        var rowCrossR = new float[width];
        var rowCrossG = new float[width];
        var rowCrossB = new float[width];
        var rowSqR1 = new float[width];
        var rowSqG1 = new float[width];
        var rowSqB1 = new float[width];
        var rowSqR2 = new float[width];
        var rowSqG2 = new float[width];
        var rowSqB2 = new float[width];

        // Accumulators: sum, sumSq, sumCross per channel (R=0, G=1, B=2).
        var sum1 = new double[3];
        var sum2 = new double[3];
        var sumSq1 = new double[3];
        var sumSq2 = new double[3];
        var sumCross = new double[3];

        img1.ProcessPixelRows(img2, (acc1, acc2) =>
        {
            for (var y = 0; y < acc1.Height; y++)
            {
                var row1 = acc1.GetRowSpan(y);
                var row2 = acc2.GetRowSpan(y);

                for (var x = 0; x < width; x++)
                {
                    rowR1[x] = row1[x].R;
                    rowG1[x] = row1[x].G;
                    rowB1[x] = row1[x].B;
                    rowR2[x] = row2[x].R;
                    rowG2[x] = row2[x].G;
                    rowB2[x] = row2[x].B;
                }

                // Use TensorPrimitives to compute squared and cross-product rows.
                TensorPrimitives.Multiply(rowR1, rowR1, rowSqR1);
                TensorPrimitives.Multiply(rowG1, rowG1, rowSqG1);
                TensorPrimitives.Multiply(rowB1, rowB1, rowSqB1);
                TensorPrimitives.Multiply(rowR2, rowR2, rowSqR2);
                TensorPrimitives.Multiply(rowG2, rowG2, rowSqG2);
                TensorPrimitives.Multiply(rowB2, rowB2, rowSqB2);
                TensorPrimitives.Multiply(rowR1, rowR2, rowCrossR);
                TensorPrimitives.Multiply(rowG1, rowG2, rowCrossG);
                TensorPrimitives.Multiply(rowB1, rowB2, rowCrossB);

                sum1[0] += TensorPrimitives.Sum(rowR1.AsSpan());
                sum1[1] += TensorPrimitives.Sum(rowG1.AsSpan());
                sum1[2] += TensorPrimitives.Sum(rowB1.AsSpan());
                sum2[0] += TensorPrimitives.Sum(rowR2.AsSpan());
                sum2[1] += TensorPrimitives.Sum(rowG2.AsSpan());
                sum2[2] += TensorPrimitives.Sum(rowB2.AsSpan());
                sumSq1[0] += TensorPrimitives.Sum(rowSqR1.AsSpan());
                sumSq1[1] += TensorPrimitives.Sum(rowSqG1.AsSpan());
                sumSq1[2] += TensorPrimitives.Sum(rowSqB1.AsSpan());
                sumSq2[0] += TensorPrimitives.Sum(rowSqR2.AsSpan());
                sumSq2[1] += TensorPrimitives.Sum(rowSqG2.AsSpan());
                sumSq2[2] += TensorPrimitives.Sum(rowSqB2.AsSpan());
                sumCross[0] += TensorPrimitives.Sum(rowCrossR.AsSpan());
                sumCross[1] += TensorPrimitives.Sum(rowCrossG.AsSpan());
                sumCross[2] += TensorPrimitives.Sum(rowCrossB.AsSpan());
            }
        });

        float ssimSum = 0f;
        for (var c = 0; c < 3; c++)
            ssimSum += ComputeChannelSSIM(sum1[c], sum2[c], sumSq1[c], sumSq2[c], sumCross[c], pixelCount);

        return ssimSum / 3f;
    }

    private static float ComputeChannelSSIM(double sum1, double sum2, double sumSq1, double sumSq2, double sumCross, int count)
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

        return (float)(numerator / denominator);
    }
}

internal sealed class ImageSnapshotSerializer : ISnapshotSerializer
{
    public bool CanSerialize(SnapshotType type, object? value) => value is Image;
    public IReadOnlyList<SnapshotData> Serialize(SnapshotType type, object? value)
    {
        if (value is not Image image)
            throw new ArgumentException("Value must be an Image.", nameof(value));

        using var ms = new MemoryStream();
        if (type == SnapshotType.Png)
        {
            image.SaveAsPng(ms);
        }
        else if (type == SnapshotType.Jpeg)
        {
            image.SaveAsJpeg(ms);
        }
        else if (type == SnapshotType.Bmp)
        {
            image.SaveAsBmp(ms);
        }
        else if (type == SnapshotType.Tiff)
        {
            image.SaveAsTiff(ms);
        }
        else if (type == SnapshotType.Webp)
        {
            image.SaveAsWebp(ms);
        }

        return [new SnapshotData(type.FileExtension, ms.ToArray())];
    }
}


internal sealed class Rgba32HumanReadableConverter : HumanReadableConverter<Rgba32>
{
    protected override void WriteValue(HumanReadableTextWriter writer, Rgba32 value, HumanReadableSerializerOptions options)
        => writer.WriteValue($"#{value.R:X2}{value.G:X2}{value.B:X2}{value.A:X2}");
}