namespace Meziantou.Framework.SnapshotTesting;

/// <summary>
/// Compares BMP/PNG/JPEG/TIFF snapshots by decoding image pixels and comparing RGB similarity.
/// </summary>
public sealed class ImageComparer(ImageComparisonSettings? settings = null) : ISnapshotComparer
{
    internal static ImageComparer Instance { get; } = new();

    public bool Equals(SnapshotData expected, SnapshotData actual)
    {
        ArgumentNullException.ThrowIfNull(expected);
        ArgumentNullException.ThrowIfNull(actual);

        try
        {
            var expectedImage = Image.Load(expected.Data);
            var actualImage = Image.Load(actual.Data);
            if (expectedImage.Width != actualImage.Width || expectedImage.Height != actualImage.Height)
                return false;

            var threshold = settings?.SimilarityThreshold;
            if (threshold is null)
                return expectedImage.Equals(actualImage);

            var similarity = ComputeMeanSsim(expectedImage.Pixels.Span, actualImage.Pixels.Span);
            return similarity >= threshold.Value;
        }
        catch (InvalidDataException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    private static float ComputeMeanSsim(ReadOnlySpan<Argb> expectedPixels, ReadOnlySpan<Argb> actualPixels)
    {
        var pixelCount = expectedPixels.Length;

        double sumExpectedR = 0;
        double sumExpectedG = 0;
        double sumExpectedB = 0;
        double sumActualR = 0;
        double sumActualG = 0;
        double sumActualB = 0;
        double sumExpectedRSquared = 0;
        double sumExpectedGSquared = 0;
        double sumExpectedBSquared = 0;
        double sumActualRSquared = 0;
        double sumActualGSquared = 0;
        double sumActualBSquared = 0;
        double sumCrossR = 0;
        double sumCrossG = 0;
        double sumCrossB = 0;

        for (var i = 0; i < pixelCount; i++)
        {
            var expectedPixel = expectedPixels[i].PackedValue;
            var actualPixel = actualPixels[i].PackedValue;

            var expectedR = (byte)(expectedPixel >> 16);
            var expectedG = (byte)(expectedPixel >> 8);
            var expectedB = (byte)expectedPixel;
            var actualR = (byte)(actualPixel >> 16);
            var actualG = (byte)(actualPixel >> 8);
            var actualB = (byte)actualPixel;

            sumExpectedR += expectedR;
            sumExpectedG += expectedG;
            sumExpectedB += expectedB;
            sumActualR += actualR;
            sumActualG += actualG;
            sumActualB += actualB;

            sumExpectedRSquared += expectedR * expectedR;
            sumExpectedGSquared += expectedG * expectedG;
            sumExpectedBSquared += expectedB * expectedB;
            sumActualRSquared += actualR * actualR;
            sumActualGSquared += actualG * actualG;
            sumActualBSquared += actualB * actualB;

            sumCrossR += expectedR * actualR;
            sumCrossG += expectedG * actualG;
            sumCrossB += expectedB * actualB;
        }

        var similarityR = ComputeChannelSsim(pixelCount, sumExpectedR, sumActualR, sumExpectedRSquared, sumActualRSquared, sumCrossR);
        var similarityG = ComputeChannelSsim(pixelCount, sumExpectedG, sumActualG, sumExpectedGSquared, sumActualGSquared, sumCrossG);
        var similarityB = ComputeChannelSsim(pixelCount, sumExpectedB, sumActualB, sumExpectedBSquared, sumActualBSquared, sumCrossB);

        return (float)((similarityR + similarityG + similarityB) / 3.0);
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
