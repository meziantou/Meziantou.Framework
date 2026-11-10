namespace Meziantou.Framework.Diagrams;

/// <summary>
/// The look of an element of a diagram, the CSS properties of the <c>style</c> and <c>classDef</c> statements of
/// Mermaid. A property that is <see langword="null"/> keeps the value of the theme.
/// </summary>
public sealed record DiagramStyle
{
    /// <summary>Gets the color of the interior (CSS <c>fill</c>).</summary>
    public SceneColor? Fill { get; init; }

    /// <summary>Gets the color of the outline, or of the line of an edge (CSS <c>stroke</c>).</summary>
    public SceneColor? Stroke { get; init; }

    /// <summary>Gets the width of the outline, or of the line of an edge (CSS <c>stroke-width</c>).</summary>
    public double? StrokeWidth { get; init; }

    /// <summary>Gets the lengths of the dashes and gaps of the outline, or an empty list for a solid outline (CSS <c>stroke-dasharray</c>).</summary>
    public IReadOnlyList<double>? StrokeDashArray { get; init; }

    /// <summary>Gets the color of the text (CSS <c>color</c>).</summary>
    public SceneColor? TextColor { get; init; }

    /// <summary>Gets a value that indicates whether the text is bold (CSS <c>font-weight</c>).</summary>
    public bool? Bold { get; init; }

    /// <summary>Gets a value that indicates whether the text is italic (CSS <c>font-style</c>).</summary>
    public bool? Italic { get; init; }

    /// <summary>Gets the size of the text, in pixels (CSS <c>font-size</c>).</summary>
    public double? FontSize { get; init; }

    /// <summary>Gets the opacity of the element, from 0 to 1 (CSS <c>opacity</c>).</summary>
    public double? Opacity { get; init; }

    /// <summary>Returns a style whose properties are the ones of <paramref name="other"/> when they are set, and the ones of this style otherwise.</summary>
    /// <param name="other">The style that takes precedence, or <see langword="null"/>.</param>
    /// <returns>The combined style.</returns>
    public DiagramStyle Merge(DiagramStyle? other)
    {
        if (other is null)
            return this;

        return new DiagramStyle
        {
            Fill = other.Fill ?? Fill,
            Stroke = other.Stroke ?? Stroke,
            StrokeWidth = other.StrokeWidth ?? StrokeWidth,
            StrokeDashArray = other.StrokeDashArray ?? StrokeDashArray,
            TextColor = other.TextColor ?? TextColor,
            Bold = other.Bold ?? Bold,
            Italic = other.Italic ?? Italic,
            FontSize = other.FontSize ?? FontSize,
            Opacity = other.Opacity ?? Opacity,
        };
    }
}
