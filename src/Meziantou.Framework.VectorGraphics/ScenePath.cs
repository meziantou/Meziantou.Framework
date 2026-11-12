namespace Meziantou.Framework.VectorGraphics;

/// <summary>A path of lines, curves and arcs.</summary>
public sealed class ScenePath : SceneShape
{
    /// <summary>Initializes a new instance of the <see cref="ScenePath"/> class.</summary>
    /// <param name="data">The geometry of the path.</param>
    public ScenePath(PathData data)
    {
        ArgumentNullException.ThrowIfNull(data);
        Data = data;
    }

    /// <summary>Gets the geometry of the path.</summary>
    public PathData Data { get; }

    /// <summary>Gets or sets the fill rule.</summary>
    public SceneFillRule FillRule { get; set; }

    /// <summary>Gets or sets the marker drawn on the first vertex of the path, or <see langword="null"/> for none.</summary>
    public SceneMarker? MarkerStart { get; set; }

    /// <summary>Gets or sets the marker drawn on the vertices of the path between its first and its last, or <see langword="null"/> for none.</summary>
    public SceneMarker? MarkerMid { get; set; }

    /// <summary>Gets or sets the marker drawn on the last vertex of the path, or <see langword="null"/> for none.</summary>
    public SceneMarker? MarkerEnd { get; set; }
}
