namespace Meziantou.Framework.Diagrams;

/// <summary>A point of a <see cref="QuadrantChartDiagram"/>: a circle at a position from 0 to 1 on each axis, with a text below it.</summary>
public sealed class QuadrantChartPoint
{
    private DiagramText _label;
    private double _x;
    private double _y;

    /// <summary>Initializes a new instance of the <see cref="QuadrantChartPoint"/> class.</summary>
    /// <param name="label">The text drawn below the point.</param>
    /// <param name="x">The position on the x axis, from 0 (left) to 1 (right).</param>
    /// <param name="y">The position on the y axis, from 0 (bottom) to 1 (top).</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="x"/> or <paramref name="y"/> is not between 0 and 1.</exception>
    public QuadrantChartPoint(DiagramText label, double x, double y)
    {
        ArgumentNullException.ThrowIfNull(label);
        _label = label;
        _x = CheckCoordinate(x, nameof(x));
        _y = CheckCoordinate(y, nameof(y));
    }

    /// <summary>Gets or sets the text drawn below the point.</summary>
    public DiagramText Label
    {
        get => _label;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _label = value;
        }
    }

    /// <summary>Gets or sets the position on the x axis, from 0 (left) to 1 (right).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double X { get => _x; set => _x = CheckCoordinate(value, nameof(value)); }

    /// <summary>Gets or sets the position on the y axis, from 0 (bottom) to 1 (top).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Y { get => _y; set => _y = CheckCoordinate(value, nameof(value)); }

    /// <summary>Gets or sets the name of the class whose style applies to the point (Mermaid <c>:::class</c>), or <see langword="null"/> for none.</summary>
    public string? ClassName { get; set; }

    /// <summary>Gets the style of the point. Its values take precedence over the ones of the class.</summary>
    public QuadrantChartPointStyle Style { get; } = new();

    /// <summary>Returns the text of the point.</summary>
    /// <returns>The text.</returns>
    public override string ToString() => Label.Text;

    private static double CheckCoordinate(double value, string name)
    {
        if (!(value >= 0 && value <= 1))
            throw new ArgumentOutOfRangeException(name, value, "The position must be between 0 and 1.");

        return value;
    }
}
