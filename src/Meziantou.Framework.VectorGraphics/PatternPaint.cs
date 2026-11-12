namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A pattern: a tile of drawing repeated side by side in both directions, as an SVG <c>pattern</c> whose units are
/// the coordinates of the shape it paints (<c>patternUnits="userSpaceOnUse"</c>).
/// </summary>
/// <param name="tile">
/// The rectangle that repeats, in the coordinates of the pattern: the tile at (0, 0) of 10 by 10 is drawn again at
/// (10, 0), (0, 10) and so on. What <see cref="Content"/> draws outside of it is cut. A tile without width or height
/// paints nothing.
/// </param>
/// <remarks>
/// The content of a pattern that paints with the pattern itself, directly or not, is not drawn. The hyperlinks of the
/// content are not links.
/// </remarks>
public sealed class PatternPaint(SceneBounds tile) : ScenePaint
{
    /// <summary>Gets the rectangle that repeats, in the coordinates of the pattern.</summary>
    public SceneBounds Tile { get; } = tile;

    /// <summary>Gets the drawing of the tile, in the coordinates of the pattern.</summary>
    public SceneGroup Content { get; } = new();

    /// <summary>
    /// Gets the transform from the coordinates of the pattern to the coordinates of the shape it paints, as the
    /// <c>patternTransform</c> of SVG, or <see langword="null"/> for none: it rotates, skews or scales the pattern.
    /// </summary>
    public SceneTransform? Transform { get; init; }
}
