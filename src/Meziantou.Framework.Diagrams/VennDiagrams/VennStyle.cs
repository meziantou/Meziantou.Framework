namespace Meziantou.Framework.Diagrams;

/// <summary>The style of an area or of a text of a <see cref="VennDiagram"/> (Mermaid <c>style</c>). A <see langword="null"/> property keeps the default.</summary>
public sealed record VennStyle
{
    /// <summary>Gets the fill: the color of a circle, or the fill of an overlap, which is otherwise transparent (CSS <c>fill</c>).</summary>
    public SceneColor? Fill { get; init; }

    /// <summary>Gets the opacity of the fill of a circle, from 0 to 1 (CSS <c>fill-opacity</c>). The default is 0.1.</summary>
    public double? FillOpacity { get; init; }

    /// <summary>Gets the color of the outline of a circle (CSS <c>stroke</c>). The default is its fill.</summary>
    public SceneColor? Stroke { get; init; }

    /// <summary>Gets the width of the outline of a circle (CSS <c>stroke-width</c>).</summary>
    public double? StrokeWidth { get; init; }

    /// <summary>Gets the color of the label of an area, or of a text (CSS <c>color</c>).</summary>
    public SceneColor? TextColor { get; init; }

    /// <summary>Gets a value indicating whether no property is set.</summary>
    public bool IsEmpty => Fill is null && FillOpacity is null && Stroke is null && StrokeWidth is null && TextColor is null;

    /// <summary>Returns this style, with the properties that another style sets replaced.</summary>
    /// <param name="other">The other style, or <see langword="null"/>.</param>
    /// <returns>The merged style.</returns>
    public VennStyle Merge(VennStyle? other)
    {
        if (other is null)
            return this;

        return new VennStyle
        {
            Fill = other.Fill ?? Fill,
            FillOpacity = other.FillOpacity ?? FillOpacity,
            Stroke = other.Stroke ?? Stroke,
            StrokeWidth = other.StrokeWidth ?? StrokeWidth,
            TextColor = other.TextColor ?? TextColor,
        };
    }
}
