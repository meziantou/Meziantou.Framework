using Meziantou.Framework.Imaging.Formats;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Tests;

/// <summary>Exact import and policy-driven export of GIF/APNG/ANI timing and play counts.</summary>
public sealed class AnimationTimingTests
{
    [Theory]
    [InlineData(0u, 0, 1)]
    [InlineData(1u, 1, 60)]
    [InlineData(6u, 1, 10)]
    [InlineData(60u, 1, 1)]
    [InlineData(90u, 3, 2)]
    [InlineData(uint.MaxValue, 286331153, 4)] // 4294967295 / 60 = 286331153 / 4
    public void AniRatesImportExactly(uint jiffies, long numerator, long denominator)
    {
        var duration = AnimationTiming.FromAniRate(jiffies);
        Assert.Equal((numerator, denominator), (duration.Numerator, duration.Denominator));
    }

    [Fact]
    public void AniExportIsExactWhenPossibleAndFollowsTheRoundingPolicyOtherwise()
    {
        foreach (var jiffies in (uint[])[0, 1, 2, 59, 60, 61, 3600, uint.MaxValue])
        {
            var duration = AnimationTiming.FromAniRate(jiffies);
            Assert.Equal(jiffies, AnimationTiming.ToAniRate(duration, FrameDurationRounding.RequireExact));
            Assert.Equal(jiffies, AnimationTiming.ToAniRate(duration, FrameDurationRounding.RoundToNearest));
        }

        // 1/120 s is half a jiffy (ties up); 1/121 s is just below
        Assert.Equal(1u, AnimationTiming.ToAniRate(new FrameDuration(1, 120), FrameDurationRounding.RoundToNearest));
        Assert.Equal(0u, AnimationTiming.ToAniRate(new FrameDuration(1, 121), FrameDurationRounding.RoundToNearest));
        Assert.Equal(2u, AnimationTiming.ToAniRate(FrameDuration.FromMilliseconds(40), FrameDurationRounding.RoundToNearest)); // 2.4 jiffies
        Assert.Equal(3u, AnimationTiming.ToAniRate(FrameDuration.FromMilliseconds(42), FrameDurationRounding.RoundToNearest)); // 2.52 jiffies
        var precision = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToAniRate(FrameDuration.FromMilliseconds(40), FrameDurationRounding.RequireExact));
        Assert.Equal((ImageFormat.Ani, "ANI frame duration precision"), (precision.Format, precision.Feature));

        // One jiffy more than the 32-bit field holds, and a value that only exceeds it once rounded up
        foreach (var rounding in Enum.GetValues<FrameDurationRounding>())
        {
            var range = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToAniRate(new FrameDuration(1L << 32, 60), rounding));
            Assert.Equal((ImageFormat.Ani, "ANI frame duration range"), (range.Format, range.Feature));
        }

        Assert.Equal("ANI frame duration range", Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToAniRate(new FrameDuration((2L * uint.MaxValue) + 1, 120), FrameDurationRounding.RoundToNearest)).Feature);
        Assert.Equal(uint.MaxValue, AnimationTiming.ToAniRate(new FrameDuration((2L * uint.MaxValue) - 1, 120), FrameDurationRounding.RoundToNearest));
    }

    [Fact]
    public void AnAnimatedCursorHasNoPlayCount()
    {
        AnimationTiming.EnsureAniPlayCount(null);
        foreach (var plays in (int[])[1, 2, int.MaxValue])
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.EnsureAniPlayCount(plays));
            Assert.Equal((ImageFormat.Ani, "ANI play count"), (exception.Format, exception.Feature));
        }
    }

    [Theory]
    [InlineData(0, 0, 1)]
    [InlineData(7, 7, 100)]
    [InlineData(10, 1, 10)]
    [InlineData(25, 1, 4)]
    [InlineData(65535, 13107, 20)]
    public void GifDelaysImportExactly(ushort hundredths, long numerator, long denominator)
    {
        var duration = AnimationTiming.FromGifDelay(hundredths);
        Assert.Equal((numerator, denominator), (duration.Numerator, duration.Denominator));
        Assert.Equal(TimeSpan.FromMilliseconds(hundredths * 10), duration.ToTimeSpan());
    }

    [Theory]
    [InlineData(1, 10, 1, 10)]
    [InlineData(3, 30, 1, 10)]
    [InlineData(0, 0, 0, 1)] // den 0 means 100
    [InlineData(5, 0, 1, 20)] // den 0 means 100
    [InlineData(50, 100, 1, 2)]
    [InlineData(1, 3, 1, 3)]
    [InlineData(65535, 1, 65535, 1)]
    [InlineData(1, 65535, 1, 65535)]
    public void ApngDelaysImportExactly(ushort delayNumerator, ushort delayDenominator, long numerator, long denominator)
    {
        var duration = AnimationTiming.FromApngDelay(delayNumerator, delayDenominator);
        Assert.Equal((numerator, denominator), (duration.Numerator, duration.Denominator));
    }

    [Fact]
    public void GifExportIsExactWhenPossible()
    {
        for (ushort hundredths = 0; hundredths < 2000; hundredths++)
        {
            var duration = AnimationTiming.FromGifDelay(hundredths);
            Assert.Equal(hundredths, AnimationTiming.ToGifDelay(duration, FrameDurationRounding.RequireExact));
            Assert.Equal(hundredths, AnimationTiming.ToGifDelay(duration, FrameDurationRounding.RoundToNearest));
        }

        Assert.Equal(ushort.MaxValue, AnimationTiming.ToGifDelay(new FrameDuration(65535, 100), FrameDurationRounding.RequireExact));
        Assert.Equal(5, AnimationTiming.ToGifDelay(FrameDuration.FromMilliseconds(50), FrameDurationRounding.RequireExact));
    }

    [Theory]
    [InlineData(1, 200, 1)] // 0.5 hundredth: ties round up
    [InlineData(3, 200, 2)] // 1.5
    [InlineData(1, 300, 0)] // 0.33
    [InlineData(1, 150, 1)] // 0.67
    [InlineData(1, 3, 33)] // 33.33
    [InlineData(2, 3, 67)] // 66.67
    [InlineData(1, 10_000_000, 0)]
    [InlineData(655_349, 1000, 65535)] // 65534.9
    public void GifExportRoundsToNearestHundredthWithTiesUp(long numerator, long denominator, int expected)
    {
        var duration = new FrameDuration(numerator, denominator);
        Assert.Equal(expected, AnimationTiming.ToGifDelay(duration, FrameDurationRounding.RoundToNearest));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToGifDelay(duration, FrameDurationRounding.RequireExact));
        Assert.Equal(ImageFormat.Gif, exception.Format);
        Assert.Equal("GIF frame duration precision", exception.Feature);
    }

    [Theory]
    [InlineData(65536, 100)] // 655.36 s
    [InlineData(655_355, 1000)] // rounds to 65536 hundredths
    [InlineData(long.MaxValue, 1)]
    public void GifExportNeverClampsOutOfRangeDurations(long numerator, long denominator)
    {
        var duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in new[] { FrameDurationRounding.RoundToNearest, FrameDurationRounding.RequireExact })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToGifDelay(duration, rounding));
            Assert.Equal(ImageFormat.Gif, exception.Format);
        }
    }

    [Theory]
    [InlineData(0, 1, 0, 1)]
    [InlineData(1, 10, 1, 10)]
    [InlineData(7, 1000, 7, 1000)]
    [InlineData(65535, 1, 65535, 1)]
    [InlineData(1, 65535, 1, 65535)]
    [InlineData(65535, 65534, 65535, 65534)]
    [InlineData(100_000, 1_000_000, 1, 10)] // reducible fractions are exact after normalization
    public void ApngExportIsExactWhenTheNormalizedFractionFits(long numerator, long denominator, int expectedNumerator, int expectedDenominator)
    {
        var duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in new[] { FrameDurationRounding.RequireExact, FrameDurationRounding.RoundToNearest })
        {
            var (num, den) = AnimationTiming.ToApngDelay(duration, rounding);
            Assert.Equal((expectedNumerator, expectedDenominator), ((int)num, (int)den));
            Assert.NotEqual(0, den);
        }
    }

    [Theory]
    [InlineData(1, 65536)]
    [InlineData(1, 100_000)]
    [InlineData(65537, 65536)]
    [InlineData(1, 10_000_000)]
    [InlineData(123_456_789, 10_000_000)]
    public void ApngStrictExportRejectsInexactDurations(long numerator, long denominator)
    {
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToApngDelay(new FrameDuration(numerator, denominator), FrameDurationRounding.RequireExact));
        Assert.Equal(ImageFormat.Png, exception.Format);
        Assert.Equal("APNG frame duration precision", exception.Feature);
    }

    [Theory]
    [InlineData(65536, 1)]
    [InlineData(131_071, 2)] // 65535.5 s
    [InlineData(long.MaxValue, 1)]
    public void ApngExportNeverClampsOutOfRangeDurations(long numerator, long denominator)
    {
        var duration = new FrameDuration(numerator, denominator);
        foreach (var rounding in new[] { FrameDurationRounding.RoundToNearest, FrameDurationRounding.RequireExact })
        {
            var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToApngDelay(duration, rounding));
            Assert.Equal("APNG frame duration range", exception.Feature);
        }
    }

    [Theory]
    [InlineData(1, 65536, 1, 65535)] // closer to 1/65535 than to 0/1
    [InlineData(1, 131_071, 0, 1)] // 1/131071 is closer to 0 than 1/65535
    [InlineData(1, 131_070, 1, 65535)] // exactly halfway between 0/1 and 1/65535: ties go to the longer duration
    [InlineData(3_333_333, 10_000_000, 1, 3)] // 0.3333333 ~ 1/3
    [InlineData(31_415_926_535, 10_000_000_000, 65298, 20785)] // pi: a semiconvergent beyond 355/113 (103993/33102 does not fit)
    [InlineData(4_294_836_225, 65536, 65534, 1)] // 65534.00002 s
    public void ApngRoundingSelectsTheNearestRepresentableFraction(long numerator, long denominator, int expectedNumerator, int expectedDenominator)
    {
        var (num, den) = AnimationTiming.ToApngDelay(new FrameDuration(numerator, denominator), FrameDurationRounding.RoundToNearest);
        Assert.Equal((expectedNumerator, expectedDenominator), ((int)num, (int)den));
    }

    [Fact]
    [SuppressMessage("Security", "CA5394:Do not use insecure randomness", Justification = "Deterministic seeded test inputs.")]
    public void ApngRoundingMatchesBruteForceSearch()
    {
        var random = new Random(2026);
        for (var i = 0; i < 150; i++)
        {
            long numerator, denominator;
            switch (i % 3)
            {
                case 0:
                    denominator = random.NextInt64(65536, 10_000_000_000);
                    numerator = random.NextInt64(0, denominator * 3);
                    break;
                case 1:
                    denominator = TimeSpan.TicksPerSecond;
                    numerator = random.NextInt64(1, 100 * TimeSpan.TicksPerSecond);
                    break;
                default:
                    denominator = random.NextInt64(65536, long.MaxValue / 70_000);
                    numerator = random.NextInt64(0, denominator * 65_000);
                    break;
            }

            var duration = new FrameDuration(numerator, denominator);
            if (duration.Numerator <= ushort.MaxValue && duration.Denominator <= ushort.MaxValue)
                continue;

            var (num, den) = AnimationTiming.ToApngDelay(duration, FrameDurationRounding.RoundToNearest);
            var expected = BruteForceNearest(duration.Numerator, duration.Denominator);
            Assert.Equal(new FrameDuration(expected.Numerator, expected.Denominator), new FrameDuration(num, den));
        }
    }

    [Fact]
    public void ApngPlayCountsFollowTheZeroMeansInfiniteConvention()
    {
        Assert.Null(AnimationTiming.FromApngNumPlays(0));
        Assert.Equal(1, AnimationTiming.FromApngNumPlays(1));
        Assert.Equal(3, AnimationTiming.FromApngNumPlays(3));
        Assert.Equal(int.MaxValue, AnimationTiming.FromApngNumPlays(int.MaxValue));
        var exception = Assert.Throws<InvalidImageContentException>(() => AnimationTiming.FromApngNumPlays((uint)int.MaxValue + 1));
        Assert.Equal(ImageFormat.Png, exception.Format);

        Assert.Equal(0u, AnimationTiming.ToApngNumPlays(null));
        Assert.Equal(1u, AnimationTiming.ToApngNumPlays(1));
        Assert.Equal((uint)int.MaxValue, AnimationTiming.ToApngNumPlays(int.MaxValue));
    }

    [Fact]
    public void GifLoopCountsStoreRepetitions()
    {
        Assert.Equal(1, AnimationTiming.FromGifLoopCount(null)); // no NETSCAPE2.0 extension: played once
        Assert.Null(AnimationTiming.FromGifLoopCount(0)); // 0 repetitions field means infinite
        Assert.Equal(2, AnimationTiming.FromGifLoopCount(1));
        Assert.Equal(65536, AnimationTiming.FromGifLoopCount(ushort.MaxValue));

        Assert.Equal((ushort)0, AnimationTiming.ToGifLoopCount(null));
        Assert.Null(AnimationTiming.ToGifLoopCount(1)); // a single play omits the extension
        Assert.Equal((ushort)1, AnimationTiming.ToGifLoopCount(2));
        Assert.Equal(ushort.MaxValue, AnimationTiming.ToGifLoopCount(65536));
        var exception = Assert.Throws<UnsupportedImageFeatureException>(() => AnimationTiming.ToGifLoopCount(65537));
        Assert.Equal(ImageFormat.Gif, exception.Format);
        Assert.Equal("GIF play count", exception.Feature);

        for (var plays = 1; plays <= 65536; plays += 1111)
        {
            Assert.Equal(plays, AnimationTiming.FromGifLoopCount(AnimationTiming.ToGifLoopCount(plays)));
        }
    }

    private static (long Numerator, long Denominator) BruteForceNearest(long p, long q)
    {
        (long, long) best = (0, 1);
        Int128 bestDistanceNumerator = p; // |p/q - 0/1| = p/q -> compare as fractions over q*b
        long bestDenominator = 1;
        for (long b = 1; b <= ushort.MaxValue; b++)
        {
            // Candidates floor(x*b) and ceil(x*b), limited to the numerator range
            var floor = (long)((Int128)p * b / q);
            foreach (var a in new[] { floor, floor + 1 })
            {
                if (a is < 0 or > ushort.MaxValue)
                    continue;

                var distance = Int128.Abs(((Int128)p * b) - ((Int128)a * q)); // |x - a/b| * q * b
                var compare = (distance * bestDenominator).CompareTo(bestDistanceNumerator * b);
                if (compare < 0 || (compare == 0 && (Int128)a * best.Item2 > (Int128)best.Item1 * b))
                {
                    best = (a, b);
                    bestDistanceNumerator = distance;
                    bestDenominator = b;
                }
            }
        }

        return best;
    }
}
