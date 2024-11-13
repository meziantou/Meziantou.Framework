using System.Globalization;

namespace Meziantou.Framework.Diagnostics.ContextSnapshot.Internals;

internal readonly struct Frequency
{
    public static readonly Frequency Zero = new(0.0);

    public static readonly Frequency MHz = FrequencyUnit.MHz.ToFrequency(1L);

    public double Hertz { get; }

    public Frequency(double hertz)
    {
        Hertz = hertz;
    }

    public Frequency(double value, FrequencyUnit unit)
        : this(value * unit.HertzAmount)
    {
    }

    public double ToMHz()
    {
        return this / MHz;
    }

    public static Frequency FromMHz(double value)
    {
        return MHz * value;
    }

    public static implicit operator Frequency(double value)
    {
        return new Frequency(value);
    }

    public static implicit operator double(Frequency property)
    {
        return property.Hertz;
    }

    public static double operator /(Frequency a, Frequency b)
    {
        return 1.0 * a.Hertz / b.Hertz;
    }

    public static Frequency operator /(Frequency a, double k)
    {
        return new Frequency(a.Hertz / k);
    }

    public static Frequency operator /(Frequency a, int k)
    {
        return new Frequency(a.Hertz / k);
    }

    public static Frequency operator *(Frequency a, double k)
    {
        return new Frequency(a.Hertz * k);
    }

    public static Frequency operator *(Frequency a, int k)
    {
        return new Frequency(a.Hertz * k);
    }

    public static Frequency operator *(double k, Frequency a)
    {
        return new Frequency(a.Hertz * k);
    }

    public static Frequency operator *(int k, Frequency a)
    {
        return new Frequency(a.Hertz * k);
    }

    public static bool TryParse(string s, FrequencyUnit unit, out Frequency freq)
    {
        var result2 = double.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out var result);
        freq = new Frequency(result, unit);
        return result2;
    }

    public static bool TryParseMHz(string s, out Frequency freq)
    {
        return TryParse(s, FrequencyUnit.MHz, out freq);
    }

    public static bool TryParseGHz(string s, out Frequency freq)
    {
        return TryParse(s, FrequencyUnit.GHz, out freq);
    }
}
