namespace Meziantou.Framework.Geo.Internals;

/// <summary>How the rings of a geometry fill the plane.</summary>
internal enum PolygonFillRule
{
    /// <summary>A position is inside when a line from it crosses the rings an odd number of times.</summary>
    EvenOdd,

    /// <summary>A position is inside when the counter-clockwise rings around it outnumber the clockwise ones: the exterior rings of oriented polygons add their area and their holes remove it.</summary>
    CounterClockwise,
}
