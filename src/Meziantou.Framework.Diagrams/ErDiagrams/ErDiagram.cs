using Meziantou.Framework.Diagrams.Rendering;

namespace Meziantou.Framework.Diagrams;

/// <summary>
/// An entity relationship diagram: entities with their attributes, linked by relationships whose ends show their
/// cardinality in crow's foot notation, grouped in subgraphs (Mermaid <c>erDiagram</c>).
/// </summary>
public sealed class ErDiagram : Diagram
{
    /// <summary>Gets or sets the direction of the relationships. The default is <see cref="DiagramDirection.TopToBottom"/>.</summary>
    public DiagramDirection Direction { get; set; }

    /// <summary>Gets the entities, in the subgraphs or not. The entities of the subgraphs that are not in this list are drawn too.</summary>
    public IList<ErEntity> Entities { get; } = new List<ErEntity>();

    /// <summary>Gets the relationships, drawn in order.</summary>
    public IList<ErRelationship> Relationships { get; } = new List<ErRelationship>();

    /// <summary>Gets the top-level subgraphs.</summary>
    public IList<ErSubgraph> Subgraphs { get; } = new List<ErSubgraph>();

    /// <summary>Gets the styles of the CSS classes of the elements, by name (Mermaid <c>classDef</c>). The class <c>default</c> applies to every entity.</summary>
    public IDictionary<string, DiagramStyle> ClassDefinitions { get; } = new Dictionary<string, DiagramStyle>(StringComparer.Ordinal);

    /// <summary>Gets or sets the space between two entities of the same rank, in pixels.</summary>
    public double NodeSpacing { get; set; } = 140;

    /// <summary>Gets or sets the space between two ranks, in pixels.</summary>
    public double RankSpacing { get; set; } = 80;

    /// <summary>Adds an entity.</summary>
    /// <param name="id">The name of the entity, which is shown when there is no label.</param>
    /// <param name="label">The name shown instead of the identifier (Mermaid alias), or <see langword="null"/>.</param>
    /// <returns>The entity.</returns>
    public ErEntity AddEntity(string id, DiagramText? label = null)
    {
        var entity = new ErEntity(id, label);
        Entities.Add(entity);
        return entity;
    }

    /// <summary>Adds a relationship.</summary>
    /// <param name="from">The first element.</param>
    /// <param name="to">The second element.</param>
    /// <param name="fromCardinality">How many instances of <paramref name="from"/> an instance of <paramref name="to"/> relates to.</param>
    /// <param name="toCardinality">How many instances of <paramref name="to"/> an instance of <paramref name="from"/> relates to.</param>
    /// <param name="label">The text of the relationship, from the point of view of <paramref name="from"/>, or <see langword="null"/>.</param>
    /// <returns>The relationship.</returns>
    public ErRelationship AddRelationship(ErElement from, ErElement to, ErCardinality fromCardinality, ErCardinality toCardinality, DiagramText? label = null)
    {
        var relationship = new ErRelationship(from, to) { FromCardinality = fromCardinality, ToCardinality = toCardinality, Label = label };
        Relationships.Add(relationship);
        return relationship;
    }

    /// <summary>Adds a top-level subgraph.</summary>
    /// <param name="id">The identifier.</param>
    /// <param name="title">The title, or <see langword="null"/> to show the identifier.</param>
    /// <returns>The subgraph.</returns>
    public ErSubgraph AddSubgraph(string id, DiagramText? title = null)
    {
        var subgraph = new ErSubgraph(id, title);
        Subgraphs.Add(subgraph);
        return subgraph;
    }

    /// <summary>Finds an entity or a subgraph by identifier. A subgraph hides an entity of the same name.</summary>
    /// <param name="id">The identifier.</param>
    /// <returns>The element, or <see langword="null"/> when there is none.</returns>
    public ErElement? Find(string id)
    {
        ArgumentNullException.ThrowIfNull(id);
        foreach (var subgraph in EnumerateSubgraphs())
        {
            if (string.Equals(subgraph.Id, id, StringComparison.Ordinal))
                return subgraph;
        }

        foreach (var entity in Entities)
        {
            if (string.Equals(entity.Id, id, StringComparison.Ordinal))
                return entity;
        }

        foreach (var subgraph in EnumerateSubgraphs())
        {
            foreach (var entity in subgraph.Entities)
            {
                if (string.Equals(entity.Id, id, StringComparison.Ordinal))
                    return entity;
            }
        }

        return null;
    }

    /// <summary>Enumerates the subgraphs and their nested subgraphs, depth first.</summary>
    /// <returns>The subgraphs.</returns>
    public IEnumerable<ErSubgraph> EnumerateSubgraphs()
    {
        var visited = new HashSet<ErSubgraph>(ReferenceEqualityComparer.Instance);
        var stack = new Stack<ErSubgraph>(Subgraphs.Reverse());
        while (stack.Count > 0)
        {
            var subgraph = stack.Pop();
            if (!visited.Add(subgraph))
                continue;

            yield return subgraph;
            for (var i = subgraph.Subgraphs.Count - 1; i >= 0; i--)
            {
                stack.Push(subgraph.Subgraphs[i]);
            }
        }
    }

    private protected override SceneBounds Render(DiagramRenderContext context, SceneGroup group) => ErDiagramRenderer.Render(this, context, group);
}
