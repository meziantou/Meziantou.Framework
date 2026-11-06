namespace Meziantou.Framework.Charts;

/// <summary>Draws horizontal reference lines at y values, across the plot area, such as a baseline at zero or a threshold.</summary>
public sealed class RuleYMark : RuleYMark<double>
{
    /// <summary>Initializes a new instance of the <see cref="RuleYMark"/> class.</summary>
    /// <param name="values">The y values of the lines.</param>
    public RuleYMark(params double[] values)
        : base(values, value => value)
    {
    }
}
