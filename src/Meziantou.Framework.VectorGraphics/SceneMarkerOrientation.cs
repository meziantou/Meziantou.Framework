namespace Meziantou.Framework.VectorGraphics;

/// <summary>How a marker is turned (<see cref="SceneMarker.Orientation"/>).</summary>
public enum SceneMarkerOrientation
{
    /// <summary>To the direction of the path at the vertex, as SVG <c>orient="auto"</c>: the x axis of the marker follows the path.</summary>
    Auto,

    /// <summary>As <see cref="Auto"/>, but the marker at the start of the path is turned back, as SVG <c>orient="auto-start-reverse"</c>: an arrowhead points out of both ends.</summary>
    AutoStartReverse,

    /// <summary>By a fixed angle (<see cref="SceneMarker.Angle"/>).</summary>
    Angle,
}
