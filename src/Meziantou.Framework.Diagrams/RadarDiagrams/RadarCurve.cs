namespace Meziantou.Framework.Diagrams;

/// <summary>A curve of a radar chart: a value per axis, joined around the center and filled.</summary>
public sealed class RadarCurve
{
    private string _name;

    /// <summary>Initializes a new instance of the <see cref="RadarCurve"/> class.</summary>
    /// <param name="name">The identifier of the curve.</param>
    /// <param name="label">The text of the curve in the legend, or <see langword="null"/> to write the identifier.</param>
    public RadarCurve(string name, string? label = null)
    {
        ArgumentException.ThrowIfNullOrEmpty(name);
        _name = name;
        Label = label;
    }

    /// <summary>Gets or sets the identifier of the curve.</summary>
    public string Name
    {
        get => _name;
        set
        {
            ArgumentException.ThrowIfNullOrEmpty(value);
            _name = value;
        }
    }

    /// <summary>Gets or sets the text of the curve in the legend, or <see langword="null"/> to write <see cref="Name"/>.</summary>
    public string? Label { get; set; }

    /// <summary>
    /// Gets the values, in the order of the axes. As Mermaid, a curve that does not have a value for each axis is not drawn,
    /// but the legend lists it.
    /// </summary>
    public IList<double> Values { get; } = new List<double>();
}
