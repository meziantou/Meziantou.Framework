using System.Numerics;

namespace Meziantou.Framework.Imaging.TestHarness.Resampling;

/// <summary>An exact rational number (arbitrary precision), used by the reference resampler for sizes and pixel-center positions.</summary>
public readonly struct Fraction : IEquatable<Fraction>, IComparable<Fraction>
{
    private readonly BigInteger _denominator;

    /// <summary>Initializes a new instance of the <see cref="Fraction"/> struct, normalized (positive denominator, reduced).</summary>
    /// <param name="numerator">The numerator.</param>
    /// <param name="denominator">The non-zero denominator.</param>
    public Fraction(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException();

        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var gcd = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!gcd.IsOne && !gcd.IsZero)
        {
            numerator /= gcd;
            denominator /= gcd;
        }

        Numerator = numerator;
        _denominator = denominator;
    }

    /// <summary>Gets the numerator.</summary>
    public BigInteger Numerator { get; }

    /// <summary>Gets the positive denominator (1 for the default value, which is zero).</summary>
    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    public static implicit operator Fraction(long value) => new(value, 1);

    public static Fraction operator +(Fraction left, Fraction right) => new((left.Numerator * right.Denominator) + (right.Numerator * left.Denominator), left.Denominator * right.Denominator);

    public static Fraction operator -(Fraction left, Fraction right) => new((left.Numerator * right.Denominator) - (right.Numerator * left.Denominator), left.Denominator * right.Denominator);

    public static Fraction operator *(Fraction left, Fraction right) => new(left.Numerator * right.Numerator, left.Denominator * right.Denominator);

    public static Fraction operator /(Fraction left, Fraction right) => new(left.Numerator * right.Denominator, left.Denominator * right.Numerator);

    public static bool operator ==(Fraction left, Fraction right) => left.Equals(right);

    public static bool operator !=(Fraction left, Fraction right) => !left.Equals(right);

    public static bool operator <(Fraction left, Fraction right) => left.CompareTo(right) < 0;

    public static bool operator >(Fraction left, Fraction right) => left.CompareTo(right) > 0;

    public static bool operator <=(Fraction left, Fraction right) => left.CompareTo(right) <= 0;

    public static bool operator >=(Fraction left, Fraction right) => left.CompareTo(right) >= 0;

    /// <summary>Gets the largest integer less than or equal to the value.</summary>
    /// <returns>The floor.</returns>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    /// <summary>Gets the value as a decimal (28 significant digits).</summary>
    /// <returns>The value.</returns>
    public decimal ToDecimal()
    {
        var integer = Floor();
        var remainder = Numerator - (integer * Denominator);
        return (decimal)integer + ((decimal)remainder / (decimal)Denominator);
    }

    /// <inheritdoc/>
    public int CompareTo(Fraction other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    /// <inheritdoc/>
    public bool Equals(Fraction other) => Numerator == other.Numerator && Denominator == other.Denominator;

    /// <inheritdoc/>
    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Fraction other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    /// <inheritdoc/>
    public override string ToString() => Denominator.IsOne ? Numerator.ToString(CultureInfo.InvariantCulture) : string.Create(CultureInfo.InvariantCulture, $"{Numerator}/{Denominator}");
}
