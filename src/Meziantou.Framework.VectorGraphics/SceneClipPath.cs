namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A clipping path made of shapes and texts (<see cref="SceneElement.ClippingPath"/>), as the <c>clipPath</c> of SVG: the
/// element is drawn only inside the union of the shapes of its content.
/// </summary>
/// <remarks>
/// <para>
/// Each element of the content adds its geometry, whatever its paints: a rectangle, a circle or a path its interior,
/// by its fill rule (<see cref="ScenePath.FillRule"/>, the <c>clip-rule</c> of SVG); a text its glyphs; a use the
/// geometry of its content; a group the geometry of its children, with its transform. A line and an image add nothing.
/// The <see cref="SceneElement.ClippingPath"/> of a shape or a text of the content cuts its geometry. The other
/// properties of the content (strokes, markers, opacities, filters, masks, the clips and the view boxes of the groups,
/// the clipping paths of the groups and of the uses) do not apply.
/// </para>
/// <para>
/// The content is in the coordinates of the parent of the clipped element, as the masks and the clips of the groups.
/// </para>
/// </remarks>
public sealed class SceneClipPath
{
    /// <summary>Gets the shapes and texts of the clipping path, in the coordinates of the parent of the clipped element.</summary>
    public SceneGroup Content { get; } = new();

    /// <summary>
    /// Gets or sets a clipping path that cuts this one, or <see langword="null"/>: the element is drawn inside both, as an
    /// SVG <c>clipPath</c> that has a <c>clip-path</c>. A clipping path that cuts itself leaves nothing visible.
    /// </summary>
    public SceneClipPath? ClippingPath { get; set; }

    /// <summary>
    /// Gets a rectangle that contains the geometry of the clipping path, cut by the clipping paths that cut it, in the
    /// coordinates of its content, or <see langword="null"/> when it leaves nothing visible.
    /// </summary>
    /// <param name="measureText">
    /// Gives the rectangle of a text before its rotation, in the coordinates of its parent, or <see langword="null"/> when
    /// it draws nothing, as for <see cref="SceneElement.GetBounds"/>.
    /// </param>
    /// <returns>The rectangle, or <see langword="null"/>.</returns>
    /// <remarks>
    /// The rectangle of a shape is its interior, without its stroke; the other properties of the content that do not
    /// change its geometry (opacities, the clips and the view boxes of the groups) do not apply.
    /// </remarks>
    public SceneBounds? GetBounds(Func<SceneText, SceneBounds?> measureText)
    {
        ArgumentNullException.ThrowIfNull(measureText);
        return SceneGeometry.GetClipBounds(this, measureText);
    }
}
