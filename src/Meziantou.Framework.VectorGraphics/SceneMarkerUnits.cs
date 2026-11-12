namespace Meziantou.Framework.VectorGraphics;

/// <summary>The units of the content of a marker (<see cref="SceneMarker.Units"/>).</summary>
public enum SceneMarkerUnits
{
    /// <summary>The content is scaled by the width of the stroke of the shape, as SVG <c>markerUnits="strokeWidth"</c>.</summary>
    StrokeWidth,

    /// <summary>The content is in the coordinates of the shape, as SVG <c>markerUnits="userSpaceOnUse"</c>.</summary>
    UserSpace,
}
