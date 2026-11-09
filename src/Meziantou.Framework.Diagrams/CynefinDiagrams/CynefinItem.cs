namespace Meziantou.Framework.Diagrams;

/// <summary>An item of a <see cref="CynefinDiagram"/>: a text drawn as a badge in its domain.</summary>
public sealed class CynefinItem
{
    private DiagramText _label;

    /// <summary>Initializes a new instance of the <see cref="CynefinItem"/> class.</summary>
    /// <param name="domain">The domain of the item.</param>
    /// <param name="label">The text of the item.</param>
    public CynefinItem(CynefinDomain domain, DiagramText label)
    {
        ArgumentNullException.ThrowIfNull(label);
        Domain = domain;
        _label = label;
    }

    /// <summary>Gets or sets the domain of the item.</summary>
    public CynefinDomain Domain { get; set; }

    /// <summary>Gets or sets the text of the item.</summary>
    public DiagramText Label
    {
        get => _label;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _label = value;
        }
    }

    /// <summary>Returns the text of the item.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label.Text;
}
