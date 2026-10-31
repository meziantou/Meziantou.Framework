using System.Diagnostics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging;

/// <summary>An exact, non-negative frame display duration expressed as a normalized rational number of seconds.</summary>
/// <remarks>
/// <para>
/// Durations are stored exactly so that GIF (hundredths of a second) and APNG (<c>delay_num / delay_den</c>) timing
/// survives import without rounding. The value is normalized: the numerator and the denominator have no common divisor,
/// the denominator is positive, and zero is always represented as <c>0/1</c>. Two durations are equal when they represent
/// the same rational value.
/// </para>
/// <para>
/// <c>default(FrameDuration)</c> is a valid zero duration. Zero durations are preserved as-is; the library never applies the
/// minimum-delay heuristics used by some players.
/// </para>
/// </remarks>
[StructLayout(LayoutKind.Auto)]
public readonly struct FrameDuration : IEquatable<FrameDuration>, IComparable<FrameDuration>, IComparable
{
    private const long TicksPerSecond = TimeSpan.TicksPerSecond;

    private readonly long _numerator;

    // Stored as (denominator - 1) so that default(FrameDuration) is the valid value 0/1
    private readonly long _denominatorMinusOne;

    /// <summary>Initializes a new instance of the <see cref="FrameDuration"/> struct from a rational number of seconds.</summary>
    /// <param name="numerator">The numerator, in seconds. Must be zero or positive.</param>
    /// <param name="denominator">The denominator. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="numerator"/> is negative or <paramref name="denominator"/> is not positive.</exception>
    public FrameDuration(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);

        if (numerator == 0)
        {
            _numerator = 0;
            _denominatorMinusOne = 0;
            return;
        }

        var gcd = GreatestCommonDivisor(numerator, denominator);
        _numerator = numerator / gcd;
        _denominatorMinusOne = (denominator / gcd) - 1;
    }

    /// <summary>Gets a zero duration.</summary>
    public static FrameDuration Zero => default;

    /// <summary>Gets the normalized numerator, in seconds.</summary>
    public long Numerator => _numerator;

    /// <summary>Gets the normalized denominator. Always positive.</summary>
    public long Denominator => _denominatorMinusOne + 1;

    /// <summary>Gets a value indicating whether the duration is zero.</summary>
    public bool IsZero => _numerator == 0;

    /// <summary>Gets the duration in seconds, as a floating-point approximation.</summary>
    public double TotalSeconds => (double)_numerator / Denominator;

    /// <summary>Gets the duration in milliseconds, as a floating-point approximation.</summary>
    public double TotalMilliseconds => (double)((Int128)_numerator * 1000) / Denominator;

    /// <summary>Creates a duration from a whole number of milliseconds.</summary>
    /// <param name="milliseconds">The number of milliseconds. Must be zero or positive.</param>
    /// <returns>The exact duration <c>milliseconds / 1000</c> seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="milliseconds"/> is negative.</exception>
    public static FrameDuration FromMilliseconds(long milliseconds)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(milliseconds);
        return new FrameDuration(milliseconds, 1000);
    }

    /// <summary>Creates a duration from a <see cref="TimeSpan"/>. The conversion is exact (ticks are 100 ns).</summary>
    /// <param name="value">The time span. Must be zero or positive.</param>
    /// <returns>The exact duration <c>ticks / 10,000,000</c> seconds.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative.</exception>
    public static FrameDuration FromTimeSpan(TimeSpan value)
    {
        if (value < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The duration must not be negative.");

        return new FrameDuration(value.Ticks, TicksPerSecond);
    }

    /// <summary>Converts the duration to a <see cref="TimeSpan"/>, rounding to the nearest tick (ties round up).</summary>
    /// <remarks>
    /// The conversion is exact when the duration is a whole number of 100 ns ticks (for example any duration created by
    /// <see cref="FromTimeSpan(TimeSpan)"/> or <see cref="FromMilliseconds(long)"/>, and GIF hundredths). Otherwise the result
    /// is the nearest tick, rounding exact halves up; the computation uses 128-bit integers and never loses precision
    /// before rounding. Durations that round above <see cref="TimeSpan.MaxValue"/> throw instead of being clamped.
    /// </remarks>
    /// <returns>The time span.</returns>
    /// <exception cref="OverflowException">The duration is greater than <see cref="TimeSpan.MaxValue"/>.</exception>
    public TimeSpan ToTimeSpan()
    {
        var denominator = (Int128)Denominator;
        var scaled = (Int128)_numerator * TicksPerSecond;
        var ticks = Int128.DivRem(scaled, denominator);
        var result = ticks.Quotient;
        if (ticks.Remainder * 2 >= denominator)
        {
            result++;
        }

        if (result > TimeSpan.MaxValue.Ticks)
            throw new OverflowException("The duration is too large to be represented as a TimeSpan.");

        return TimeSpan.FromTicks((long)result);
    }

    /// <summary>Returns the exact sum of this duration and another one, like <c>this + other</c>.</summary>
    /// <param name="other">The duration to add.</param>
    /// <returns>The normalized sum.</returns>
    /// <remarks>
    /// The sum is computed exactly with 128-bit integers and then normalized. It is never rounded: a sum whose normalized
    /// numerator or denominator does not fit in a <see cref="long"/> throws instead (for example two durations whose
    /// denominators are large coprime numbers).
    /// </remarks>
    /// <exception cref="OverflowException">The normalized numerator or denominator of the sum is greater than <see cref="long.MaxValue"/>.</exception>
    public FrameDuration Add(FrameDuration other)
    {
        if (other.IsZero)
            return this;

        if (IsZero)
            return other;

        if (Denominator == other.Denominator)
            return FromInt128((Int128)_numerator + other._numerator, Denominator);

        var numerator = ((Int128)_numerator * other.Denominator) + ((Int128)other._numerator * Denominator);
        return FromInt128(numerator, (Int128)Denominator * other.Denominator);
    }

    /// <summary>Returns the exact difference of this duration and another one, like <c>this - other</c>.</summary>
    /// <param name="other">The duration to subtract. It must not be longer than this duration.</param>
    /// <returns>The normalized difference.</returns>
    /// <remarks>
    /// The difference is computed exactly, like <see cref="Add(FrameDuration)"/>. A duration is never negative, so a
    /// difference below zero throws instead of being clamped to <see cref="Zero"/>.
    /// </remarks>
    /// <exception cref="OverflowException"><paramref name="other"/> is longer than this duration, or the normalized denominator of the difference is greater than <see cref="long.MaxValue"/>.</exception>
    public FrameDuration Subtract(FrameDuration other)
    {
        if (other.IsZero)
            return this;

        var numerator = ((Int128)_numerator * other.Denominator) - ((Int128)other._numerator * Denominator);
        if (numerator < 0)
            throw new OverflowException("The difference is negative: a frame duration cannot be negative.");

        return FromInt128(numerator, (Int128)Denominator * other.Denominator);
    }

    /// <inheritdoc />
    public bool Equals(FrameDuration other) => _numerator == other._numerator && _denominatorMinusOne == other._denominatorMinusOne;

    /// <inheritdoc />
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is FrameDuration other && Equals(other);

    /// <inheritdoc />
    public override int GetHashCode() => HashCode.Combine(_numerator, _denominatorMinusOne);

    /// <inheritdoc />
    public int CompareTo(FrameDuration other)
    {
        var left = (Int128)_numerator * other.Denominator;
        var right = (Int128)other._numerator * Denominator;
        return left.CompareTo(right);
    }

    /// <inheritdoc />
    public int CompareTo(object? obj)
    {
        if (obj is null)
            return 1;

        if (obj is FrameDuration other)
            return CompareTo(other);

        throw new ArgumentException($"Object must be of type {nameof(FrameDuration)}.", nameof(obj));
    }

    /// <summary>Returns the duration formatted as <c>{Numerator}/{Denominator} s</c>.</summary>
    /// <returns>A string representation of the duration.</returns>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator} s");

    /// <summary>Compares two durations for equality.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if both represent the same rational value; otherwise <see langword="false"/>.</returns>
    public static bool operator ==(FrameDuration left, FrameDuration right) => left.Equals(right);

    /// <summary>Compares two durations for inequality.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if the durations differ; otherwise <see langword="false"/>.</returns>
    public static bool operator !=(FrameDuration left, FrameDuration right) => !left.Equals(right);

    /// <summary>Determines whether a duration is shorter than another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is shorter than <paramref name="right"/>.</returns>
    public static bool operator <(FrameDuration left, FrameDuration right) => left.CompareTo(right) < 0;

    /// <summary>Determines whether a duration is shorter than or equal to another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is shorter than or equal to <paramref name="right"/>.</returns>
    public static bool operator <=(FrameDuration left, FrameDuration right) => left.CompareTo(right) <= 0;

    /// <summary>Determines whether a duration is longer than another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is longer than <paramref name="right"/>.</returns>
    public static bool operator >(FrameDuration left, FrameDuration right) => left.CompareTo(right) > 0;

    /// <summary>Determines whether a duration is longer than or equal to another.</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns><see langword="true"/> if <paramref name="left"/> is longer than or equal to <paramref name="right"/>.</returns>
    public static bool operator >=(FrameDuration left, FrameDuration right) => left.CompareTo(right) >= 0;

    /// <summary>Adds two durations exactly (see <see cref="Add(FrameDuration)"/>).</summary>
    /// <param name="left">The first duration.</param>
    /// <param name="right">The second duration.</param>
    /// <returns>The normalized sum.</returns>
    /// <exception cref="OverflowException">The normalized numerator or denominator of the sum is greater than <see cref="long.MaxValue"/>.</exception>
    public static FrameDuration operator +(FrameDuration left, FrameDuration right) => left.Add(right);

    /// <summary>Subtracts a duration from another exactly (see <see cref="Subtract(FrameDuration)"/>).</summary>
    /// <param name="left">The duration to subtract from.</param>
    /// <param name="right">The duration to subtract. It must not be longer than <paramref name="left"/>.</param>
    /// <returns>The normalized difference.</returns>
    /// <exception cref="OverflowException"><paramref name="right"/> is longer than <paramref name="left"/>, or the normalized denominator of the difference is greater than <see cref="long.MaxValue"/>.</exception>
    public static FrameDuration operator -(FrameDuration left, FrameDuration right) => left.Subtract(right);

    private static FrameDuration FromInt128(Int128 numerator, Int128 denominator)
    {
        Debug.Assert(numerator >= 0 && denominator > 0);
        if (numerator == 0)
            return Zero;

        var gcd = GreatestCommonDivisor(numerator, denominator);
        numerator /= gcd;
        denominator /= gcd;
        if (numerator > long.MaxValue || denominator > long.MaxValue)
            throw new OverflowException("The result cannot be represented exactly as a frame duration: its normalized numerator or denominator exceeds 64 bits.");

        return new FrameDuration((long)numerator, (long)denominator);
    }

    private static Int128 GreatestCommonDivisor(Int128 a, Int128 b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static long GreatestCommonDivisor(long a, long b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }
}
