namespace Meziantou.Framework.Diagrams;

/// <summary>A link of a sankey diagram: a flow of a quantity from a source node to a target node.</summary>
public sealed class SankeyLink
{
    private string _source;
    private string _target;
    private double _value;

    /// <summary>Initializes a new instance of the <see cref="SankeyLink"/> class.</summary>
    /// <param name="source">The name of the node the flow comes from.</param>
    /// <param name="target">The name of the node the flow goes to.</param>
    /// <param name="value">The quantity, which gives the width of the link.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="value"/> is negative, not finite or larger than 1E+300.</exception>
    public SankeyLink(string source, string target, double value)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        _source = source;
        _target = target;
        _value = ValidateValue(value);
    }

    /// <summary>Gets or sets the name of the node the flow comes from.</summary>
    public string Source
    {
        get => _source;
        set => _source = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the name of the node the flow goes to.</summary>
    public string Target
    {
        get => _target;
        set => _target = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the quantity, which gives the width of the link.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative, not finite or larger than 1E+300.</exception>
    public double Value
    {
        get => _value;
        set => _value = ValidateValue(value);
    }

    private static double ValidateValue(double value)
    {
        if (!(value >= 0) || value > 1e300)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be a positive number, at most 1E+300.");

        return value;
    }
}
