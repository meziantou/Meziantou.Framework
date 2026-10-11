using System.Globalization;
using System.Numerics;

namespace Meziantou.Framework.Imaging.CorpusGenerator.Infrastructure;

/// <summary>An exact rational number (always normalized: positive denominator, lowest terms).</summary>
internal readonly struct Rational : IEquatable<Rational>, IComparable<Rational>
{
    private readonly BigInteger _denominator;

    public Rational(BigInteger numerator, BigInteger denominator)
    {
        if (denominator.IsZero)
            throw new DivideByZeroException();
        if (denominator.Sign < 0)
        {
            numerator = -numerator;
            denominator = -denominator;
        }

        var divisor = BigInteger.GreatestCommonDivisor(numerator, denominator);
        if (!divisor.IsOne && !divisor.IsZero)
        {
            numerator /= divisor;
            denominator /= divisor;
        }

        Numerator = numerator;
        _denominator = denominator;
    }

    public BigInteger Numerator { get; }

    // default(Rational) is zero: its denominator is 1
    public BigInteger Denominator => _denominator.IsZero ? BigInteger.One : _denominator;

    public static Rational Pow10(int exponent) => new(BigInteger.Pow(10, exponent), 1);

    public static implicit operator Rational(long value) => new(value, 1);

    public static implicit operator Rational(BigInteger value) => new(value, 1);

    public static Rational operator +(Rational a, Rational b) => new(a.Numerator * b.Denominator + b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a, Rational b) => new(a.Numerator * b.Denominator - b.Numerator * a.Denominator, a.Denominator * b.Denominator);

    public static Rational operator -(Rational a) => new(-a.Numerator, a.Denominator);

    public static Rational operator *(Rational a, Rational b) => new(a.Numerator * b.Numerator, a.Denominator * b.Denominator);

    public static Rational operator /(Rational a, Rational b) => new(a.Numerator * b.Denominator, a.Denominator * b.Numerator);

    public static bool operator ==(Rational a, Rational b) => a.Equals(b);

    public static bool operator !=(Rational a, Rational b) => !a.Equals(b);

    public static bool operator <(Rational a, Rational b) => a.CompareTo(b) < 0;

    public static bool operator >(Rational a, Rational b) => a.CompareTo(b) > 0;

    public static bool operator <=(Rational a, Rational b) => a.CompareTo(b) <= 0;

    public static bool operator >=(Rational a, Rational b) => a.CompareTo(b) >= 0;

    /// <summary>math.floor: the greatest integer not above the value.</summary>
    public BigInteger Floor()
    {
        var quotient = BigInteger.DivRem(Numerator, Denominator, out var remainder);
        return remainder.Sign < 0 ? quotient - 1 : quotient;
    }

    /// <summary>math.ceil: the least integer not below the value.</summary>
    public BigInteger Ceiling() => -(-this).Floor();

    public double ToDouble() => (double)Numerator / (double)Denominator;

    public int CompareTo(Rational other) => (Numerator * other.Denominator).CompareTo(other.Numerator * Denominator);

    public bool Equals(Rational other) => Numerator == other.Numerator && Denominator == other.Denominator;

    public override bool Equals([NotNullWhen(true)] object? obj) => obj is Rational other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Numerator, Denominator);

    public override string ToString() => Denominator.IsOne
        ? Numerator.ToString(CultureInfo.InvariantCulture)
        : Numerator.ToString(CultureInfo.InvariantCulture) + "/" + Denominator.ToString(CultureInfo.InvariantCulture);
}
