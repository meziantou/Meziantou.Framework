namespace Meziantou.Framework.VectorGraphics;

/// <summary>A group of elements, with a clip rectangle or path, and the transform, the opacity and the effects of an element, that apply to all of them.</summary>
public sealed class SceneGroup : SceneElement
{
    /// <summary>Gets the elements of the group, drawn in order.</summary>
    public IList<SceneElement> Children { get; } = new List<SceneElement>();

    /// <summary>Gets or sets the rectangle outside of which nothing is drawn, in the coordinates of the parent of the group, or <see langword="null"/> for none.</summary>
    public SceneBounds? Clip { get; set; }

    /// <summary>
    /// Gets or sets the path outside of which nothing is drawn, in the coordinates of the parent of the group, or
    /// <see langword="null"/> for none. With <see cref="Clip"/>, only what is inside both is drawn.
    /// </summary>
    public PathData? ClipPath { get; set; }

    /// <summary>Gets or sets the rule that decides which points are inside <see cref="ClipPath"/>.</summary>
    public SceneFillRule ClipRule { get; set; }

    /// <summary>
    /// Gets or sets the view box of the children, as a nested SVG <c>svg</c> element, or <see langword="null"/> for none:
    /// a rectangle of the coordinates of the children is mapped to a viewport of the coordinates of the group, which cuts
    /// what is outside of it.
    /// </summary>
    /// <remarks>
    /// It applies inside the <see cref="SceneElement.Transform"/> of the group; <see cref="Clip"/> and <see cref="ClipPath"/>
    /// stay in the coordinates of the parent. A view box whose box or viewport has no area draws nothing.
    /// </remarks>
    public SceneViewBox? ViewBox { get; set; }

    /// <summary>Adds an element to the group.</summary>
    /// <param name="element">The element.</param>
    /// <returns>The element.</returns>
    public T Add<T>(T element)
        where T : SceneElement
    {
        Children.Add(element);
        return element;
    }
}
