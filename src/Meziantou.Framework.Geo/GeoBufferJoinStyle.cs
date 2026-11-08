namespace Meziantou.Framework.Geo;

/// <summary>The shape of a buffer around a vertex where a line turns, on the outer side of the turn.</summary>
public enum GeoBufferJoinStyle
{
    /// <summary>An arc of a circle around the vertex.</summary>
    Round,

    /// <summary>The two sides are extended until they meet, or cut as by <see cref="Bevel"/> when they meet further than <see cref="GeoBufferOptions.MiterLimit"/> times the distance.</summary>
    Miter,

    /// <summary>The two sides are joined by a straight line.</summary>
    Bevel,
}
