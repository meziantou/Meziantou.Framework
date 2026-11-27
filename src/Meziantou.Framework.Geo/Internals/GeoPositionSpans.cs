using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Gives the positions of geometries as spans, in the order of <see cref="GeoGeometry.GetPositions"/>, without the enumerators of LINQ.</summary>
internal static class GeoPositionSpans
{
    /// <summary>Gives each span of positions of a geometry to a visitor, until it asks to stop.</summary>
    /// <returns><see langword="false"/> when the visitor asked to stop.</returns>
    public static bool Visit<TVisitor>(GeoGeometry geometry, ref TVisitor visitor)
        where TVisitor : struct, IGeoPositionSpanVisitor
    {
        switch (geometry)
        {
            case GeoPoint point:
                var position = point.Position;
                return visitor.Visit(new ReadOnlySpan<GeoPosition>(in position));

            case GeoLineString lineString:
                return visitor.Visit(lineString.PositionArray);

            case GeoPolygon polygon:
                foreach (var ring in polygon.RingArrays)
                {
                    if (!visitor.Visit(ring))
                        return false;
                }

                return true;

            case GeoMultiPoint multiPoint:
                return visitor.Visit(multiPoint.PositionArray);

            case GeoMultiLineString multiLineString:
                foreach (var lineString in multiLineString.LineStringArray)
                {
                    if (!visitor.Visit(lineString.PositionArray))
                        return false;
                }

                return true;

            case GeoMultiPolygon multiPolygon:
                foreach (var polygon in multiPolygon.PolygonArray)
                {
                    if (!Visit(polygon, ref visitor))
                        return false;
                }

                return true;

            case GeoGeometryCollection collection:
                foreach (var part in collection.GeometryArray)
                {
                    if (!Visit(part, ref visitor))
                        return false;
                }

                return true;

            default:
                foreach (var item in geometry.GetPositions())
                {
                    var copy = item;
                    if (!visitor.Visit(new ReadOnlySpan<GeoPosition>(in copy)))
                        return false;
                }

                return true;
        }
    }

    /// <summary>Gets the smallest box that contains the positions of a geometry, as <see cref="GeoBounds.FromPositions"/> computes it.</summary>
    public static GeoBounds? GetBounds(GeoGeometry geometry)
    {
        var visitor = new BoundsVisitor();
        Visit(geometry, ref visitor);
        return visitor.GetBounds();
    }

    /// <summary>Gets the smallest box that contains the positions of the geometries of features, as <see cref="GeoBounds.FromPositions"/> computes it.</summary>
    public static GeoBounds? GetBounds(IEnumerable<GeoFeature> features)
    {
        var visitor = new BoundsVisitor();
        foreach (var feature in features)
        {
            if (feature.Geometry is not null)
            {
                Visit(feature.Geometry, ref visitor);
            }
        }

        return visitor.GetBounds();
    }

    /// <summary>Gets a value that indicates whether a geometry has at least a position.</summary>
    public static bool HasPositions(GeoGeometry geometry)
    {
        var visitor = new NonEmptyVisitor();
        return !Visit(geometry, ref visitor);
    }

    /// <summary>Gets a value that indicates whether a position of a geometry has an altitude, and whether one has a measure.</summary>
    public static (bool HasAltitude, bool HasMeasure) GetDimensions(GeoGeometry geometry)
    {
        var visitor = new DimensionsVisitor();
        Visit(geometry, ref visitor);
        return (visitor.HasAltitude, visitor.HasMeasure);
    }

    /// <summary>Gets the smallest box that contains positions; the same comparisons as <see cref="GeoBounds.FromPositions"/>, in the same order.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct BoundsVisitor() : IGeoPositionSpanVisitor
    {
        private double _west = double.PositiveInfinity;
        private double _south = double.PositiveInfinity;
        private double _east = double.NegativeInfinity;
        private double _north = double.NegativeInfinity;

        public bool Visit(ReadOnlySpan<GeoPosition> positions)
        {
            var (west, south, east, north) = (_west, _south, _east, _north);
            foreach (ref readonly var position in positions)
            {
                west = Math.Min(west, position.Longitude);
                south = Math.Min(south, position.Latitude);
                east = Math.Max(east, position.Longitude);
                north = Math.Max(north, position.Latitude);
            }

            (_west, _south, _east, _north) = (west, south, east, north);
            return true;
        }

        public readonly GeoBounds? GetBounds() => _west > _east ? null : new GeoBounds(_west, _south, _east, _north);
    }

    private readonly struct NonEmptyVisitor : IGeoPositionSpanVisitor
    {
        public bool Visit(ReadOnlySpan<GeoPosition> positions) => positions.IsEmpty;
    }

    [StructLayout(LayoutKind.Auto)]
    private struct DimensionsVisitor : IGeoPositionSpanVisitor
    {
        public bool HasAltitude { get; private set; }

        public bool HasMeasure { get; private set; }

        public bool Visit(ReadOnlySpan<GeoPosition> positions)
        {
            foreach (ref readonly var position in positions)
            {
                HasAltitude |= position.Altitude.HasValue;
                HasMeasure |= position.Measure.HasValue;
            }

            return !(HasAltitude && HasMeasure);
        }
    }
}
