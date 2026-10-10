using System.Diagnostics;
using Meziantou.Framework.Imaging.Formats;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Conversions between the exact public timing model (<see cref="FrameDuration"/>, <c>AnimationMetadata.TotalPlays</c>)
/// and the encoded fields of animated formats. Shared by every codec so that import is always
/// exact and export follows <see cref="FrameDurationRounding"/> uniformly.
/// </summary>
[SuppressMessage("Design", "MA0182:Unused internal type", Justification = "Shared codec infrastructure; consumed by the codecs and covered by unit tests.")]
internal static class AnimationTiming
{
    /// <summary>The largest value of a 16-bit timing field (GIF delay, APNG <c>delay_num</c>/<c>delay_den</c>).</summary>
    public const int MaxUInt16Field = ushort.MaxValue;

    /// <summary>The largest <c>TotalPlays</c> value representable by the GIF NETSCAPE2.0 loop extension (65,535 repetitions + the first play).</summary>
    public const int MaxGifTotalPlays = ushort.MaxValue + 1;

    /// <summary>The largest WebP <c>ANMF</c> frame duration, in milliseconds (a 24-bit field).</summary>
    public const int MaxWebPDuration = (1 << 24) - 1;

    /// <summary>Converts a WebP <c>ANMF</c> frame duration (milliseconds) to the exact duration <c>duration / 1000</c>.</summary>
    /// <param name="milliseconds">The encoded 24-bit duration. Zero is preserved (no player minimum-delay heuristic).</param>
    /// <returns>The exact duration.</returns>
    public static FrameDuration FromWebPDuration(int milliseconds) => FrameDuration.FromMilliseconds(milliseconds);

    /// <summary>Converts a duration to a WebP <c>ANMF</c> frame duration in milliseconds.</summary>
    /// <param name="duration">The duration.</param>
    /// <param name="rounding">
    /// <see cref="FrameDurationRounding.RequireExact"/> throws unless <c>duration * 1000</c> is an integer;
    /// <see cref="FrameDurationRounding.RoundToNearest"/> rounds to the nearest millisecond (ties up).
    /// </param>
    /// <returns>The encoded duration.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The duration is not exactly representable (strict mode) or exceeds 16,777.215 seconds after rounding.</exception>
    public static int ToWebPDuration(FrameDuration duration, FrameDurationRounding rounding)
    {
        var scaled = (UInt128)(ulong)duration.Numerator * 1000;
        var denominator = (UInt128)(ulong)duration.Denominator;
        var (quotient, remainder) = UInt128.DivRem(scaled, denominator);
        if (remainder != 0)
        {
            if (rounding == FrameDurationRounding.RequireExact)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} is not a whole number of milliseconds and cannot be stored exactly in a WebP file. Use FrameDurationRounding.RoundToNearest to round it."), ImageFormat.WebP, "WebP frame duration precision");

            if (remainder * 2 >= denominator)
            {
                quotient++;
            }
        }

        if (quotient > MaxWebPDuration)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} exceeds the maximum WebP frame duration of 16777.215 seconds."), ImageFormat.WebP, "WebP frame duration range");

        return (int)quotient;
    }

    /// <summary>Converts a WebP <c>ANIM</c> loop count to <c>TotalPlays</c>: 0 means infinite, <c>n</c> means <c>n</c> plays in total.</summary>
    /// <param name="loopCount">The encoded loop count.</param>
    /// <returns>The total number of plays, or <see langword="null"/> for infinite.</returns>
    public static int? FromWebPLoopCount(ushort loopCount) => loopCount == 0 ? null : loopCount;

    /// <summary>Converts <c>TotalPlays</c> to a WebP <c>ANIM</c> loop count (infinite is 0).</summary>
    /// <param name="totalPlays">The total number of plays (positive), or <see langword="null"/> for infinite.</param>
    /// <returns>The loop count.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The play count exceeds 65,535.</exception>
    public static ushort ToWebPLoopCount(int? totalPlays)
    {
        Debug.Assert(totalPlays is null or > 0);
        if (totalPlays is null)
            return 0;

        if (totalPlays.Value > MaxUInt16Field)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A WebP animation can be played at most {MaxUInt16Field} times (or infinitely); {totalPlays.Value} plays cannot be represented."), ImageFormat.WebP, "WebP play count");

        return (ushort)totalPlays.Value;
    }

    /// <summary>Converts an ANI display rate (jiffies, sixtieths of a second) to the exact duration <c>jiffies / 60</c>.</summary>
    /// <param name="jiffies">The encoded rate. Zero is preserved (no player minimum-delay heuristic).</param>
    /// <returns>The exact duration.</returns>
    public static FrameDuration FromAniRate(uint jiffies) => new(jiffies, 60);

    /// <summary>Converts a duration to an ANI display rate in jiffies (sixtieths of a second).</summary>
    /// <param name="duration">The duration.</param>
    /// <param name="rounding">
    /// <see cref="FrameDurationRounding.RequireExact"/> throws unless <c>duration * 60</c> is an integer;
    /// <see cref="FrameDurationRounding.RoundToNearest"/> rounds to the nearest jiffy (ties up).
    /// </param>
    /// <returns>The encoded rate.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The duration is not exactly representable (strict mode) or exceeds 2^32 - 1 jiffies after rounding.</exception>
    public static uint ToAniRate(FrameDuration duration, FrameDurationRounding rounding)
    {
        var scaled = (UInt128)(ulong)duration.Numerator * 60;
        var denominator = (UInt128)(ulong)duration.Denominator;
        var (quotient, remainder) = UInt128.DivRem(scaled, denominator);
        if (remainder != 0)
        {
            if (rounding == FrameDurationRounding.RequireExact)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} is not a whole number of sixtieths of a second and cannot be stored exactly in an ANI file. Use FrameDurationRounding.RoundToNearest to round it."), ImageFormat.Ani, "ANI frame duration precision");

            if (remainder * 2 >= denominator)
            {
                quotient++;
            }
        }

        if (quotient > uint.MaxValue)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} exceeds the maximum ANI rate of {uint.MaxValue} sixtieths of a second."), ImageFormat.Ani, "ANI frame duration range");

        return (uint)quotient;
    }

    /// <summary>Validates that a play count can be stored by an animated cursor, which has no play count: it always loops.</summary>
    /// <param name="totalPlays">The total number of plays (positive), or <see langword="null"/> for infinite.</param>
    /// <exception cref="UnsupportedImageFeatureException">The play count is finite.</exception>
    public static void EnsureAniPlayCount(int? totalPlays)
    {
        Debug.Assert(totalPlays is null or > 0);
        if (totalPlays is { } plays)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"An animated cursor always loops: {plays} play(s) cannot be represented. Set AnimationMetadata.TotalPlays to null."), ImageFormat.Ani, "ANI play count");
    }

    /// <summary>Converts a GIF Graphic Control Extension delay (hundredths of a second) to the exact duration <c>delay / 100</c>.</summary>
    /// <param name="hundredths">The encoded delay. Zero is preserved (no player minimum-delay heuristic).</param>
    /// <returns>The exact duration.</returns>
    public static FrameDuration FromGifDelay(ushort hundredths) => new(hundredths, 100);

    /// <summary>Converts APNG <c>fcTL</c> delay fields to the exact duration <c>num / den</c>, where <c>den == 0</c> means 100.</summary>
    /// <param name="numerator">The encoded <c>delay_num</c>.</param>
    /// <param name="denominator">The encoded <c>delay_den</c>; 0 is interpreted as 100 (APNG specification).</param>
    /// <returns>The exact duration.</returns>
    public static FrameDuration FromApngDelay(ushort numerator, ushort denominator) => new(numerator, denominator == 0 ? 100 : denominator);

    /// <summary>Converts a duration to a GIF delay in hundredths of a second.</summary>
    /// <param name="duration">The duration.</param>
    /// <param name="rounding">
    /// <see cref="FrameDurationRounding.RequireExact"/> throws unless <c>duration * 100</c> is an integer;
    /// <see cref="FrameDurationRounding.RoundToNearest"/> rounds to the nearest hundredth (ties up).
    /// </param>
    /// <returns>The encoded delay.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The duration is not exactly representable (strict mode) or exceeds 655.35 seconds after rounding.</exception>
    public static ushort ToGifDelay(FrameDuration duration, FrameDurationRounding rounding)
    {
        var scaled = (UInt128)(ulong)duration.Numerator * 100;
        var denominator = (UInt128)(ulong)duration.Denominator;
        var (quotient, remainder) = UInt128.DivRem(scaled, denominator);
        if (remainder != 0)
        {
            if (rounding == FrameDurationRounding.RequireExact)
                throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} is not a whole number of hundredths of a second and cannot be stored exactly in a GIF file. Use FrameDurationRounding.RoundToNearest to round it."), ImageFormat.Gif, "GIF frame duration precision");

            if (remainder * 2 >= denominator)
            {
                quotient++;
            }
        }

        if (quotient > MaxUInt16Field)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} exceeds the maximum GIF delay of 655.35 seconds."), ImageFormat.Gif, "GIF frame duration range");

        return (ushort)quotient;
    }

    /// <summary>Converts a duration to APNG <c>delay_num</c>/<c>delay_den</c> fields.</summary>
    /// <param name="duration">The duration.</param>
    /// <param name="rounding">
    /// <see cref="FrameDurationRounding.RequireExact"/> throws unless the normalized fraction fits in 16-bit fields;
    /// <see cref="FrameDurationRounding.RoundToNearest"/> selects the nearest fraction whose numerator and denominator both
    /// fit (ties toward the longer duration).
    /// </param>
    /// <returns>The encoded fields. Zero is encoded as <c>0/1</c>; the denominator is never 0.</returns>
    /// <exception cref="UnsupportedImageFeatureException">The duration is not exactly representable (strict mode) or exceeds 65,535 seconds.</exception>
    public static (ushort Numerator, ushort Denominator) ToApngDelay(FrameDuration duration, FrameDurationRounding rounding)
    {
        var p = duration.Numerator;
        var q = duration.Denominator;
        if (p <= MaxUInt16Field && q <= MaxUInt16Field)
            return ((ushort)p, (ushort)q); // Normalized fractions are the smallest representation: exact

        // Any equivalent fraction is a multiple of the normalized one, so nothing else can be exact
        if ((Int128)p > (Int128)MaxUInt16Field * q)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} exceeds the maximum APNG delay of 65535 seconds."), ImageFormat.Png, "APNG frame duration range");

        if (rounding == FrameDurationRounding.RequireExact)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"The frame duration {duration} cannot be stored exactly in 16-bit APNG delay fields. Use FrameDurationRounding.RoundToNearest to select the nearest representable fraction."), ImageFormat.Png, "APNG frame duration precision");

        var (numerator, denominator) = NearestBoundedFraction(p, q, MaxUInt16Field);
        return ((ushort)numerator, (ushort)denominator);
    }

    /// <summary>
    /// Returns the fraction <c>a/b</c> nearest to <c>p/q</c> with <c>0 &lt;= a &lt;= max</c> and <c>1 &lt;= b &lt;= max</c>;
    /// on a tie, the larger fraction. <c>p/q</c> must not exceed <c>max</c>.
    /// </summary>
    /// <remarks>
    /// Walks the Stern–Brocot tree with accelerated steps (equivalent to continued-fraction convergents and semiconvergents).
    /// When no mediant of the two current bounds fits within <paramref name="max"/>, every fraction strictly between them has a
    /// numerator or a denominator above <paramref name="max"/>, so the bounds are the best one-sided approximations.
    /// All products fit in 128-bit integers (inputs are at most 2^63, bounds at most 2^16).
    /// </remarks>
    internal static (long Numerator, long Denominator) NearestBoundedFraction(long p, long q, long max)
    {
        Debug.Assert(p >= 0 && q > 0 && max > 0);
        Debug.Assert((Int128)p <= (Int128)max * q);

        // Lower bound a/b <= p/q, upper bound c/d > p/q (1/0 is +infinity)
        Int128 a = 0, b = 1, c = 1, d = 0;
        Int128 bigP = p, bigQ = q, bigMax = max;
        while (true)
        {
            var mediantNumerator = a + c;
            var mediantDenominator = b + d;
            if (mediantNumerator > bigMax || mediantDenominator > bigMax)
                break;

            var compare = (mediantNumerator * bigQ).CompareTo(bigP * mediantDenominator);
            if (compare == 0)
                return ((long)mediantNumerator, (long)mediantDenominator);

            if (compare < 0)
            {
                // Mediant below the target: advance the lower bound k times toward the upper bound
                // (a + k c) / (b + k d) <= p/q  <=>  k (q c - p d) <= p b - q a
                var k = (bigP * b - bigQ * a) / (bigQ * c - bigP * d);
                k = Int128.Min(k, LimitSteps(a, c, bigMax));
                k = Int128.Min(k, LimitSteps(b, d, bigMax));
                a += k * c;
                b += k * d;
            }
            else
            {
                // Mediant above the target: advance the upper bound k times toward the lower bound
                // (c + k a) / (d + k b) > p/q  <=>  k (p b - q a) < q c - p d
                var numerator = bigQ * c - bigP * d;
                var step = bigP * b - bigQ * a;
                var k = (numerator - 1) / step;
                k = Int128.Min(k, LimitSteps(c, a, bigMax));
                k = Int128.Min(k, LimitSteps(d, b, bigMax));
                c += k * a;
                d += k * b;
            }

            if (a * bigQ == bigP * b)
                return ((long)a, (long)b);
        }

        if (d == 0)
            return ((long)a, (long)b);

        // Pick the closer bound; ties go to the upper (longer) bound
        // p/q - a/b = (p b - q a) / (q b), c/d - p/q = (q c - p d) / (q d)
        var lowerDistance = (bigP * b - bigQ * a) * d;
        var upperDistance = (bigQ * c - bigP * d) * b;
        return lowerDistance < upperDistance ? ((long)a, (long)b) : ((long)c, (long)d);

        static Int128 LimitSteps(Int128 start, Int128 increment, Int128 max)
            => increment == 0 ? Int128.MaxValue : (max - start) / increment;
    }

    /// <summary>Converts an APNG <c>acTL num_plays</c> field to <c>TotalPlays</c>: 0 means infinite (<see langword="null"/>).</summary>
    /// <param name="numPlays">The encoded field.</param>
    /// <returns>The total number of plays, or <see langword="null"/> for infinite.</returns>
    /// <exception cref="InvalidImageContentException">The value exceeds the PNG 4-byte integer limit (2^31 - 1).</exception>
    public static int? FromApngNumPlays(uint numPlays)
    {
        if (numPlays > int.MaxValue)
            throw new InvalidImageContentException(string.Create(CultureInfo.InvariantCulture, $"The APNG num_plays value {numPlays} exceeds the PNG integer limit (2^31 - 1)."), ImageFormat.Png);

        return numPlays == 0 ? null : (int)numPlays;
    }

    /// <summary>Converts <c>TotalPlays</c> to an APNG <c>acTL num_plays</c> field (infinite is 0).</summary>
    /// <param name="totalPlays">The total number of plays (positive), or <see langword="null"/> for infinite.</param>
    /// <returns>The encoded field.</returns>
    public static uint ToApngNumPlays(int? totalPlays)
    {
        Debug.Assert(totalPlays is null or > 0);
        return totalPlays is null ? 0u : (uint)totalPlays.Value;
    }

    /// <summary>
    /// Converts a GIF NETSCAPE2.0 loop count to <c>TotalPlays</c>. The loop count stores <em>repetitions</em> after the first
    /// play: 0 means infinite, <c>L &gt; 0</c> means <c>L + 1</c> plays, and an absent extension means a single play.
    /// </summary>
    /// <param name="loopCount">The encoded loop count, or <see langword="null"/> when the file has no loop extension.</param>
    /// <returns>The total number of plays, or <see langword="null"/> for infinite.</returns>
    public static int? FromGifLoopCount(ushort? loopCount) => loopCount switch
    {
        null => 1,
        0 => null,
        var value => value.Value + 1,
    };

    /// <summary>Converts <c>TotalPlays</c> to a GIF NETSCAPE2.0 loop count.</summary>
    /// <param name="totalPlays">The total number of plays (positive), or <see langword="null"/> for infinite.</param>
    /// <returns>The loop count to write, or <see langword="null"/> when the extension must be omitted (a single play).</returns>
    /// <exception cref="UnsupportedImageFeatureException">The play count exceeds 65,536.</exception>
    public static ushort? ToGifLoopCount(int? totalPlays)
    {
        Debug.Assert(totalPlays is null or > 0);
        if (totalPlays is null)
            return 0;

        if (totalPlays.Value == 1)
            return null;

        if (totalPlays.Value > MaxGifTotalPlays)
            throw new UnsupportedImageFeatureException(string.Create(CultureInfo.InvariantCulture, $"A GIF animation can be played at most {MaxGifTotalPlays} times (or infinitely); {totalPlays.Value} plays cannot be represented."), ImageFormat.Gif, "GIF play count");

        return (ushort)(totalPlays.Value - 1);
    }
}
