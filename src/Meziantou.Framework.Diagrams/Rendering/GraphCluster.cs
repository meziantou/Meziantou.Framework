using Meziantou.Framework.Diagrams.Layout;

namespace Meziantou.Framework.Diagrams.Rendering;

internal sealed class GraphCluster
{
    public GraphCluster? Parent { get; set; }

    /// <summary>Gets or sets the space taken by the title, on the top side of the cluster.</summary>
    public double TitleSpace { get; set; }

    /// <summary>Gets or sets the width of the title, with its padding.</summary>
    public double TitleWidth { get; set; }

    /// <summary>Gets or sets the size of the header of a swimlane, along the flow.</summary>
    public double HeaderSize { get; set; }

    /// <summary>Gets or sets the direction of the content, honored when no edge crosses the border of the cluster.</summary>
    public LayoutDirection? Direction { get; set; }

    /// <summary>Gets or sets the drawing of the frame and the title of the cluster, in its rectangle.</summary>
    public Action<SceneGroup, SceneBounds>? Draw { get; set; }

    public SceneBounds Bounds { get; set; }

    public bool IsDescendantOf(GraphCluster ancestor)
    {
        for (var cluster = this; cluster is not null; cluster = cluster.Parent)
        {
            if (cluster == ancestor)
                return true;
        }

        return false;
    }
}
