namespace Meziantou.Framework.Diagrams;

/// <summary>A terminal of a railroad diagram: a literal text, drawn in a rounded box.</summary>
public sealed class RailroadTerminal : RailroadExpression
{
    private string _value;

    /// <summary>Initializes a new instance of the <see cref="RailroadTerminal"/> class.</summary>
    /// <param name="value">The text.</param>
    public RailroadTerminal(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        _value = value;
    }

    /// <summary>Gets or sets the text.</summary>
    public string Value
    {
        get => _value;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _value = value;
        }
    }
}
