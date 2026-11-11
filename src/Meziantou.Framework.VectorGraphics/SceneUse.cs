namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// An element that draws another element again, as an SVG <c>use</c>: the SVG writes the element once and refers to it,
/// the other renderers draw it again.
/// </summary>
/// <param name="content">
/// The element to draw: an element of the scene, or an element that is only drawn by uses. It is drawn as a child of the
/// parent of the use, with its own transform and effects, then the transform and the effects of the use.
/// </param>
/// <remarks>
/// The SVG writes the element where it is in the scene, or in its definitions when it is not in the scene, with an
/// identifier (its <see cref="SceneElement.Id"/>, or a generated one). A use that is in the content it draws, directly or
/// through other uses, draws nothing (<see cref="IsCircular"/>), as in SVG.
/// </remarks>
/// <exception cref="ArgumentNullException"><paramref name="content"/> is <see langword="null"/>.</exception>
public sealed class SceneUse(SceneElement content) : SceneElement
{
    /// <summary>Gets the element the use draws.</summary>
    public SceneElement Content { get; } = content ?? throw new ArgumentNullException(nameof(content));

    /// <summary>Indicates whether the use is in the content it draws, directly or through other uses: it then draws nothing, as in SVG.</summary>
    /// <returns><see langword="true"/> when the use is in its content.</returns>
    /// <remarks>The content of the masks, the patterns and the markers of the elements counts.</remarks>
    public bool IsCircular()
    {
        var visited = new HashSet<SceneElement>(ReferenceEqualityComparer.Instance);
        var pending = new Stack<SceneElement>();
        pending.Push(Content);
        while (pending.TryPop(out var element))
        {
            if (ReferenceEquals(element, this))
                return true;

            if (!visited.Add(element))
                continue;

            foreach (var drawn in SceneTree.GetContents(element))
            {
                pending.Push(drawn);
            }

            if (element is SceneUse use)
            {
                pending.Push(use.Content);
            }
        }

        return false;
    }
}
