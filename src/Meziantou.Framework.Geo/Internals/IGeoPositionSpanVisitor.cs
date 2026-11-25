namespace Meziantou.Framework.Geo.Internals;

/// <summary>Visits the spans of positions of a geometry.</summary>
internal interface IGeoPositionSpanVisitor
{
    /// <summary>Visits a span of positions.</summary>
    /// <returns><see langword="false"/> to stop.</returns>
    bool Visit(ReadOnlySpan<GeoPosition> positions);
}
