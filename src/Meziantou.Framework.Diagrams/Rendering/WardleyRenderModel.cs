namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The nodes, the pipelines, the links and the movements of a Wardley map, merged as the builder of Mermaid merges them:
/// the anchors, then the components, then the components of the pipelines, by name; the coordinates in percent.
/// </summary>
internal sealed class WardleyRenderModel
{
    private WardleyRenderModel()
    {
    }

    public List<WardleyRenderNode> Nodes { get; } = [];

    public Dictionary<string, WardleyRenderNode> NodesById { get; } = new(StringComparer.Ordinal);

    public List<WardleyRenderPipeline> Pipelines { get; } = [];

    public Dictionary<string, WardleyRenderPipeline> PipelinesByParent { get; } = new(StringComparer.Ordinal);

    public List<WardleyRenderLink> Links { get; } = [];

    public List<WardleyRenderTrend> Trends { get; } = [];

    public static WardleyRenderModel Create(WardleyDiagram diagram)
    {
        var model = new WardleyRenderModel();
        foreach (var anchor in diagram.Anchors)
        {
            model.AddNode(anchor.Name, anchor.Name, anchor.Evolution * 100, anchor.Visibility * 100, "anchor", labelOffset: null, inertia: false, WardleySourcing.None);
        }

        foreach (var component in diagram.Components)
        {
            model.AddNode(component.Name, component.Name, component.Evolution * 100, component.Visibility * 100, "component", component.LabelOffset, component.Inertia, component.Sourcing);
        }

        foreach (var pipeline in diagram.Pipelines)
        {
            // Mermaid rejects a pipeline whose parent is not on the map
            if (!model.NodesById.TryGetValue(pipeline.Parent, out var parent))
                continue;

            var renderPipeline = new WardleyRenderPipeline(pipeline.Parent);
            if (model.PipelinesByParent.TryGetValue(pipeline.Parent, out var previous))
            {
                model.Pipelines[model.Pipelines.IndexOf(previous)] = renderPipeline;
            }
            else
            {
                model.Pipelines.Add(renderPipeline);
            }

            model.PipelinesByParent[pipeline.Parent] = renderPipeline;
            parent.IsPipelineParent = true;
            var parentY = parent.Y;
            foreach (var component in pipeline.Components)
            {
                var id = pipeline.Parent + "_" + component.Name;
                model.AddNode(id, component.Name, component.Evolution * 100, parentY, "pipeline-component", component.LabelOffset, inertia: false, WardleySourcing.None);
                renderPipeline.ComponentIds.Add(id);
            }
        }

        foreach (var link in diagram.Links)
        {
            model.Links.Add(new WardleyRenderLink(model.Resolve(link.Source), model.Resolve(link.Target), link.IsDashed, link.Flow, link.Label));
        }

        // The last movement of a node replaces the previous ones, at the place of the first
        var trends = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var evolve in diagram.Evolves)
        {
            if (!model.NodesById.TryGetValue(evolve.Component, out var node))
                continue;

            var trend = new WardleyRenderTrend(evolve.Component, evolve.Evolution * 100, node.Y);
            if (trends.TryGetValue(evolve.Component, out var index))
            {
                model.Trends[index] = trend;
            }
            else
            {
                trends[evolve.Component] = model.Trends.Count;
                model.Trends.Add(trend);
            }
        }

        return model;
    }

    /// <summary>Adds a node, or updates the node of the same identifier as Mermaid: its label offset is kept when the new one has none.</summary>
    private WardleyRenderNode AddNode(string id, string label, double x, double y, string className, WardleyLabelOffset? labelOffset, bool inertia, WardleySourcing sourcing)
    {
        if (!NodesById.TryGetValue(id, out var node))
        {
            node = new WardleyRenderNode(id);
            NodesById[id] = node;
            Nodes.Add(node);
        }

        node.Label = label;
        node.X = x;
        node.Y = y;
        node.ClassName = className;
        node.LabelOffset = labelOffset ?? node.LabelOffset;
        node.Inertia = inertia;
        node.Sourcing = sourcing;
        return node;
    }

    /// <summary>Finds the node of a name: the node of this identifier, or the first node of this label, such as a component of a pipeline.</summary>
    private string Resolve(string name)
    {
        if (NodesById.ContainsKey(name))
            return name;

        foreach (var node in Nodes)
        {
            if (string.Equals(node.Label, name, StringComparison.Ordinal))
                return node.Id;
        }

        return name;
    }
}
