namespace Meziantou.Framework.Mermaid;

/// <summary>The style of a point of a <see cref="QuadrantChart"/>, or of a class of points. A <see langword="null"/> value keeps the default.</summary>
public sealed class QuadrantChartPointStyle
{
    private int? _radius;
    private int? _strokeWidth;

    /// <summary>Gets or sets the radius of the point, in pixels (Mermaid <c>radius</c>).</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 1000000.</exception>
    public int? Radius
    {
        get => _radius;
        set => _radius = Check(value);
    }

    /// <summary>Gets or sets the fill color of the point (Mermaid <c>color</c>).</summary>
    public SceneColor? Color { get; set; }

    /// <summary>Gets or sets the color of the border of the point (Mermaid <c>stroke-color</c>).</summary>
    public SceneColor? StrokeColor { get; set; }

    /// <summary>Gets or sets the width of the border of the point, in pixels (Mermaid <c>stroke-width</c>). The default is 0: no border.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative or greater than 1000000.</exception>
    public int? StrokeWidth
    {
        get => _strokeWidth;
        set => _strokeWidth = Check(value);
    }

    /// <summary>Gets a value that indicates whether no value of the style is set.</summary>
    public bool IsEmpty => Radius is null && Color is null && StrokeColor is null && StrokeWidth is null;

    private static int? Check(int? value)
    {
        if (value is < 0 or > 1_000_000)
            throw new ArgumentOutOfRangeException(nameof(value), value, "The value must be between 0 and 1000000.");

        return value;
    }
}
