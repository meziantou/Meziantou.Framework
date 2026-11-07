using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The wavelet transformations of Rec. ITU-T T.800, Annex F, on hand-computed signals and against the filters the lifting
/// steps implement, and the component transformations of Annex G.
/// </summary>
public sealed class Jpeg2000WaveletTests
{
    [Fact]
    public void ReversibleFilterOfAHandComputedSignal()
    {
        // 1 to 8, the first sample at an even position. The samples at odd positions lose the mean of their neighbors
        // (rounded down), with the sample 7 repeated after the sample 8; the samples at even positions then gain a quarter
        // of the sum of their neighbors, rounded
        int[] signal = [1, 2, 3, 4, 5, 6, 7, 8];
        Jpeg2000Dwt.LiftForwardReversible(signal, parity: 0);
        Assert.Equal([1, 0, 3, 0, 5, 0, 7, 1], signal);
        Jpeg2000Dwt.LiftReversible(signal, parity: 0);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], signal);

        // The same samples when the first one is at an odd position: the sample 1 is then a high-pass sample, between the
        // sample 2 repeated on its left and the sample 2
        signal = [1, 2, 3, 4, 5, 6, 7, 8];
        Jpeg2000Dwt.LiftForwardReversible(signal, parity: 1);
        Assert.Equal([-1, 2, 0, 4, 0, 6, 0, 8], signal);
        Jpeg2000Dwt.LiftReversible(signal, parity: 1);
        Assert.Equal([1, 2, 3, 4, 5, 6, 7, 8], signal);
    }

    [Fact]
    public void ASignalOfOneSampleIsCopiedOrDoubled()
    {
        // F.3.6 and F.4.6: copied at an even position, doubled by the forward filter at an odd one
        int[] even = [37];
        Jpeg2000Dwt.LiftForwardReversible(even, parity: 0);
        Assert.Equal([37], even);
        int[] odd = [37];
        Jpeg2000Dwt.LiftForwardReversible(odd, parity: 1);
        Assert.Equal([74], odd);
        Jpeg2000Dwt.LiftReversible(odd, parity: 1);
        Assert.Equal([37], odd);

        float[] single = [3];
        Jpeg2000Dwt.LiftForwardIrreversible(single, parity: 1);
        Assert.Equal([6f], single);
        Jpeg2000Dwt.LiftIrreversible(single, parity: 1);
        Assert.Equal([3f], single);
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(2, 1)]
    [InlineData(3, 0)]
    [InlineData(3, 1)]
    [InlineData(17, 0)]
    [InlineData(64, 1)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ReversibleFilterIsExactOnRandomSignals(int length, int parity)
    {
        var random = new Random((length * 2) + parity);
        var signal = new int[length];
        for (var i = 0; i < length; i++)
        {
            signal[i] = random.Next(-70000, 70000);
        }

        var transformed = (int[])signal.Clone();
        Jpeg2000Dwt.LiftForwardReversible(transformed, parity);
        Jpeg2000Dwt.LiftReversible(transformed, parity);
        Assert.Equal(signal, transformed);
    }

    [Fact]
    public void IrreversibleFilterRebuildsTheSynthesisFiltersOfTheSpecification()
    {
        // One low-pass coefficient gives the 7 taps of the low-pass synthesis filter, one high-pass coefficient the 9 taps
        // of the high-pass one: the filters the lifting steps of F.3.8.2 implement, whose taps add up to 2 and to 0
        var signal = new float[32];
        signal[16] = 1;
        Jpeg2000Dwt.LiftIrreversible(signal, parity: 0);
        AssertTaps(signal.AsSpan(13, 7), -0.091271763114, -0.057543526229, 0.591271763114, 1.115087052457, 0.591271763114, -0.057543526229, -0.091271763114);
        Assert.Equal(0f, signal[12]);
        Assert.Equal(0f, signal[20]);

        Array.Clear(signal);
        signal[17] = 1;
        Jpeg2000Dwt.LiftIrreversible(signal, parity: 0);
        AssertTaps(signal.AsSpan(13, 9), 0.026748757411, 0.016864118443, -0.078223266529, -0.266864118443, 0.602949018236, -0.266864118443, -0.078223266529, 0.016864118443, 0.026748757411);
        Assert.Equal(0f, signal[12]);
        Assert.Equal(0f, signal[22]);

        static void AssertTaps(ReadOnlySpan<float> actual, params ReadOnlySpan<double> expected)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-6, $"tap {i}: expected {expected[i]}, got {actual[i]}");
            }
        }
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(5, 1)]
    [InlineData(64, 0)]
    [InlineData(33, 1)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void IrreversibleFilterRoundTripsWithinSinglePrecision(int length, int parity)
    {
        var random = new Random(length + parity);
        var signal = new float[length];
        for (var i = 0; i < length; i++)
        {
            signal[i] = random.Next(-32768, 32768);
        }

        var transformed = (float[])signal.Clone();
        Jpeg2000Dwt.LiftForwardIrreversible(transformed, parity);
        Jpeg2000Dwt.LiftIrreversible(transformed, parity);
        for (var i = 0; i < length; i++)
        {
            Assert.True(Math.Abs(signal[i] - transformed[i]) < 0.05, $"sample {i}: {signal[i]} became {transformed[i]}");
        }
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TwoDimensionalTransformationsRoundTrip()
    {
        // A tile-component whose origin is odd in both directions, over three decomposition levels: the planes hold the
        // sub-bands as the forward transformation leaves them
        const int Width = 37;
        const int Height = 21;
        var resolutions = new Jpeg2000ResolutionGeometry[4];
        for (var r = 0; r < 4; r++)
        {
            var shift = 3 - r;
            resolutions[r] = new Jpeg2000ResolutionGeometry
            {
                X0 = Jpeg2000TileGeometry.CeilingShift(5, shift),
                Y0 = Jpeg2000TileGeometry.CeilingShift(3, shift),
                X1 = Jpeg2000TileGeometry.CeilingShift(5 + Width, shift),
                Y1 = Jpeg2000TileGeometry.CeilingShift(3 + Height, shift),
            };
        }

        var random = new Random(7);
        var samples = new int[Width * Height];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = random.Next(-512, 512);
        }

        var plane = (int[])samples.Clone();
        var scratch = new int[Jpeg2000Dwt.GetScratchLength(Width, Height)];
        Jpeg2000Dwt.ForwardReversible(plane, Width, resolutions, scratch);
        Assert.NotEqual(samples, plane);
        Jpeg2000Dwt.InverseReversible(plane, Width, resolutions, scratch);
        Assert.Equal(samples, plane);

        var coefficients = Array.ConvertAll(samples, sample => (float)sample);
        var floatScratch = new float[scratch.Length];
        Jpeg2000Dwt.ForwardIrreversible(coefficients, Width, resolutions, floatScratch);
        Jpeg2000Dwt.InverseIrreversible(coefficients, Width, resolutions, floatScratch);
        for (var i = 0; i < samples.Length; i++)
        {
            Assert.True(Math.Abs(samples[i] - coefficients[i]) < 0.01, $"sample {i}: {samples[i]} became {coefficients[i]}");
        }
    }

    /// <summary>
    /// The transformations by several workers split each level into bands of rows and of strips of columns: they compute the
    /// coefficients of one worker bit for bit, on planes large enough to be split, and on small ones split down to one row
    /// or strip per worker (<see cref="Jpeg2000Workers.TestIgnoreMinimumSize"/>).
    /// </summary>
    [Theory]
    [InlineData(331, 223, 5, false)]
    [InlineData(64, 1100, 3, false)]
    [InlineData(37, 21, 3, true)]
    [InlineData(1, 9, 2, true)]
    [InlineData(130, 1, 2, true)]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void TransformationsByWorkersGiveTheCoefficientsOfOneWorker(int width, int height, int levels, bool ignoreMinimumSize)
    {
        var resolutions = new Jpeg2000ResolutionGeometry[levels + 1];
        for (var r = 0; r <= levels; r++)
        {
            var shift = levels - r;
            resolutions[r] = new Jpeg2000ResolutionGeometry
            {
                X0 = Jpeg2000TileGeometry.CeilingShift(3, shift),
                Y0 = Jpeg2000TileGeometry.CeilingShift(1, shift),
                X1 = Jpeg2000TileGeometry.CeilingShift(3 + width, shift),
                Y1 = Jpeg2000TileGeometry.CeilingShift(1 + height, shift),
            };
        }

        var random = new Random(width + height);
        var samples = new int[width * height];
        for (var i = 0; i < samples.Length; i++)
        {
            samples[i] = random.Next(-2048, 2048);
        }

        var scratchLength = Jpeg2000Dwt.GetScratchLength(width, height);
        var reversible = (int[])samples.Clone();
        var irreversible = Array.ConvertAll(samples, sample => (float)sample);
        var scratch = new int[scratchLength];
        var floatScratch = new float[scratchLength];
        Jpeg2000Dwt.ForwardReversible(reversible, width, resolutions, scratch);
        Jpeg2000Dwt.ForwardIrreversible(irreversible, width, resolutions, floatScratch);
        var reversibleInverse = (int[])reversible.Clone();
        var irreversibleInverse = (float[])irreversible.Clone();
        Jpeg2000Dwt.InverseReversible(reversibleInverse, width, resolutions, scratch);
        Jpeg2000Dwt.InverseIrreversible(irreversibleInverse, width, resolutions, floatScratch);

        Jpeg2000Workers.TestIgnoreMinimumSize = ignoreMinimumSize;
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TransformationsByWorkersGiveTheCoefficientsOfOneWorker));
        try
        {
            foreach (var workers in (int[])[2, 3, 16])
            {
                var scratches = new Av1PooledArray<int>[workers];
                using var plane = Av1PooledArray<int>.Rent(scope, samples.Length);
                try
                {
                    for (var w = 0; w < workers; w++)
                    {
                        scratches[w] = Av1PooledArray<int>.Rent(scope, scratchLength);
                    }

                    samples.CopyTo(plane.Span);
                    Jpeg2000Dwt.ForwardReversible(plane, width, resolutions, scratches, workers);
                    Assert.True(plane.Span.SequenceEqual(reversible), $"{workers} workers: forward reversible");
                    Jpeg2000Dwt.InverseReversible(plane, width, resolutions, scratches, workers);
                    Assert.True(plane.Span.SequenceEqual(reversibleInverse), $"{workers} workers: inverse reversible");

                    var coefficients = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<int, float>(plane.Span));
                    for (var i = 0; i < samples.Length; i++)
                    {
                        coefficients[i] = samples[i];
                    }

                    Jpeg2000Dwt.ForwardIrreversible(plane, width, resolutions, scratches, workers);
                    Assert.True(plane.Span.SequenceEqual(unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(irreversible.AsSpan()))), $"{workers} workers: forward irreversible");
                    Jpeg2000Dwt.InverseIrreversible(plane, width, resolutions, scratches, workers);
                    Assert.True(plane.Span.SequenceEqual(unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<float, int>(irreversibleInverse.AsSpan()))), $"{workers} workers: inverse irreversible");
                }
                finally
                {
                    foreach (var array in scratches)
                    {
                        array?.Dispose();
                    }
                }
            }
        }
        finally
        {
            Jpeg2000Workers.TestIgnoreMinimumSize = false;
        }
    }

    [Fact]
    public void ReversibleComponentTransformationIsExactOnEveryCornerOfTheRange()
    {
        // Equation G-1, written out here, then its inverse (G-2), on the corners of the range of signed 9-bit samples
        int[] values = [-256, -255, -1, 0, 1, 254, 255];
        foreach (var red in values)
        {
            foreach (var green in values)
            {
                foreach (var blue in values)
                {
                    int[] y0 = [(int)Math.Floor((red + (2 * green) + blue) / 4.0)];
                    int[] y1 = [blue - green];
                    int[] y2 = [red - green];
                    Jpeg2000ColorTransform.InverseReversible(y0, y1, y2);
                    Assert.Equal((red, green, blue), (y0[0], y1[0], y2[0]));
                }
            }
        }
    }

    [Fact]
    public void IrreversibleComponentTransformationUsesTheMatrixOfTheSpecification()
    {
        // Equation G-4: a gray level is unchanged, and each chrominance moves the colors by the coefficients of the matrix
        float[] y0 = [100, 0, 0];
        float[] y1 = [0, 100, 0];
        float[] y2 = [0, 0, 100];
        Jpeg2000ColorTransform.InverseIrreversible(y0, y1, y2);
        AssertClose(y0, 100, 0, 140.2);
        AssertClose(y1, 100, -34.413, -71.414);
        AssertClose(y2, 100, 177.2, 0);

        static void AssertClose(ReadOnlySpan<float> actual, params ReadOnlySpan<double> expected)
        {
            for (var i = 0; i < expected.Length; i++)
            {
                Assert.True(Math.Abs(actual[i] - expected[i]) < 1e-4, $"value {i}: expected {expected[i]}, got {actual[i]}");
            }
        }
    }

    [Fact]
    public void ForwardComponentTransformationsFollowTheEquationsOfTheSpecification()
    {
        // Equations G-1 and G-3, written out here in double precision, on the corners of the range of signed 9-bit samples
        int[] values = [-256, -255, -1, 0, 1, 254, 255];
        foreach (var r in values)
        {
            foreach (var g in values)
            {
                foreach (var b in values)
                {
                    int[] red = [r];
                    int[] green = [g];
                    int[] blue = [b];
                    int[] expected = [(int)Math.Floor((r + (2 * g) + b) / 4.0), b - g, r - g];
                    double[] expectedIrreversible =
                    [
                        (0.299 * r) + (0.587 * g) + (0.114 * b),
                        (-0.16875 * r) - (0.33126 * g) + (0.5 * b),
                        (0.5 * r) - (0.41869 * g) - (0.08131 * b),
                    ];
                    for (var component = 0; component < 3; component++)
                    {
                        var reversible = new int[1];
                        Jpeg2000ColorTransform.ForwardReversible(red, green, blue, component, reversible);
                        Assert.Equal(expected[component], reversible[0]);

                        var irreversible = new float[1];
                        Jpeg2000ColorTransform.ForwardIrreversible(red, green, blue, component, irreversible);
                        Assert.True(Math.Abs(irreversible[0] - expectedIrreversible[component]) < 1e-3, $"component {component} of ({r}, {g}, {b}): expected {expectedIrreversible[component]}, got {irreversible[0]}");
                    }
                }
            }
        }
    }

    [Fact]
    public void SamplesAreShiftedRoundedToEvenAndClamped()
    {
        // G.1.2: half of the range is added; a floating-point sample is clamped, then rounded with ties to even
        int[] integers = [-200, -128, 0, 127, 300];
        Jpeg2000ColorTransform.ShiftLevel(integers, precision: 8);
        Assert.Equal([0, 0, 128, 255, 255], integers);

        float[] floats = [-200f, -128.4f, -0.5f, 0.5f, 1.5f, 126.5f, 127.4f, 300f, float.PositiveInfinity, float.NegativeInfinity];
        var rounded = new int[floats.Length];
        Jpeg2000ColorTransform.ShiftLevel(floats, rounded, precision: 8);
        Assert.Equal([0, 0, 128, 128, 130, 254, 255, 255, 255, 0], rounded);
    }
}
