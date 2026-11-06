namespace Meziantou.Framework.Charts;

/// <summary>A power scale: the position is proportional to the value raised to <see cref="Exponent"/>, keeping its sign.</summary>
public sealed class PowScale : ContinuousScale
{
    private double _exponent = 1;

    /// <summary>Gets or sets the exponent. The default is 1, a linear scale.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or not finite.</exception>
    public double Exponent
    {
        get => _exponent;
        set
        {
            if (value == 0 || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The exponent must be a finite number other than 0.");

            _exponent = value;
        }
    }
}
