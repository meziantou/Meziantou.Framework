namespace Meziantou.Framework.Charts;

/// <summary>
/// A bi-symmetric log scale (Webber 2012): logarithmic for large values, linear near zero, and defined for negative
/// values and zero.
/// </summary>
public sealed class SymlogScale : ContinuousScale
{
    private double _constant = 1;

    /// <summary>Gets or sets the size of the linear region around zero. The default is 1.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a positive finite number.</exception>
    public double Constant
    {
        get => _constant;
        set
        {
            if (!(value > 0) || !double.IsFinite(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The constant must be a positive finite number.");

            _constant = value;
        }
    }
}
