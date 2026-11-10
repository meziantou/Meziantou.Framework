namespace Meziantou.Framework.Mermaid;

/// <summary>A slice of a pie chart: a label and a value, whose share of the total sets the angle of the slice.</summary>
public sealed class PieSlice
{
    private string _label;
    private double _value;

    /// <summary>Initializes a new instance of the <see cref="PieSlice"/> class.</summary>
    /// <param name="label">The label of the slice, written in the legend.</param>
    /// <param name="value">The value of the slice, positive or zero.</param>
    public PieSlice(string label, double value)
    {
        ArgumentNullException.ThrowIfNull(label);
        ThrowIfInvalidValue(value);
        _label = label;
        _value = value;
    }

    /// <summary>Gets or sets the label of the slice, written in the legend.</summary>
    public string Label
    {
        get => _label;
        set => _label = value ?? throw new ArgumentNullException(nameof(value));
    }

    /// <summary>Gets or sets the value of the slice, positive or zero.</summary>
    public double Value
    {
        get => _value;
        set
        {
            ThrowIfInvalidValue(value);
            _value = value;
        }
    }

    private static void ThrowIfInvalidValue(double value)
    {
        if (!(value >= 0) || !double.IsFinite(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value of a slice must be a positive or zero finite number.");
    }
}
