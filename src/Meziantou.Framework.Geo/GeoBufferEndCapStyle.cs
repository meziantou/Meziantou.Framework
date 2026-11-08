namespace Meziantou.Framework.Geo;

/// <summary>The shape of a buffer at the ends of a line.</summary>
public enum GeoBufferEndCapStyle
{
    /// <summary>A half circle around the end.</summary>
    Round,

    /// <summary>The buffer stops at the end, perpendicular to the line.</summary>
    Flat,

    /// <summary>The buffer goes beyond the end by the distance, and stops perpendicular to the line.</summary>
    Square,
}
