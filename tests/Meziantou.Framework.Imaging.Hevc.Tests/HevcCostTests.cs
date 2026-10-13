using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>
/// The rate estimates of the encoder. The cost of a bin is checked against its definition, computed here in floating point
/// from the table of the specification, and against what it estimates: the size of the codeword that the arithmetic
/// encoder writes for the bins.
/// </summary>
[SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
public sealed class HevcCostTests
{
    [Fact]
    public void TheCostOfABinIsTheMeanLogarithmOfTheRangeOverItsInterval()
    {
        for (var pStateIdx = 0; pStateIdx < 64; pStateIdx++)
        {
            double mps = 0;
            double lps = 0;
            for (var range = 256; range < 512; range++)
            {
                double ivlLpsRange = HevcCabacTables.RangeTabLps[(pStateIdx << 2) + ((range >> 6) & 3)];
                mps += Math.Log2(range / (range - ivlLpsRange));
                lps += Math.Log2(range / ivlLpsRange);
            }

            // The mean over the 256 ranges, in units of 1/256 bit, is the sum
            for (var valMps = 0; valMps < 2; valMps++)
            {
                var context = (byte)((pStateIdx << 1) | valMps);
                Assert.Equal(mps, HevcCost.GetBinCost(context, valMps), 0.51);
                Assert.Equal(lps, HevcCost.GetBinCost(context, 1 - valMps), 0.51);
            }
        }
    }

    [Fact]
    public void TheCostsFollowTheProbabilityOfTheStates()
    {
        // The state 0 is the one of equal probabilities: about one bit for each bin
        Assert.True(HevcCost.GetBinCost(0, 0) is > 200 and < 256);
        Assert.True(HevcCost.GetBinCost(0, 1) is > 256 and < 320);

        // The most probable symbol costs less and less, the least probable one more and more
        for (var pStateIdx = 1; pStateIdx < 63; pStateIdx++)
        {
            Assert.True(HevcCost.GetBinCost((byte)(pStateIdx << 1), 0) <= HevcCost.GetBinCost((byte)((pStateIdx - 1) << 1), 0));
            Assert.True(HevcCost.GetBinCost((byte)(pStateIdx << 1), 1) > HevcCost.GetBinCost((byte)((pStateIdx - 1) << 1), 1));
        }

        // The last state: a hundredth of a bit and nearly 6 bits
        Assert.True(HevcCost.GetBinCost(62 << 1, 0) is > 0 and < 10);
        Assert.True(HevcCost.GetBinCost(62 << 1, 1) is > 5 * 256 and < 6 * 256);

        // A bypass bin is one bit; a terminating bin equal to 0 nearly nothing, and one equal to 1 the 7 or 8 bits of an
        // interval of range 2, and the 2 bits of the flushing
        Assert.Equal(256, HevcCost.Scale);
        Assert.Equal(0, HevcCost.GetBypassCost(0));
        Assert.Equal(7 * 256, HevcCost.GetBypassCost(7));
        Assert.True(HevcCost.GetTerminateCost(0) is > 0 and < 4);
        Assert.True(HevcCost.GetTerminateCost(1) is > 9 * 256 and < 10 * 256);
    }

    [Theory]
    [InlineData(1, 5)]
    [InlineData(2, 20)]
    [InlineData(3, 50)]
    [InlineData(4, 80)]
    [InlineData(5, 95)]
    public void TheCostOfTheBinsIsTheSizeOfTheirCodeword(int seed, int skew)
    {
        // Bins of a fixed probability, on a few contexts that adapt to it, with some bypass bins
        var random = new Random(seed);
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(TheCostOfTheBinsIsTheSizeOfTheirCodeword));
        using var output = new PooledPayloadWriter(scope);
        var encoder = new HevcCabacEncoder();
        encoder.Initialize(output);
        var contexts = new byte[8];
        long rate = 0;
        for (var i = 0; i < 200000; i++)
        {
            if (random.Next(10) is 0)
            {
                rate += HevcCost.GetBypassCost(1);
                encoder.EncodeBypass(random.Next(2));
                continue;
            }

            var bin = random.Next(100) < skew ? 1 : 0;
            ref var context = ref contexts[random.Next(contexts.Length)];
            rate += HevcCost.GetBinCost(context, bin);
            encoder.EncodeDecision(ref context, bin);
        }

        rate += HevcCost.GetTerminateCost(1);
        encoder.EncodeTerminate(1);

        // The estimate ignores what the range is when a bin is coded: it is within two percent of the size
        var bits = output.Length * 8L;
        var estimate = rate / (double)HevcCost.Scale;
        Assert.True(Math.Abs(estimate - bits) < bits * 0.02, $"{estimate} bits estimated, {bits} bits written");
    }

    [Fact]
    public void ExpGolombCodesHaveTheLengthOfTheirBinarization()
    {
        // ue(v), Table 9-2: 1, 3, 3, 5, 5, 5, 5 and 7 bits for 0 to 7; se(v), Table 9-3
        int[] lengths = [1, 3, 3, 5, 5, 5, 5, 7];
        for (var value = 0u; value < lengths.Length; value++)
        {
            Assert.Equal(lengths[value] * HevcCost.Scale, HevcCost.GetUECost(value));
        }

        Assert.Equal(1 * HevcCost.Scale, HevcCost.GetSECost(0));
        Assert.Equal(3 * HevcCost.Scale, HevcCost.GetSECost(1));
        Assert.Equal(3 * HevcCost.Scale, HevcCost.GetSECost(-1));
        Assert.Equal(5 * HevcCost.Scale, HevcCost.GetSECost(2));
        Assert.Equal(7 * HevcCost.Scale, HevcCost.GetSECost(-4));

        // Clause 9.3.3.3, k = 0: 0 is "0"; 1 is "1 0 0" and 2 "1 0 1"; 3 to 6 are "1 1 0 x x"
        Assert.Equal(1, HevcCost.GetExpGolombLength(0, 0));
        Assert.Equal(3, HevcCost.GetExpGolombLength(1, 0));
        Assert.Equal(3, HevcCost.GetExpGolombLength(2, 0));
        Assert.Equal(5, HevcCost.GetExpGolombLength(3, 0));
        Assert.Equal(5, HevcCost.GetExpGolombLength(6, 0));
        Assert.Equal(7, HevcCost.GetExpGolombLength(7, 0));

        // k = 1: 0 and 1 are "0 x"; 2 to 5 are "1 0 x x"; 6 to 13 are "1 1 0 x x x". k = 3: 0 to 7 are "0 x x x"
        Assert.Equal(2, HevcCost.GetExpGolombLength(1, 1));
        Assert.Equal(4, HevcCost.GetExpGolombLength(2, 1));
        Assert.Equal(4, HevcCost.GetExpGolombLength(5, 1));
        Assert.Equal(6, HevcCost.GetExpGolombLength(6, 1));
        Assert.Equal(6, HevcCost.GetExpGolombLength(13, 1));
        Assert.Equal(8, HevcCost.GetExpGolombLength(14, 1));
        Assert.Equal(4, HevcCost.GetExpGolombLength(7, 3));
        Assert.Equal(6, HevcCost.GetExpGolombLength(8, 3));
        Assert.Equal(6 * HevcCost.Scale, HevcCost.GetExpGolombCost(8, 3));

        // The sink writes as many bins as the length says
        var scope = AllocationScope.Create(ImageConfiguration.Default, nameof(ExpGolombCodesHaveTheLengthOfTheirBinarization));
        using var output = new PooledPayloadWriter(scope);
        var sink = new HevcBinSink { Mode = HevcBinSinkMode.Encode };
        sink.Encoder.Initialize(output);
        var random = new Random(3);
        long bins = 0;
        for (var i = 0; i < 2000; i++)
        {
            var k = random.Next(0, 8);
            var value = (uint)random.NextInt64(1L << random.Next(0, 24));
            sink.EncodeExpGolomb(value, k);
            bins += HevcCost.GetExpGolombLength(value, k);
            Assert.Equal(bins, sink.Encoder.BinCountsInNalUnits);
        }
    }
}
