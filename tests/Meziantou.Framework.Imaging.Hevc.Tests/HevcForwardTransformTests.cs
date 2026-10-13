using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The forward transforms of the encoder against the scaling and transformation process of the specification (Rec. ITU-T
/// H.265, clauses 8.6.2, 8.6.4 and 8.6.5), which the decoder implements and its own tests check against hand-computed
/// values: a forward transform is correct when the inverse process undoes it. The steps that the decoder has no method
/// for (Equations 8-298 and 8-299, the accumulation of clause 8.6.5) are written here from their equations.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcForwardTransformTests
{
    [Fact]
    public void AConstantResidualIsItsFirstCoefficient()
    {
        // The first basis function of every size is 64, and the other ones have a sum of zero: a residual c has the
        // coefficient ( c * nTbS * 64 )^2 / ( c * 2^( 27 - bitDepth ) ) scaled back by the squared norm 64 * 64 * nTbS of
        // the function in each direction, which is c * 2^( 15 - bitDepth ) whatever the size. For 8 bits and c = 100,
        // the inverse process gives ( 64 * 12800 + 64 ) >> 7 = 6400, then ( 64 * 6400 + 2048 ) >> 12 = 100
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var count = 1 << (log2TrafoSize << 1);
            var residual = new int[count];
            var coefficients = new int[count];
            foreach (var (bitDepth, value, expected) in new[] { (8, 100, 12800), (8, -255, -32640), (10, 100, 3200), (10, 1023, 32736), (12, 100, 800), (12, -4095, -32760) })
            {
                residual.AsSpan().Fill(value);
                HevcForwardTransform.Forward(residual, coefficients, log2TrafoSize, trType: 0, bitDepth);
                Assert.Equal(expected, coefficients[0]);
                Assert.True(coefficients.AsSpan(1).IndexOfAnyExcept(0) < 0);
                Assert.Equal(residual, Inverse(coefficients, log2TrafoSize, trType: 0, bitDepth));
            }
        }
    }

    [Theory]

    // The size, the transform type, the bit depth and the largest difference between a residual and what the inverse
    // process gives back for its coefficients: none for 8-bit samples. The coefficients are integers of 16 bits
    // whatever the bit depth: a coefficient of a 12-bit residual has 4 bits less after the binary point than one of an
    // 8-bit residual, and the rounding of the 1024 coefficients of a 32x32 block adds up
    [InlineData(2, 0, 8, 0)]
    [InlineData(2, 1, 8, 0)]
    [InlineData(3, 0, 8, 0)]
    [InlineData(4, 0, 8, 0)]
    [InlineData(5, 0, 8, 0)]
    [InlineData(2, 0, 10, 0)]
    [InlineData(2, 1, 10, 0)]
    [InlineData(3, 0, 10, 0)]
    [InlineData(4, 0, 10, 1)]
    [InlineData(5, 0, 10, 1)]
    [InlineData(2, 0, 12, 1)]
    [InlineData(2, 1, 12, 1)]
    [InlineData(3, 0, 12, 1)]
    [InlineData(4, 0, 12, 3)]
    [InlineData(5, 0, 12, 5)]
    public void AResidualIsUndoneByTheInverseProcess(int log2TrafoSize, int trType, int bitDepth, int maxError)
    {
        var random = new Random((log2TrafoSize * 100) + (trType * 50) + bitDepth);
        var nTbS = 1 << log2TrafoSize;
        var count = nTbS * nTbS;
        var maxValue = (1 << bitDepth) - 1;
        var residual = new int[count];
        var coefficients = new int[count];
        var worst = 0;
        long squaredError = 0;
        const int Iterations = 400;
        for (var iteration = 0; iteration < Iterations; iteration++)
        {
            // The extreme residuals, the residuals of a prediction that is nearly right, edges, and noise
            var slopeX = random.Next(-maxValue / nTbS, (maxValue / nTbS) + 1);
            var slopeY = random.Next(-maxValue / nTbS, (maxValue / nTbS) + 1);
            var amplitude = random.Next(1, maxValue + 1);
            var edge = random.Next(nTbS);
            for (var y = 0; y < nTbS; y++)
            {
                for (var x = 0; x < nTbS; x++)
                {
                    residual[(y * nTbS) + x] = iteration switch
                    {
                        0 => maxValue,
                        1 => -maxValue,
                        2 => ((x + y) & 1) is 0 ? maxValue : -maxValue,
                        3 => (x & 1) is 0 ? maxValue : -maxValue,
                        4 => y < nTbS / 2 ? maxValue : -maxValue,
                        _ => (iteration % 4) switch
                        {
                            0 => random.Next(-maxValue, maxValue + 1),
                            1 => random.Next(-amplitude, amplitude + 1),
                            2 => Math.Clamp(((x - (nTbS / 2)) * slopeX) + ((y - (nTbS / 2)) * slopeY), -maxValue, maxValue),
                            _ => x < edge ? amplitude : -amplitude,
                        },
                    };
                }
            }

            HevcForwardTransform.Forward(residual, coefficients, log2TrafoSize, trType, bitDepth);
            Assert.All(coefficients, coefficient => coefficient is >= HevcInverseTransform.CoeffMin and <= HevcInverseTransform.CoeffMax);
            var inverse = Inverse(coefficients, log2TrafoSize, trType, bitDepth);
            for (var i = 0; i < count; i++)
            {
                var error = Math.Abs(inverse[i] - residual[i]);
                worst = Math.Max(worst, error);
                squaredError += (long)error * error;
            }
        }

        Assert.True(worst <= maxError, $"The largest error is {worst}; the mean squared error is {squaredError / (double)(Iterations * count):F3}");
    }

    [Fact]
    public void TheTransformOfTheOppositeOfAResidualIsTheOppositeOfItsTransform()
    {
        var random = new Random(4);
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var count = 1 << (log2TrafoSize << 1);
            var residual = new int[count];
            var opposite = new int[count];
            var coefficients = new int[count];
            var oppositeCoefficients = new int[count];
            for (var trType = 0; trType <= (log2TrafoSize is 2 ? 1 : 0); trType++)
            {
                for (var iteration = 0; iteration < 50; iteration++)
                {
                    for (var i = 0; i < count; i++)
                    {
                        residual[i] = random.Next(-1023, 1024);
                        opposite[i] = -residual[i];
                    }

                    HevcForwardTransform.Forward(residual, coefficients, log2TrafoSize, trType, bitDepth: 10);
                    HevcForwardTransform.Forward(opposite, oppositeCoefficients, log2TrafoSize, trType, bitDepth: 10);
                    Assert.Equal(coefficients.Select(coefficient => -coefficient).ToArray(), oppositeCoefficients);
                }
            }
        }
    }

    [Fact]
    public void TheCoefficientsHaveTheEnergyOfTheResidualTimesTheGain()
    {
        // 2^( 30 - 2 * bitDepth - 2 * Log2( nTbS ) ): 2^10 for a 4x4 block of 8-bit samples, 2^-4 for a 32x32 block of
        // 12-bit samples
        Assert.Equal(10, HevcForwardTransform.GetLog2SquaredGain(2, 8));
        Assert.Equal(-4, HevcForwardTransform.GetLog2SquaredGain(5, 12));
        var random = new Random(5);
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var count = 1 << (log2TrafoSize << 1);
            var residual = new int[count];
            var coefficients = new int[count];
            foreach (var bitDepth in new[] { 8, 10, 12 })
            {
                for (var trType = 0; trType <= (log2TrafoSize is 2 ? 1 : 0); trType++)
                {
                    double samples = 0;
                    double energy = 0;
                    for (var iteration = 0; iteration < 20; iteration++)
                    {
                        for (var i = 0; i < count; i++)
                        {
                            residual[i] = random.Next(-(1 << bitDepth) + 1, 1 << bitDepth);
                            samples += (double)residual[i] * residual[i];
                        }

                        HevcForwardTransform.Forward(residual, coefficients, log2TrafoSize, trType, bitDepth);
                        energy += coefficients.Sum(coefficient => (double)coefficient * coefficient);
                    }

                    // The matrices are orthogonal to less than one percent
                    var gain = Math.Pow(2, HevcForwardTransform.GetLog2SquaredGain(log2TrafoSize, bitDepth));
                    Assert.Equal(1.0, energy / (samples * gain), 0.01);
                }
            }
        }
    }

    [Fact]
    public void ABlockThatSkipsTheTransformIsScaledAsTheDecoderScalesItBack()
    {
        // 8 bits, 4x4: the decoder computes ( ( d << 7 ) + 2048 ) >> 12, so d is the residual times 32
        Span<int> coefficients = stackalloc int[16];
        int[] residual = [1, -1, 255, -255, 0, 7, -7, 100, 3, 3, 3, 3, -128, 127, 64, -64];
        HevcForwardTransform.ForwardTransformSkip(residual, coefficients, 2, bitDepth: 8, rotate: false);
        Assert.Equal(residual.Select(value => value * 32).ToArray(), coefficients.ToArray());

        // With the rotation, the coefficient ( x, y ) is the one of the sample ( 3 - x, 3 - y )
        HevcForwardTransform.ForwardTransformSkip(residual, coefficients, 2, bitDepth: 8, rotate: true);
        Assert.Equal(Enumerable.Reverse(residual).Select(value => value * 32).ToArray(), coefficients.ToArray());

        // 12 bits, 32x32: the decoder computes ( ( d << 10 ) + 128 ) >> 8, so d is the residual divided by 4, to nearest
        var large = new int[1024];
        var largeCoefficients = new int[1024];
        large[0] = 6;
        large[1] = -6;
        large[2] = 9;
        large[3] = 4095;
        HevcForwardTransform.ForwardTransformSkip(large, largeCoefficients, 5, bitDepth: 12, rotate: false);
        Assert.Equal([2, -2, 2, 1024, 0], largeCoefficients[..5]);

        var random = new Random(6);
        foreach (var bitDepth in new[] { 8, 10, 12 })
        {
            for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
            {
                var count = 1 << (log2TrafoSize << 1);
                var samples = new int[count];
                var scaled = new int[count];
                for (var i = 0; i < count; i++)
                {
                    samples[i] = random.Next(-(1 << bitDepth) + 1, 1 << bitDepth);
                }

                foreach (var rotate in new[] { false, log2TrafoSize is 2 })
                {
                    HevcForwardTransform.ForwardTransformSkip(samples, scaled, log2TrafoSize, bitDepth, rotate);

                    // Equations 8-298 and 8-299: tsShift = 5 + Log2( nTbS ) and bdShift = 20 - bitDepth
                    var tsShift = 5 + log2TrafoSize;
                    var bdShift = 20 - bitDepth;
                    for (var i = 0; i < count; i++)
                    {
                        var d = rotate ? scaled[count - 1 - i] : scaled[i];
                        var r = ((d << tsShift) + (1 << (bdShift - 1))) >> bdShift;

                        // Exact when the decoder shifts down more than it shifts up; otherwise the coefficient is
                        // rounded, and the residual is a multiple of 2^( tsShift - bdShift )
                        var step = tsShift > bdShift ? 1 << (tsShift - bdShift) : 1;
                        Assert.True(Math.Abs(r - samples[i]) <= step / 2, $"{bitDepth} bits, {1 << log2TrafoSize} samples: {samples[i]} gives {r}");
                    }
                }
            }
        }
    }

    [Fact]
    public void ABlockThatBypassesTheTransformIsCopiedAndRotated()
    {
        int[] residual = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11, 12, 13, 14, 15, 16];
        var levels = new int[16];
        HevcForwardTransform.ForwardBypass(residual, levels, rotate: false);
        Assert.Equal(residual, levels);

        // Equation 8-297: r[ x ][ y ] = TransCoeffLevel[ nTbS - x - 1 ][ nTbS - y - 1 ]
        HevcForwardTransform.ForwardBypass(residual, levels, rotate: true);
        for (var y = 0; y < 4; y++)
        {
            for (var x = 0; x < 4; x++)
            {
                Assert.Equal(residual[(y * 4) + x], levels[((3 - y) * 4) + (3 - x)]);
            }
        }

        HevcForwardTransform.Rotate(levels);
        Assert.Equal(residual, levels);
    }

    [Fact]
    public void TheDifferencesOfAResidualAreAccumulatedBackByTheDecoder()
    {
        // A row 5, 7, 4, 4 is coded as 5, 2, -3, 0 in the horizontal direction
        int[] block = [5, 7, 4, 4, 1, 1, 1, 1, -3, 0, 3, 0, 9, 8, 7, 6];
        var differences = (int[])block.Clone();
        HevcForwardTransform.DifferenceResidual(differences, 4, vertical: false);
        Assert.Equal([5, 2, -3, 0, 1, 0, 0, 0, -3, 3, 3, -3, 9, -1, -1, -1], differences);

        // And a column 5, 1, -3, 9 as 5, -4, -4, 12 in the vertical direction
        differences = (int[])block.Clone();
        HevcForwardTransform.DifferenceResidual(differences, 4, vertical: true);
        Assert.Equal([5, 7, 4, 4, -4, -6, -3, -3, -4, -1, 2, -1, 12, 8, 4, 6], differences);

        var random = new Random(7);
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var nTbS = 1 << log2TrafoSize;
            var residual = new int[nTbS * nTbS];
            for (var i = 0; i < residual.Length; i++)
            {
                residual[i] = random.Next(-4095, 4096);
            }

            foreach (var vertical in new[] { false, true })
            {
                var coded = (int[])residual.Clone();
                HevcForwardTransform.DifferenceResidual(coded, nTbS, vertical);

                // Clause 8.6.5: r[ x ][ y ] += r[ x - 1 ][ y ] for the horizontal direction, r[ x ][ y - 1 ] otherwise
                for (var y = 0; y < nTbS; y++)
                {
                    for (var x = 0; x < nTbS; x++)
                    {
                        if (vertical && y > 0)
                        {
                            coded[(y * nTbS) + x] += coded[((y - 1) * nTbS) + x];
                        }
                        else if (!vertical && x > 0)
                        {
                            coded[(y * nTbS) + x] += coded[(y * nTbS) + x - 1];
                        }
                    }
                }

                Assert.Equal(residual, coded);
            }
        }
    }

    // Clause 8.6.4, then Equation 8-299: the residual that a decoder computes from scaled transform coefficients
    private static int[] Inverse(int[] coefficients, int log2TrafoSize, int trType, int bitDepth)
    {
        var block = (int[])coefficients.Clone();
        HevcInverseTransform.Transform(block, log2TrafoSize, trType, new int[block.Length]);
        var bdShift = 20 - bitDepth;
        for (var i = 0; i < block.Length; i++)
        {
            block[i] = (block[i] + (1 << (bdShift - 1))) >> bdShift;
        }

        return block;
    }
}
