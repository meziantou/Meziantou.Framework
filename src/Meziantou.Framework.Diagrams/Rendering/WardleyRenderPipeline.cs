namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A pipeline of a Wardley map as Mermaid draws it: the identifiers of its components.</summary>
internal sealed class WardleyRenderPipeline(string parentId)
{
    public string ParentId { get; } = parentId;

    public List<string> ComponentIds { get; } = [];
}
