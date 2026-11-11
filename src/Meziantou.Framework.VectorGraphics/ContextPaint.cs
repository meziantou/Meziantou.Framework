namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// The paint of the shape a marker is drawn on, as the SVG 2 <c>context-fill</c> and <c>context-stroke</c> values: an
/// arrowhead of the color of the line it ends.
/// </summary>
/// <remarks>
/// In the content of a <see cref="SceneMarker"/>, <see cref="Fill"/> paints with the fill of the path or the line the
/// marker is drawn on, and <see cref="Stroke"/> with its stroke: one marker serves lines of every color. Elsewhere
/// (outside of the markers, or in the content of a pattern), it paints nothing.
/// </remarks>
public sealed class ContextPaint : ScenePaint
{
    private ContextPaint(bool isStroke)
    {
        IsStroke = isStroke;
    }

    /// <summary>Gets the paint of the fill of the shape the marker is drawn on (<c>context-fill</c>).</summary>
    public static ContextPaint Fill { get; } = new(isStroke: false);

    /// <summary>Gets the paint of the stroke of the shape the marker is drawn on (<c>context-stroke</c>).</summary>
    public static ContextPaint Stroke { get; } = new(isStroke: true);

    /// <summary>Gets a value that indicates whether the paint is the stroke of the shape, rather than its fill.</summary>
    public bool IsStroke { get; }
}
