namespace Meziantou.Framework.Charts;

/// <summary>Draws vertical reference lines at x values, across the plot area.</summary>
public sealed class RuleXMark : RuleXMark<double>
{
    /// <summary>Initializes a new instance of the <see cref="RuleXMark"/> class.</summary>
    /// <param name="values">The x values of the lines.</param>
    public RuleXMark(params double[] values)
        : base(values, value => value)
    {
    }
}
