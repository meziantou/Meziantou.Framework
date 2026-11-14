namespace Meziantou.AspNetCore.Testing.Fuzz.Components;

/// <summary>A node with children: the root, an element, a child component or a region.</summary>
internal class ModelContainer(bool inSvg) : ModelNode
{
    public List<ModelNode> Children { get; } = [];

    /// <summary>Gets whether the children are in an <c>svg</c> element (only SVG elements are created there).</summary>
    public bool InSvg { get; } = inSvg;

    public void CollectContainers(List<ModelContainer> containers)
    {
        containers.Add(this);
        foreach (var child in Children)
        {
            if (child is ModelContainer container)
            {
                container.CollectContainers(containers);
            }
        }
    }
}
