namespace Meziantou.Framework.Mermaid;

/// <summary>An axis of a radar chart: a spoke from the center, with its label at its end.</summary>
public sealed class RadarAxis
{
    private string _name;

    /// <summary>Initializes a new instance of the <see cref="RadarAxis"/> class.</summary>
    /// <param name="name">The identifier of the axis, which the curves use to give their values by axis.</param>
    /// <param name="label">The text drawn at the end of the axis, or <see langword="null"/> to draw the identifier.</param>
    public RadarAxis(string name, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        Label = label;
    }

    /// <summary>Gets or sets the identifier of the axis.</summary>
    public string Name
    {
        get => _name;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _name = value;
        }
    }

    /// <summary>Gets or sets the text drawn at the end of the axis, or <see langword="null"/> to draw <see cref="Name"/>.</summary>
    public string? Label { get; set; }
}
