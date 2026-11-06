namespace Meziantou.Framework.Charts;

/// <summary>A group of elements, with a transform, a clip rectangle and an opacity that apply to all of them.</summary>
public sealed class SceneGroup : SceneElement
{
    /// <summary>Gets the elements of the group, drawn in order.</summary>
    public IList<SceneElement> Children { get; } = new List<SceneElement>();

    /// <summary>Gets or sets the transform of the elements, or <see langword="null"/> for none.</summary>
    public SceneTransform? Transform { get; set; }

    /// <summary>Gets or sets the rectangle outside of which nothing is drawn, in the coordinates of the parent of the group, or <see langword="null"/> for none.</summary>
    public ChartRectangle? Clip { get; set; }

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
