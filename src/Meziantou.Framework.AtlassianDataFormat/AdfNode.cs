namespace Meziantou.Framework.AtlassianDataFormat;

/// <summary>Represents a node of an Atlassian Document Format document.</summary>
public abstract class AdfNode
{
    /// <summary>Gets the type of the node.</summary>
    public abstract AdfNodeKind Kind { get; }

    /// <summary>Gets the identifier of the node inside the document, when the document defines one.</summary>
    public string? LocalId { get; init; }

    /// <summary>Gets the child nodes. The list is empty for leaf nodes.</summary>
    public IReadOnlyList<AdfNode> Content { get; init; } = [];

    /// <summary>Gets the marks applied to the node. The list is empty when the node carries no mark.</summary>
    public IReadOnlyList<AdfMark> Marks { get; init; } = [];

    /// <summary>Gets the first mark of the specified type, or <see langword="null"/> when the node does not carry one.</summary>
    /// <typeparam name="T">The type of the mark to find.</typeparam>
    public T? GetMark<T>()
        where T : AdfMark
    {
        foreach (var mark in Marks)
        {
            if (mark is T result)
                return result;
        }

        return null;
    }

    /// <summary>Enumerates this node and all its descendants, depth first.</summary>
    public IEnumerable<AdfNode> DescendantsAndSelf()
    {
        yield return this;
        foreach (var child in Content)
        {
            foreach (var descendant in child.DescendantsAndSelf())
            {
                yield return descendant;
            }
        }
    }
}
