namespace Meziantou.Framework.Geo;

/// <summary>A set of lines, such as the segments of a GPS track.</summary>
public sealed class GeoMultiLineString : GeoGeometry
{
    /// <summary>Initializes a new instance of the <see cref="GeoMultiLineString"/> class.</summary>
    /// <param name="lineStrings">The lines.</param>
    public GeoMultiLineString(IEnumerable<GeoLineString> lineStrings)
    {
        LineStringArray = ToArray(lineStrings, nameof(lineStrings));
    }

    /// <summary>Gets the lines.</summary>
    public IReadOnlyList<GeoLineString> LineStrings => LineStringArray;

    /// <summary>Gets the array that <see cref="LineStrings"/> returns, so that the operations read it without interface calls.</summary>
    internal GeoLineString[] LineStringArray { get; }

    /// <inheritdoc />
    public override GeoGeometryType Type => GeoGeometryType.MultiLineString;

    /// <inheritdoc />
    public override IEnumerable<GeoPosition> GetPositions() => LineStrings.SelectMany(lineString => lineString.Positions);
}
