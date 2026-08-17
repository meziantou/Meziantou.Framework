namespace Meziantou.Framework.SnapshotTesting.Tests;

public sealed class ImageLoaderTests
{
    [Fact]
    public async Task Image_LoadAsync_ThrowsWhenFormatIsNotSupported()
    {
        var ex = await Assert.ThrowsAsync<NotSupportedException>(() => Image.LoadAsync(new MemoryStream("not-a-bmp"u8.ToArray())));
        Assert.Contains("Only BMP, PNG, JPEG, and TIFF are currently supported.", ex.Message);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(17)]
    [InlineData(64)]
    [InlineData(255)]
    [InlineData(1000)]
    public void SsimAccumulator_ScoreDoesNotDependOnHowThePixelsAreSplit(int chunkLength)
    {
        // The satellite packages feed the accumulator differently: SkiaSharp hands it a whole image at once
        // while ImageSharp hands it one row at a time, so the score must not depend on the split.
        var (expected, actual) = CreatePixels(pixelCount: 5000);

        var single = new SsimAccumulator();
        single.Add(expected, actual);

        var chunked = new SsimAccumulator();
        for (var offset = 0; offset < expected.Length; offset += chunkLength)
        {
            var length = Math.Min(chunkLength, expected.Length - offset);
            chunked.Add(expected.AsSpan(offset, length), actual.AsSpan(offset, length));
        }

        Assert.Equal(single.ComputeMeanSsim(), chunked.ComputeMeanSsim());
    }

    [Fact]
    public void SsimAccumulator_MatchesADoublePrecisionComputation()
    {
        // The vectorized paths accumulate in float, so they only agree with this reference as long as the
        // accumulator folds them into its double fields before they leave the exactly representable range.
        var (expected, actual) = CreatePixels(pixelCount: 100_000);

        var accumulator = new SsimAccumulator();
        accumulator.Add(expected, actual);

        Assert.Equal(ComputeReferenceMeanSsim(expected, actual), accumulator.ComputeMeanSsim());
    }

    private static (uint[] Expected, uint[] Actual) CreatePixels(int pixelCount)
    {
        var random = new Random(42);
        var expected = new uint[pixelCount];
        var actual = new uint[pixelCount];
        for (var i = 0; i < pixelCount; i++)
        {
            expected[i] = (uint)random.Next();
            // Mostly identical, so the score lands in the range a caller would actually set a threshold in
            actual[i] = random.Next(20) is 0 ? (uint)random.Next() : expected[i];
        }

        return (expected, actual);
    }

    private static float ComputeReferenceMeanSsim(uint[] expected, uint[] actual)
    {
        var similarity = 0d;
        for (var shift = 0; shift <= 16; shift += 8)
        {
            double sumExpected = 0, sumActual = 0, sumExpectedSquared = 0, sumActualSquared = 0, sumCross = 0;
            for (var i = 0; i < expected.Length; i++)
            {
                double expectedChannel = (byte)(expected[i] >> shift);
                double actualChannel = (byte)(actual[i] >> shift);
                sumExpected += expectedChannel;
                sumActual += actualChannel;
                sumExpectedSquared += expectedChannel * expectedChannel;
                sumActualSquared += actualChannel * actualChannel;
                sumCross += expectedChannel * actualChannel;
            }

            var expectedMean = sumExpected / expected.Length;
            var actualMean = sumActual / expected.Length;
            var expectedVariance = sumExpectedSquared / expected.Length - expectedMean * expectedMean;
            var actualVariance = sumActualSquared / expected.Length - actualMean * actualMean;
            var covariance = sumCross / expected.Length - expectedMean * actualMean;

            const double C1 = 0.01 * 255.0 * 0.01 * 255.0;
            const double C2 = 0.03 * 255.0 * 0.03 * 255.0;
            similarity += (2.0 * expectedMean * actualMean + C1) * (2.0 * covariance + C2)
                / ((expectedMean * expectedMean + actualMean * actualMean + C1) * (expectedVariance + actualVariance + C2));
        }

        return (float)(similarity / 3.0);
    }
}
