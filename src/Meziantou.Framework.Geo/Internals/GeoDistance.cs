using System.Runtime.InteropServices;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>The geodesic distance between two geometries.</summary>
/// <remarks>
/// The edges are straight lines in degrees, as for the other operations. The shortest distance between two geometries that
/// do not meet is between a vertex of one and a point of an edge (or a point) of the other. The pairs are compared on a
/// sphere first, then the nearest ones are measured on the WGS 84 ellipsoid.
/// </remarks>
internal static class GeoDistance
{
    /// <summary>The largest ratio between a distance on the ellipsoid and the distance on the sphere, with a margin for the nearest position of an edge that the first pass estimates.</summary>
    private const double CandidateRatio = 1.02;

    private const double DegreesToRadians = Math.PI / 180;

    public static double? GetDistance(GeoGeometry first, GeoGeometry second)
    {
        var a = new Parts(first);
        var b = new Parts(second);
        if (a.Vertices.Count == 0 || b.Vertices.Count == 0)
            return null;

        if (Intersects(a, b))
            return 0;

        var best = double.PositiveInfinity;
        var candidates = new List<Candidate>();
        AddCandidates(a, b, candidates, ref best);
        AddCandidates(b, a, candidates, ref best);

        var result = double.PositiveInfinity;
        foreach (var candidate in candidates)
        {
            if (candidate.Estimate > (best * CandidateRatio) + 1e-3)
                continue;

            var target = candidate.Segment is { } segment ? Refine(candidate.Vertex, segment, candidate.T) : candidate.Target;
            result = Math.Min(result, Geodesic.GetDistance(candidate.Vertex, target));
        }

        return result;
    }

    /// <summary>Compares the vertices of a geometry to the points and edges of another one.</summary>
    private static void AddCandidates(Parts from, Parts to, List<Candidate> candidates, ref double best)
    {
        foreach (var vertex in from.Vertices)
        {
            foreach (var point in to.Points)
            {
                if (LatitudeGap(vertex.Latitude, point.Latitude, point.Latitude) > best * CandidateRatio)
                    continue;

                var estimate = vertex.GetDistance(point);
                Add(new Candidate(vertex, point, Segment: null, T: 0, estimate), candidates, ref best);
            }

            foreach (var segment in to.Segments)
            {
                if (LatitudeGap(vertex.Latitude, segment.MinLatitude, segment.MaxLatitude) > best * CandidateRatio)
                    continue;

                var t = EstimateNearest(vertex, segment);
                var estimate = vertex.GetDistance(segment.At(t));
                Add(new Candidate(vertex, default, segment, t, estimate), candidates, ref best);
            }
        }

        static void Add(Candidate candidate, List<Candidate> candidates, ref double best)
        {
            if (candidate.Estimate > best * CandidateRatio)
                return;

            best = Math.Min(best, candidate.Estimate);
            candidates.Add(candidate);
        }
    }

    /// <summary>A distance, on the sphere, that is not more than the distance between a position and any position between two latitudes.</summary>
    private static double LatitudeGap(double latitude, double minLatitude, double maxLatitude)
    {
        var gap = latitude < minLatitude ? minLatitude - latitude : latitude > maxLatitude ? latitude - maxLatitude : 0;
        return gap * DegreesToRadians * GeoPosition.EarthRadius;
    }

    /// <summary>Estimates the nearest position of an edge on an equirectangular map whose scale is right at the latitude of the position.</summary>
    private static double EstimateNearest(GeoPosition vertex, Segment segment)
    {
        var scale = Math.Cos(vertex.Latitude * DegreesToRadians);
        var dx = (segment.End.Longitude - segment.Start.Longitude) * scale;
        var dy = segment.End.Latitude - segment.Start.Latitude;
        var lengthSquared = (dx * dx) + (dy * dy);
        if (lengthSquared == 0)
            return 0;

        var t = ((((vertex.Longitude - segment.Start.Longitude) * scale) * dx) + ((vertex.Latitude - segment.Start.Latitude) * dy)) / lengthSquared;
        return Math.Clamp(t, 0, 1);
    }

    /// <summary>
    /// Finds the nearest position of an edge: a golden-section search on the distance on the sphere over the whole edge, then
    /// on the distance on the ellipsoid around the position it finds. The estimate and the ends are kept when they are nearer.
    /// </summary>
    private static GeoPosition Refine(GeoPosition vertex, Segment segment, double estimate)
    {
        var spherical = Minimize(t => vertex.GetDistance(segment.At(t)), 0, 1);
        var t = Minimize(t => Geodesic.GetDistance(vertex, segment.At(t)), Math.Max(0, spherical - 0.1), Math.Min(1, spherical + 0.1));
        var best = t;
        var bestDistance = Geodesic.GetDistance(vertex, segment.At(t));
        foreach (var value in (ReadOnlySpan<double>)[estimate, spherical, 0, 1])
        {
            var distance = Geodesic.GetDistance(vertex, segment.At(value));
            if (distance < bestDistance)
            {
                bestDistance = distance;
                best = value;
            }
        }

        return segment.At(best);
    }

    /// <summary>Finds the minimum of a function that decreases then increases, with a golden-section search.</summary>
    private static double Minimize(Func<double, double> function, double low, double high)
    {
        var invPhi = (Math.Sqrt(5) - 1) / 2;
        var c = high - (invPhi * (high - low));
        var d = low + (invPhi * (high - low));
        var fc = function(c);
        var fd = function(d);
        for (var i = 0; i < 100 && high - low > 1e-14; i++)
        {
            if (fc < fd)
            {
                high = d;
                d = c;
                fd = fc;
                c = high - (invPhi * (high - low));
                fc = function(c);
            }
            else
            {
                low = c;
                c = d;
                fc = fd;
                d = low + (invPhi * (high - low));
                fd = function(d);
            }
        }

        return (low + high) / 2;
    }

    /// <summary>Gets a value indicating whether two geometries meet: an edge of one crosses or touches an edge of the other, or a part of one is inside a polygon of the other.</summary>
    private static bool Intersects(Parts a, Parts b)
    {
        foreach (var first in a.Segments)
        {
            foreach (var second in b.Segments)
            {
                if (first.MinLongitude <= second.MaxLongitude && second.MinLongitude <= first.MaxLongitude &&
                    first.MinLatitude <= second.MaxLatitude && second.MinLatitude <= first.MaxLatitude &&
                    SegmentsIntersect(first.Start, first.End, second.Start, second.End))
                {
                    return true;
                }
            }
        }

        return IsInside(a.Representatives, b.Polygons) || IsInside(b.Representatives, a.Polygons);

        static bool IsInside(List<GeoPosition> positions, List<(GeoPolygon Polygon, GeoBounds Bounds)> polygons)
        {
            foreach (var position in positions)
            {
                foreach (var (polygon, bounds) in polygons)
                {
                    if (bounds.Contains(position) && polygon.Contains(position))
                        return true;
                }
            }

            return false;
        }
    }

    private static bool SegmentsIntersect(GeoPosition p1, GeoPosition p2, GeoPosition q1, GeoPosition q2)
    {
        var d1 = Orientation(q1, q2, p1);
        var d2 = Orientation(q1, q2, p2);
        var d3 = Orientation(p1, p2, q1);
        var d4 = Orientation(p1, p2, q2);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0)))
            return true;

        return (d1 == 0 && OnSegment(q1, q2, p1))
            || (d2 == 0 && OnSegment(q1, q2, p2))
            || (d3 == 0 && OnSegment(p1, p2, q1))
            || (d4 == 0 && OnSegment(p1, p2, q2));

        static double Orientation(GeoPosition a, GeoPosition b, GeoPosition c) => ((b.Longitude - a.Longitude) * (c.Latitude - a.Latitude)) - ((b.Latitude - a.Latitude) * (c.Longitude - a.Longitude));

        static bool OnSegment(GeoPosition a, GeoPosition b, GeoPosition c) =>
            Math.Min(a.Longitude, b.Longitude) <= c.Longitude && c.Longitude <= Math.Max(a.Longitude, b.Longitude) &&
            Math.Min(a.Latitude, b.Latitude) <= c.Latitude && c.Latitude <= Math.Max(a.Latitude, b.Latitude);
    }

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Candidate(GeoPosition Vertex, GeoPosition Target, Segment? Segment, double T, double Estimate);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Segment(GeoPosition Start, GeoPosition End)
    {
        public double MinLongitude => Math.Min(Start.Longitude, End.Longitude);

        public double MaxLongitude => Math.Max(Start.Longitude, End.Longitude);

        public double MinLatitude => Math.Min(Start.Latitude, End.Latitude);

        public double MaxLatitude => Math.Max(Start.Latitude, End.Latitude);

        public GeoPosition At(double t) => new(Start.Longitude + ((End.Longitude - Start.Longitude) * t), Start.Latitude + ((End.Latitude - Start.Latitude) * t));
    }

    /// <summary>The points, edges and polygons of a geometry.</summary>
    private sealed class Parts
    {
        public Parts(GeoGeometry geometry)
        {
            foreach (var simple in geometry.GetSimpleGeometries())
            {
                switch (simple)
                {
                    case GeoPoint point:
                        Points.Add(point.Position);
                        Vertices.Add(point.Position);
                        Representatives.Add(point.Position);
                        break;

                    case GeoLineString lineString:
                        AddLine(lineString.Positions, closed: false);
                        break;

                    case GeoPolygon polygon:
                        foreach (var ring in polygon.Rings)
                        {
                            AddLine(ring, closed: true);
                        }

                        if (polygon.GetBounds() is { } bounds)
                        {
                            Polygons.Add((polygon, bounds));
                        }

                        break;
                }
            }
        }

        public List<GeoPosition> Points { get; } = [];

        public List<Segment> Segments { get; } = [];

        public List<GeoPosition> Vertices { get; } = [];

        /// <summary>A position of each part: when no edges cross, a part is inside a polygon when this position is.</summary>
        public List<GeoPosition> Representatives { get; } = [];

        public List<(GeoPolygon Polygon, GeoBounds Bounds)> Polygons { get; } = [];

        private void AddLine(IReadOnlyList<GeoPosition> positions, bool closed)
        {
            if (positions.Count == 0)
                return;

            Representatives.Add(positions[0]);
            Vertices.AddRange(positions);
            if (positions.Count == 1)
            {
                Points.Add(positions[0]);
                return;
            }

            for (var i = 0; i + 1 < positions.Count; i++)
            {
                Segments.Add(new Segment(positions[i], positions[i + 1]));
            }

            if (closed && (positions[0].Longitude != positions[^1].Longitude || positions[0].Latitude != positions[^1].Latitude))
            {
                Segments.Add(new Segment(positions[^1], positions[0]));
            }
        }
    }
}
