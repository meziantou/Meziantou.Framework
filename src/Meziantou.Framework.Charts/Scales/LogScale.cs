namespace Meziantou.Framework.Charts;

/// <summary>A logarithmic scale. Values that are zero or negative are not drawn.</summary>
public sealed class LogScale : ContinuousScale
{
    private double _base = 10;

    /// <summary>Gets or sets the base of the logarithm. The default is 10.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not greater than 1.</exception>
    public double Base
    {
        get => _base;
        set
        {
            if (!(value > 1) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The base must be a finite number greater than 1.");

            _base = value;
        }
    }
}
