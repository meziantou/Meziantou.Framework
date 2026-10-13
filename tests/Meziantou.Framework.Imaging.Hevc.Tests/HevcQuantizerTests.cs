using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The quantizer of the encoder against the scaling process for transform coefficients of the specification (Rec. ITU-T
/// H.265, clause 8.6.3), written here from Equation 8-309, and the sign data hiding against the residual coding syntax
/// (clause 7.3.8.11), written here from its syntax table.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcQuantizerTests
{
    private static readonly int[] LevelScale = [40, 45, 51, 57, 64, 72];

    [Fact]
    public void ALevelIsTheCoefficientDividedByTheStepWithADeadZone()
    {
        // 4x4, 8 bits, qP 4: bdShift is 5 and the factor 16 * 64, so that a step is 32 coefficient units
        int[] coefficients = [0, 15, 16, 31, 32, 47, 48, -48, -15, 100, -100, 16383, 21, 22, -21, -22];
        var levels = new int[16];

        // To nearest, halves away from zero
        Assert.Equal(13, HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 128));
        Assert.Equal([0, 0, 1, 1, 1, 1, 2, -2, 0, 3, -3, 512, 1, 1, -1, -1], levels);

        // Toward zero
        Assert.Equal(7, HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 0));
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, -1, 0, 3, -3, 511, 0, 0, 0, 0], levels);

        // A third of a step: 21 / 32 is below 1 - 85 / 256, and 22 / 32 is not
        Assert.Equal(10, HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 85));
        Assert.Equal([0, 0, 0, 1, 1, 1, 1, -1, 0, 3, -3, 512, 0, 1, 0, -1], levels);

        // Six more is a step twice as large: qP 10
        Assert.Equal(7, HevcQuantizer.Quantize(coefficients, levels, 2, qP: 10, bitDepth: 8, [], rounding: 128));
        Assert.Equal([0, 0, 0, 0, 1, 1, 1, -1, 0, 2, -2, 256, 0, 0, 0, 0], levels);

        Assert.Equal(5, HevcQuantizer.GetBdShift(2, 8));
        Assert.Equal(12, HevcQuantizer.GetBdShift(5, 12));
        Assert.Equal(64, HevcQuantizer.GetLevelScale(4));
        Assert.Equal(72 << 8, HevcQuantizer.GetLevelScale(53));
    }

    [Theory]
    [InlineData(8)]
    [InlineData(10)]
    [InlineData(12)]
    public void TheLevelsAreMonotonicAndReconstructWithinAStep(int bitDepth)
    {
        var random = new Random(bitDepth);
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var count = 1 << (log2TrafoSize << 1);
            var coefficients = new int[count];
            var levels = new int[count];
            var opposite = new int[count];
            var bdShift = bitDepth + log2TrafoSize - 5;

            // qP includes QpBdOffset: 0 to 51 + 6 * ( bitDepth - 8 )
            for (var qP = 0; qP <= 51 + (6 * (bitDepth - 8)); qP += 1 + random.Next(3))
            {
                var rounding = random.Next(4) switch
                {
                    0 => 0,
                    1 => 128,
                    2 => 255,
                    _ => random.Next(256),
                };

                // Increasing magnitudes: every small one, then larger and larger ones, up to the largest coefficient
                long factor = (16L * LevelScale[qP % 6]) << (qP / 6);
                var magnitude = 0;
                for (var i = 0; i < count; i++)
                {
                    coefficients[i] = magnitude;
                    opposite[i] = -magnitude;
                    magnitude = Math.Min(32767, magnitude + 1 + (i > count / 2 ? random.Next((int)Math.Min(4000, 1 + (4 * (factor >> bdShift)))) : 0));
                }

                coefficients[^1] = 32767;
                opposite[^1] = -32768;
                var nonZero = HevcQuantizer.Quantize(coefficients, levels, log2TrafoSize, qP, bitDepth, [], rounding);
                Assert.Equal(levels.Count(level => level is not 0), nonZero);
                for (var i = 0; i < count; i++)
                {
                    var level = levels[i];
                    var scaled = (long)coefficients[i] << bdShift;
                    var message = $"{bitDepth} bits, {1 << log2TrafoSize} samples, qP {qP}, rounding {rounding}, coefficient {coefficients[i]}, level {level}";
                    Assert.True(level is >= 0 and <= HevcQuantizer.MaxLevel, message);
                    Assert.True(i is 0 || level >= levels[i - 1], message);

                    // The dead zone: zero below 1 - rounding / 256 of a step
                    if (scaled * 256 < factor * (256 - rounding))
                    {
                        Assert.Equal(0, level, message);
                    }

                    // The level is the quotient: between the coefficient minus a step and the coefficient, with the offset
                    if (level < HevcQuantizer.MaxLevel)
                    {
                        Assert.True(level * factor <= scaled + ((factor * rounding) / 256), message);
                        Assert.True((level + 1) * factor > scaled + ((factor * rounding) / 256), message);

                        // What the decoder reconstructs is within a step of the coefficient
                        var reconstructed = Scale(level, 16, qP, log2TrafoSize, bitDepth);
                        Assert.True(Math.Abs((long)reconstructed - coefficients[i]) << bdShift <= factor + (1L << bdShift), message);
                    }
                }

                // The levels of the opposite coefficients are the opposite levels
                var expected = levels.Select(level => -level).ToArray();
                Assert.Equal(nonZero, HevcQuantizer.Quantize(opposite, levels, log2TrafoSize, qP, bitDepth, [], rounding));
                expected[^1] = levels[^1];
                Assert.Equal(expected, levels);
            }
        }
    }

    [Fact]
    public void ALevelHasAtMostSixteenBits()
    {
        // 32x32, 12 bits, qP 0: the factor 16 * 40 is less than 2^bdShift = 4096, and the quotient of the largest
        // coefficients has more than 15 bits
        var coefficients = new int[1024];
        var levels = new int[1024];
        coefficients[0] = 32767;
        coefficients[1] = -32768;
        coefficients[2] = 5119;
        coefficients[3] = 5120;
        Assert.Equal(4, HevcQuantizer.Quantize(coefficients, levels, 5, qP: 0, bitDepth: 12, [], rounding: 128));
        Assert.Equal([32767, -32767, 32762, 32767], levels[..4]);
        Assert.Equal(32767, HevcQuantizer.MaxLevel);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(3)]
    public void TheStepOfACoefficientFollowsItsScalingFactor(int chromaArrayType)
    {
        // The default scaling lists (Tables 7-5 and 7-6), as the decoder derives ScalingFactor from them
        var scalingFactors = new byte[HevcScalingList.ScalingFactorLength];
        HevcScalingList.Default.WriteScalingFactors(scalingFactors, chromaArrayType);
        var random = new Random(chromaArrayType);
        for (var log2TrafoSize = 2; log2TrafoSize <= 5; log2TrafoSize++)
        {
            var count = 1 << (log2TrafoSize << 1);
            var coefficients = new int[count];
            var levels = new int[count];
            var single = new int[count];
            var singleLevels = new int[count];
            for (var matrixId = 0; matrixId < 6; matrixId += log2TrafoSize is 5 && chromaArrayType is not 3 ? 3 : 1)
            {
                var m = scalingFactors.AsSpan(HevcScalingList.GetScalingFactorOffset(log2TrafoSize - 2, matrixId), count);
                Assert.True(log2TrafoSize is 2 ? m.IndexOfAnyExcept((byte)16) < 0 : m.IndexOfAnyExcept((byte)16) >= 0);
                foreach (var (qP, bitDepth, rounding) in new[] { (0, 8, 128), (22, 8, 85), (37, 8, 43), (51, 8, 128), (30, 10, 100), (63, 10, 0), (40, 12, 128), (75, 12, 200) })
                {
                    for (var i = 0; i < count; i++)
                    {
                        coefficients[i] = random.Next(-32768, 32768) >> random.Next(12);
                    }

                    var nonZero = HevcQuantizer.Quantize(coefficients, levels, log2TrafoSize, qP, bitDepth, m, rounding);
                    Assert.Equal(levels.Count(level => level is not 0), nonZero);
                    var bdShift = bitDepth + log2TrafoSize - 5;
                    for (var i = 0; i < count; i++)
                    {
                        // The level of a coefficient is the one of a block without scaling list whose step is m / 16
                        // times as large: the quotient by m * levelScale
                        long factor = ((long)m[i] * LevelScale[qP % 6]) << (qP / 6);
                        var scaled = Math.Abs((long)coefficients[i]) << bdShift;
                        var expected = Math.Min((scaled + ((factor * rounding) / 256)) / factor, 32767);
                        Assert.Equal((int)(coefficients[i] < 0 ? -expected : expected), levels[i]);
                        if (expected < 32767)
                        {
                            var reconstructed = Scale(levels[i], m[i], qP, log2TrafoSize, bitDepth);
                            Assert.True(Math.Abs((long)reconstructed - coefficients[i]) << bdShift <= factor + (1L << bdShift));
                        }
                    }

                    // A flat matrix of 16 is no matrix
                    if (log2TrafoSize is 2)
                    {
                        Assert.Equal(nonZero, HevcQuantizer.Quantize(coefficients, singleLevels, log2TrafoSize, qP, bitDepth, [], rounding));
                        Assert.Equal(singleLevels, levels);
                    }
                }
            }
        }
    }

    [Fact]
    public void AHiddenSignIsGivenByTheParityOfTheLevels()
    {
        // 4x4, 8 bits, qP 4: a step of 32. The coefficients 100 and 70 at the scan positions 0 and 5 have the levels 3
        // and 2: more than 3 positions apart, so the sign of the first one is hidden, and the sum 5 is odd, which says
        // that it is negative. Adding one to the level 2 costs ( 96 - 70 )^2 - ( 70 - 64 )^2 = 640, less than adding one
        // to the level 3, ( 128 - 100 )^2 - ( 100 - 96 )^2 = 768, than subtracting one from a level, and than a new
        // level, 32^2 = 1024
        var scan = HevcScanOrder.Get(2, HevcScanOrder.Diagonal);
        var coefficients = new int[16];
        var levels = new int[16];
        var first = (HevcScanOrder.Y(scan[0]) * 4) + HevcScanOrder.X(scan[0]);
        var last = (HevcScanOrder.Y(scan[5]) * 4) + HevcScanOrder.X(scan[5]);
        coefficients[first] = 100;
        coefficients[last] = 70;
        Assert.Equal(2, HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 128));
        Assert.Equal((3, 2), (levels[first], levels[last]));
        Assert.True(HevcQuantizer.HideSign(coefficients, levels, 2, HevcScanOrder.Diagonal, 0, 0, qP: 4, bitDepth: 8, []));
        Assert.Equal((3, 3), (levels[first], levels[last]));
        Assert.Equal(2, levels.Count(level => level is not 0));

        // The levels now tell the sign: nothing changes
        Assert.False(HevcQuantizer.HideSign(coefficients, levels, 2, HevcScanOrder.Diagonal, 0, 0, qP: 4, bitDepth: 8, []));

        // A negative first coefficient with an odd sum is what the parity says
        coefficients[first] = -100;
        HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 128);
        Assert.False(HevcQuantizer.HideSign(coefficients, levels, 2, HevcScanOrder.Diagonal, 0, 0, qP: 4, bitDepth: 8, []));
        Assert.Equal((-3, 2), (levels[first], levels[last]));

        // The scan positions 0 and 3 are not far enough: the sign is coded, whatever the parity
        var near = (HevcScanOrder.Y(scan[3]) * 4) + HevcScanOrder.X(scan[3]);
        Array.Clear(coefficients);
        coefficients[first] = 100;
        coefficients[near] = 70;
        HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 128);
        Assert.False(HevcQuantizer.HideSign(coefficients, levels, 2, HevcScanOrder.Diagonal, 0, 0, qP: 4, bitDepth: 8, []));
        Assert.Equal((3, 2), (levels[first], levels[near]));

        // A coefficient next to the half of a step is the cheapest one to change: 80 / 32 is 2.5, coded as 3, and 2 costs
        // nothing more; the level 3 of the coefficient 96 is exact
        Array.Clear(coefficients);
        coefficients[first] = 96;
        coefficients[last] = 80;
        HevcQuantizer.Quantize(coefficients, levels, 2, qP: 4, bitDepth: 8, [], rounding: 128);
        Assert.Equal((3, 3), (levels[first], levels[last]));
        coefficients[first] = -96;
        levels[first] = -3;
        Assert.True(HevcQuantizer.HideSign(coefficients, levels, 2, HevcScanOrder.Diagonal, 0, 0, qP: 4, bitDepth: 8, []));
        Assert.Equal((-3, 2), (levels[first], levels[last]));
    }

    [Theory]
    [InlineData(8, false)]
    [InlineData(10, false)]
    [InlineData(12, false)]
    [InlineData(8, true)]
    public void HidingASignChangesOneLevelByOneForTheLeastDistortion(int bitDepth, bool withScalingList)
    {
        var random = new Random(bitDepth + (withScalingList ? 100 : 0));
        var scalingFactors = new byte[HevcScalingList.ScalingFactorLength];
        HevcScalingList.Default.WriteScalingFactors(scalingFactors, chromaArrayType: 1);
        Span<int> positions = stackalloc int[16];
        var changes = 0;
        var newLevels = 0;
        var removedLevels = 0;
        for (var iteration = 0; iteration < 1500; iteration++)
        {
            var log2TrafoSize = random.Next(2, 6);
            var nTbS = 1 << log2TrafoSize;
            var count = nTbS * nTbS;
            var scanIdx = random.Next(3);
            var qP = random.Next(0, 52 + (6 * (bitDepth - 8)));
            var m = withScalingList ? scalingFactors.AsSpan(HevcScalingList.GetScalingFactorOffset(log2TrafoSize - 2, random.Next(2) * 3), count) : [];
            var bdShift = bitDepth + log2TrafoSize - 5;
            var coefficients = new int[count];
            var levels = new int[count];

            // Sparse blocks of small levels, where a sign is often hidden or not by a single change
            var step = (int)Math.Max(1, ((16L * LevelScale[qP % 6]) << (qP / 6)) >> bdShift);
            var density = random.Next(1, 10);
            for (var i = 0; i < count; i++)
            {
                coefficients[i] = random.Next(10) < density ? (int)Math.Clamp(random.NextInt64(-3L * step, (3L * step) + 1), -32768, 32767) : 0;
            }

            HevcQuantizer.Quantize(coefficients, levels, log2TrafoSize, qP, bitDepth, m, rounding: random.Next(256));
            var scan = HevcScanOrder.Get(2, scanIdx);
            for (var yS = 0; yS < nTbS >> 2; yS++)
            {
                for (var xS = 0; xS < nTbS >> 2; xS++)
                {
                    for (var n = 0; n < 16; n++)
                    {
                        positions[n] = (((yS << 2) + HevcScanOrder.Y(scan[n])) * nTbS) + (xS << 2) + HevcScanOrder.X(scan[n]);
                    }

                    var before = (int[])levels.Clone();
                    var consistent = IsConsistent(before, positions);
                    var changed = HevcQuantizer.HideSign(coefficients, levels, log2TrafoSize, scanIdx, xS, yS, qP, bitDepth, m);
                    var message = $"iteration {iteration}, sub-block ({xS}, {yS})";

                    // Nothing changes when the levels already tell the sign, and outside the sub-block
                    Assert.Equal(!consistent, changed, message);
                    Assert.True(IsConsistent(levels, positions), message);
                    var different = Enumerable.Range(0, count).Where(i => before[i] != levels[i]).ToList();
                    if (!changed)
                    {
                        Assert.Empty(different, message);
                        continue;
                    }

                    // One level of the sub-block, whose magnitude changes by one and whose sign is the one of its
                    // coefficient
                    changes++;
                    var position = Assert.Single(different);
                    Assert.True(positions.Contains(position), message);
                    Assert.Equal(1, Math.Abs(Math.Abs(levels[position]) - Math.Abs(before[position])), message);
                    Assert.True(levels[position] is 0 || coefficients[position] is 0 || (levels[position] < 0) == (coefficients[position] < 0), message);
                    newLevels += before[position] is 0 ? 1 : 0;
                    removedLevels += levels[position] is 0 ? 1 : 0;

                    // No other change of one level by one that leaves the sub-block consistent adds less distortion
                    var cost = GetCost(coefficients[position], before[position], levels[position], m.IsEmpty ? 16 : m[position], qP, bdShift);
                    var candidate = (int[])before.Clone();
                    foreach (var other in positions)
                    {
                        foreach (var magnitude in new[] { Math.Abs(before[other]) - 1, Math.Abs(before[other]) + 1 })
                        {
                            if (magnitude is < 0 or > 32767)
                                continue;

                            foreach (var negative in before[other] is 0 && coefficients[other] is 0 ? new[] { false, true } : [before[other] is 0 ? coefficients[other] < 0 : before[other] < 0])
                            {
                                candidate[other] = negative ? -magnitude : magnitude;
                                if (IsConsistent(candidate, positions))
                                {
                                    Assert.True(cost <= GetCost(coefficients[other], before[other], candidate[other], m.IsEmpty ? 16 : m[other], qP, bdShift), message);
                                }
                            }
                        }

                        candidate[other] = before[other];
                    }
                }
            }
        }

        // The cases that matter were met: changes of each kind
        Assert.True(changes > 500 && newLevels > 0 && removedLevels > 0, $"{changes} changes, {newLevels} new levels, {removedLevels} removed levels");
    }

    // Clause 7.3.8.11: whether a decoder gives its sign to the first significant coefficient of a sub-block. signHidden is
    // lastSigScanPos - firstSigScanPos > 3; the coefficient is then negative when sumAbsLevel is odd
    private static bool IsConsistent(int[] levels, ReadOnlySpan<int> positions)
    {
        var firstSigScanPos = 16;
        var lastSigScanPos = -1;
        var sumAbsLevel = 0;
        for (var n = 15; n >= 0; n--)
        {
            if (levels[positions[n]] is 0)
                continue;

            if (lastSigScanPos is -1)
            {
                lastSigScanPos = n;
            }

            firstSigScanPos = n;
            sumAbsLevel += Math.Abs(levels[positions[n]]);
        }

        var signHidden = lastSigScanPos - firstSigScanPos > 3;
        return !signHidden || (sumAbsLevel % 2 is 1) == (levels[positions[firstSigScanPos]] < 0);
    }

    // The squared error of a level minus the one of another level, in units of 2^-bdShift of a coefficient
    private static Int128 GetCost(int coefficient, int before, int after, int m, int qP, int bdShift)
    {
        long factor = ((long)m * LevelScale[qP % 6]) << (qP / 6);
        Int128 scaled = Math.Abs((long)coefficient) << bdShift;
        var errorBefore = scaled - (Math.Abs(before) * (Int128)factor);
        var errorAfter = scaled - (Math.Abs(after) * (Int128)factor);
        return (errorAfter * errorAfter) - (errorBefore * errorBefore);
    }

    // Equation 8-309, without extended precision processing
    private static int Scale(int level, int m, int qP, int log2TrafoSize, int bitDepth)
    {
        var bdShift = bitDepth + log2TrafoSize + 10 - 15;
        var value = ((((long)level * m * LevelScale[qP % 6]) << (qP / 6)) + (1L << (bdShift - 1))) >> bdShift;
        return (int)Math.Clamp(value, -32768, 32767);
    }
}
