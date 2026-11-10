using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>A graph to lay out and draw: the common model of the flowcharts, class, state and entity relationship diagrams.</summary>
internal sealed class GraphModel
{
    public LayoutDirection Direction { get; set; }

    public double NodeSeparation { get; set; } = 50;

    public double RankSeparation { get; set; } = 50;

    public double EdgeSeparation { get; set; } = 20;

    public double ClusterPadding { get; set; } = 8;

    public List<GraphNode> Nodes { get; } = [];

    public List<GraphEdge> Edges { get; } = [];

    public List<GraphCluster> Clusters { get; } = [];

    public GraphNode AddNode(double width, double height, GraphCluster? cluster = null)
    {
        var node = new GraphNode { Width = width, Height = height, Cluster = cluster };
        Nodes.Add(node);
        return node;
    }
}
