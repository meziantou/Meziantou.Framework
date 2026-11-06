namespace Meziantou.Framework.Charts;

/// <summary>The shape of the grid lines of the radial scale of polar coordinates.</summary>
public enum PolarGridShape
{
    /// <summary>Polygons when the angular scale has categories (radar charts), circles otherwise.</summary>
    Auto,

    /// <summary>Circles.</summary>
    Circle,

    /// <summary>Polygons, with a corner on each tick of the angular scale.</summary>
    Polygon,
}
