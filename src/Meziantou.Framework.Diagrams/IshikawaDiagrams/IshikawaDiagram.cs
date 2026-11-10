using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An Ishikawa diagram, also known as a fishbone or cause-and-effect diagram: the effect, a problem, at the head of a
/// spine, and its causes as bones on both sides of the spine, with their own causes as smaller bones (Mermaid
/// <c>ishikawa-beta</c>).
/// </summary>
public sealed class IshikawaDiagram : Diagram
{
    /// <summary>Initializes a new instance of the <see cref="IshikawaDiagram"/> class, with the padding of Mermaid, 20 pixels.</summary>
    public IshikawaDiagram()
    {
        Padding = 20;
    }

    /// <summary>Gets or sets the effect: the problem drawn in the head of the fish, or <see langword="null"/> for none.</summary>
    public DiagramText? Effect { get; set; }

    /// <summary>Gets the causes of the effect: the main bones, placed from the head to the tail, alternately above and below the spine.</summary>
    public IList<IshikawaCause> Causes { get; } = new List<IshikawaCause>();

    /// <summary>Adds a main cause of the effect.</summary>
    /// <param name="label">The text of the cause.</param>
    /// <returns>The cause.</returns>
    public IshikawaCause AddCause(DiagramText label)
    {
        var cause = new IshikawaCause(label);
        Causes.Add(cause);
        return cause;
    }

    /// <summary>Enumerates the causes and their causes, depth first.</summary>
    /// <returns>The causes.</returns>
    public IEnumerable<IshikawaCause> EnumerateCauses()
    {
        var visited = new HashSet<IshikawaCause>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<IshikawaCause>(Causes.Reverse());
        while (stack.Count > 0)
        {
            var cause = stack.Pop();
            if (!visited.Add(cause))
                continue;

            yield return cause;
            for (var i = cause.Causes.Count - 1; i >= 0; i--)
            {
                stack.Push(cause.Causes[i]);
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => IshikawaDiagramRenderer.Render(this, context, group);
}
