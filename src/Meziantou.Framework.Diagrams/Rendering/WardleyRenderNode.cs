namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A node of a Wardley map as Mermaid draws it: an anchor, a component or a component of a pipeline, merged by identifier.</summary>
internal sealed class WardleyRenderNode(string id)
{
    public string Id { get; } = id;

    public string Label { get; set; } = id;

    public double X { get; set; }

    public double Y { get; set; }

    public string ClassName { get; set; } = "component";

    public WardleyLabelOffset? LabelOffset { get; set; }

    public bool Inertia { get; set; }

    public WardleySourcing Sourcing { get; set; }

    public bool IsPipelineParent { get; set; }
}
