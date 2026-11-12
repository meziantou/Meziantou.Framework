namespace Meziantou.Framework.VectorGraphics;

/// <summary>
/// A drawing placed on the vertices of a path or a line, such as an arrowhead, as an SVG <c>marker</c>
/// (<see cref="ScenePath.MarkerStart"/>, <see cref="ScenePath.MarkerMid"/>, <see cref="ScenePath.MarkerEnd"/>).
/// </summary>
/// <remarks>
/// The content is drawn at each vertex: its point (<see cref="RefX"/>, <see cref="RefY"/>) on the vertex, turned as
/// <see cref="Orientation"/> says, and scaled by the width of the stroke of the shape unless <see cref="Units"/> is
/// <see cref="SceneMarkerUnits.UserSpace"/>. It is not cut. A marker can be shared by several shapes. The content of a
/// marker that is drawn on a shape of its own content is not drawn again, and its hyperlinks are not links.
/// </remarks>
public sealed class SceneMarker
{
    /// <summary>Gets the drawing of the marker, in its own coordinates.</summary>
    public SceneGroup Content { get; } = new();

    /// <summary>Gets or sets the x coordinate of the point of the content placed on the vertex.</summary>
    public double RefX { get; set; }

    /// <summary>Gets or sets the y coordinate of the point of the content placed on the vertex.</summary>
    public double RefY { get; set; }

    /// <summary>
    /// Gets or sets how the marker is turned: to the direction of the path (the default), the start marker turned back,
    /// or a fixed angle (<see cref="Angle"/>).
    /// </summary>
    public SceneMarkerOrientation Orientation { get; set; }

    /// <summary>Gets or sets the angle of the marker, in degrees, clockwise, when <see cref="Orientation"/> is <see cref="SceneMarkerOrientation.Angle"/>.</summary>
    public double Angle { get; set; }

    /// <summary>Gets or sets the units of the content. The default is <see cref="SceneMarkerUnits.StrokeWidth"/>, as in SVG.</summary>
    public SceneMarkerUnits Units { get; set; }

    /// <summary>Gets the transform that places the content of the marker on a vertex of a shape.</summary>
    /// <param name="vertex">The vertex, with the direction of the path there (<see cref="PathData.GetVertices"/>).</param>
    /// <param name="strokeWidth">The width of the stroke of the shape, which scales the content when <see cref="Units"/> is <see cref="SceneMarkerUnits.StrokeWidth"/>.</param>
    /// <param name="isStart">Whether the vertex is the first vertex of the shape, which <see cref="SceneMarkerOrientation.AutoStartReverse"/> turns back.</param>
    /// <returns>The transform from the coordinates of the content to the coordinates of the shape.</returns>
    /// <remarks>A direction or an angle that is not finite is 0.</remarks>
    public SceneTransform GetTransform(PathPoint vertex, double strokeWidth, bool isStart = false)
    {
        var angle = Orientation switch
        {
            SceneMarkerOrientation.Auto => vertex.Angle,
            SceneMarkerOrientation.AutoStartReverse => isStart ? vertex.Angle + 180 : vertex.Angle,
            _ => Angle,
        };

        var scale = Units is SceneMarkerUnits.UserSpace ? 1 : strokeWidth;
        return SceneTransform.Translate(-RefX, -RefY)
            .Then(SceneTransform.Scale(scale, scale))
            .Then(SceneTransform.Rotate(double.IsFinite(angle) ? angle : 0))
            .Then(SceneTransform.Translate(vertex.X, vertex.Y));
    }
}
