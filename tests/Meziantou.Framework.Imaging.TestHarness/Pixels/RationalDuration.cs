using System.Numerics;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Imaging.TestHarness.Pixels;

/// <summary>
/// An exact, normalized, non-negative rational duration in seconds (zero is <c>0/1</c>). Mirrors the library
/// <c>FrameDuration</c> semantics without depending on it, so expectations are independent of the code under test.
/// </summary>
[StructLayout(LayoutKind.Auto)]
public readonly struct RationalDuration : IEquatable<RationalDuration>
{
    private readonly long _denominatorMinusOne;

    private RationalDuration(long numerator, long denominator)
    {
        Numerator = numerator;
        _denominatorMinusOne = denominator - 1;
    }

    /// <summary>Gets the zero duration.</summary>
    public static RationalDuration Zero => default;

    /// <summary>Gets the normalized numerator.</summary>
    public long Numerator { get; }

    /// <summary>Gets the normalized denominator (positive).</summary>
    public long Denominator => _denominatorMinusOne + 1;

    /// <summary>Creates a normalized duration.</summary>
    /// <param name="numerator">The numerator (non-negative).</param>
    /// <param name="denominator">The denominator (positive).</param>
    /// <returns>The duration.</returns>
    public static RationalDuration Create(long numerator, long denominator)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(numerator);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(denominator);
        if (numerator == 0)
            return Zero;

        var gcd = (long)BigInteger.GreatestCommonDivisor(numerator, denominator);
        return new RationalDuration(numerator / gcd, denominator / gcd);
    }

    /// <summary>Parses <c>numerator/denominator</c>.</summary>
    /// <param name="value">The text.</param>
    /// <param name="result">The duration.</param>
    /// <returns><see langword="true"/> if the text is a valid non-negative fraction with a positive denominator.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out RationalDuration result)
    {
        result = default;
        if (value is null)
            return false;

        var separator = value.IndexOf('/', StringComparison.Ordinal);
        if (separator <= 0
            || !long.TryParse(value.AsSpan(0, separator), NumberStyles.None, CultureInfo.InvariantCulture, out var numerator)
            || !long.TryParse(value.AsSpan(separator + 1), NumberStyles.None, CultureInfo.InvariantCulture, out var denominator)
            || denominator <= 0)
        {
            return false;
        }

        result = Create(numerator, denominator);
        return true;
    }

    /// <summary>Parses <c>numerator/denominator</c>.</summary>
    /// <param name="value">The text.</param>
    /// <returns>The duration.</returns>
    public static RationalDuration Parse(string value)
        => TryParse(value, out var result) ? result : throw new FormatException($"'{value}' is not a valid rational duration (expected 'numerator/denominator' with a positive denominator).");

    /// <summary>Compares two durations.</summary>
    public static bool operator ==(RationalDuration left, RationalDuration right) => left.Equals(right);

    /// <summary>Compares two durations.</summary>
    public static bool operator !=(RationalDuration left, RationalDuration right) => !left.Equals(right);

    /// <inheritdoc/>
    public bool Equals(RationalDuration other) => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is RationalDuration other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <inheritdoc/>
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}
