using System.Globalization;
using System.Text;
using Meziantou.Framework.Geo.Internals;

namespace Meziantou.Framework.Geo;

/// <summary>Reads and writes the Well-Known Text (WKT) representation of a geometry, such as <c>POINT (2.35 48.86)</c>, as defined by the OGC Simple Features and ISO 19125.</summary>
/// <remarks>
/// <para>
/// The reader accepts the seven geometry types, in any case and with any whitespace, the <c>Z</c>, <c>M</c> and
/// <c>ZM</c> tags, also written after the type as in PostGIS (<c>POINTZ</c>), positions of 3 numbers without a tag
/// (an altitude) or of 4 numbers (an altitude and a measure), the multipoints with and without parentheses around
/// their points (<c>MULTIPOINT ((1 2), (3 4))</c> and <c>MULTIPOINT (1 2, 3 4)</c>) and the <c>SRID=4326;</c> prefix of
/// the extended WKT of PostGIS (EWKT), which <see cref="Parse(string, out int?)"/> returns. It keeps the Z values as <see cref="GeoPosition.Altitude"/>
/// and drops the M values (measures). A child of a <c>GEOMETRYCOLLECTION</c> without a tag uses the tag of the
/// collection.
/// </para>
/// <para>
/// <c>POINT EMPTY</c> becomes an empty <see cref="GeoMultiPoint"/>, as a <see cref="GeoPoint"/> always has a position,
/// and the empty points of a <c>MULTIPOINT</c> are skipped. The other types are rejected, such as <c>CIRCULARSTRING</c>,
/// <c>TRIANGLE</c> or <c>TIN</c>.
/// </para>
/// <para>
/// The writer writes the ISO form (<c>POINT Z (1 2 3)</c>, <c>MULTIPOINT ((1 2), (3 4))</c>) with the shortest
/// numbers that read back to the same values. A geometry has a single number of dimensions: when a position of the
/// geometry has an altitude, every position is written with one, and a missing altitude is written as 0.
/// </para>
/// </remarks>
public static class WellKnownText
{
    // GEOMETRYCOLLECTION can nest: limit the depth so a hostile text cannot overflow the stack
    private const int MaxDepth = 100;

    /// <summary>Reads the WKT, or the extended WKT of PostGIS, of a geometry.</summary>
    /// <param name="text">The text, such as <c>POINT (2.35 48.86)</c> or <c>SRID=4326;LINESTRING Z (0 0 10, 1 1 20)</c>.</param>
    /// <returns>The geometry.</returns>
    /// <exception cref="FormatException">The text is not the WKT of a supported geometry. The message gives the position of the error in the text.</exception>
    public static GeoGeometry Parse(string text) => Parse(text, out _);

    /// <summary>Reads the WKT, or the extended WKT of PostGIS, of a geometry, and the SRID of the extended WKT.</summary>
    /// <param name="text">The text, such as <c>SRID=2154;POINT (652310.72 6862414.64)</c>.</param>
    /// <param name="srid">The SRID of the <c>SRID=</c> prefix, usually an EPSG code that <see cref="CoordinateReferenceSystem.FromEpsg"/> converts to a coordinate reference system, or <see langword="null"/> when the text has no prefix.</param>
    /// <returns>The geometry, with the coordinates of the text: they are not converted.</returns>
    /// <exception cref="FormatException">The text is not the WKT of a supported geometry. The message gives the position of the error in the text.</exception>
    public static GeoGeometry Parse(string text, out int? srid)
    {
        ArgumentNullException.ThrowIfNull(text);
        var parser = new Parser(text);
        var geometry = parser.ParseRoot();
        srid = parser.Srid;
        return geometry;
    }

    /// <summary>Writes the WKT of a geometry, such as <c>POINT (2.35 48.86)</c>.</summary>
    /// <param name="geometry">The geometry.</param>
    /// <returns>The WKT of the geometry.</returns>
    /// <exception cref="ArgumentException">A position is not finite.</exception>
    public static string Format(GeoGeometry geometry)
    {
        ArgumentNullException.ThrowIfNull(geometry);
        GeoValidation.EnsureFinite(geometry, nameof(geometry));
        var hasAltitude = geometry.GetPositions().Any(position => position.Altitude is not null);
        var builder = new StringBuilder();
        WriteGeometry(builder, geometry, hasAltitude);
        return builder.ToString();
    }

    private static void WriteGeometry(StringBuilder builder, GeoGeometry geometry, bool hasAltitude)
    {
        builder.Append(geometry.Type switch
        {
            GeoGeometryType.Point => "POINT",
            GeoGeometryType.LineString => "LINESTRING",
            GeoGeometryType.Polygon => "POLYGON",
            GeoGeometryType.MultiPoint => "MULTIPOINT",
            GeoGeometryType.MultiLineString => "MULTILINESTRING",
            GeoGeometryType.MultiPolygon => "MULTIPOLYGON",
            _ => "GEOMETRYCOLLECTION",
        });

        if (hasAltitude)
        {
            builder.Append(" Z");
        }

        builder.Append(' ');
        switch (geometry)
        {
            case GeoPoint point:
                builder.Append('(');
                WritePosition(builder, point.Position, hasAltitude);
                builder.Append(')');
                break;

            case GeoLineString lineString:
                WritePositions(builder, lineString.Positions, hasAltitude);
                break;

            case GeoPolygon polygon:
                WritePolygon(builder, polygon, hasAltitude);
                break;

            case GeoMultiPoint multiPoint:
                WriteList(builder, multiPoint.Positions, position =>
                {
                    builder.Append('(');
                    WritePosition(builder, position, hasAltitude);
                    builder.Append(')');
                });
                break;

            case GeoMultiLineString multiLineString:
                WriteList(builder, multiLineString.LineStrings, lineString => WritePositions(builder, lineString.Positions, hasAltitude));
                break;

            case GeoMultiPolygon multiPolygon:
                WriteList(builder, multiPolygon.Polygons, polygon => WritePolygon(builder, polygon, hasAltitude));
                break;

            case GeoGeometryCollection collection:
                WriteList(builder, collection.Geometries, item => WriteGeometry(builder, item, hasAltitude));
                break;
        }
    }

    private static void WritePolygon(StringBuilder builder, GeoPolygon polygon, bool hasAltitude)
    {
        WriteList(builder, polygon.Rings, ring => WritePositions(builder, ring, hasAltitude));
    }

    private static void WritePositions(StringBuilder builder, IReadOnlyList<GeoPosition> positions, bool hasAltitude)
    {
        WriteList(builder, positions, position => WritePosition(builder, position, hasAltitude));
    }

    private static void WriteList<T>(StringBuilder builder, IReadOnlyList<T> items, Action<T> writeItem)
    {
        if (items.Count is 0)
        {
            builder.Append("EMPTY");
            return;
        }

        builder.Append('(');
        for (var i = 0; i < items.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(", ");
            }

            writeItem(items[i]);
        }

        builder.Append(')');
    }

    private static void WritePosition(StringBuilder builder, GeoPosition position, bool hasAltitude)
    {
        builder.Append(position.Longitude.ToString(CultureInfo.InvariantCulture));
        builder.Append(' ');
        builder.Append(position.Latitude.ToString(CultureInfo.InvariantCulture));
        if (hasAltitude)
        {
            builder.Append(' ');
            builder.Append((position.Altitude ?? 0).ToString(CultureInfo.InvariantCulture));
        }
    }

    private enum Dimensions
    {
        /// <summary>No tag: 2, 3 (an altitude) or 4 (an altitude and a measure) numbers per position.</summary>
        Untagged,
        Z,
        M,
        ZM,
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public int? Srid { get; private set; }

        public GeoGeometry ParseRoot()
        {
            SkipWhitespace();
            if (TryReadKeyword("SRID"))
            {
                Expect('=');
                SkipWhitespace();
                var start = _position;
                if (_position < text.Length && text[_position] is '-' or '+')
                {
                    _position++;
                }

                while (_position < text.Length && char.IsAsciiDigit(text[_position]))
                {
                    _position++;
                }

                if (!int.TryParse(text.AsSpan(start, _position - start), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var srid))
                    throw Error("Expected the SRID number", start);

                Srid = srid;

                Expect(';');
            }

            var geometry = ParseGeometry(Dimensions.Untagged, depth: 0) ?? new GeoMultiPoint([]);
            SkipWhitespace();
            if (_position < text.Length)
                throw Error("Unexpected text after the geometry", _position);

            return geometry;
        }

        /// <summary>Reads a tagged geometry. Returns <see langword="null"/> for <c>POINT EMPTY</c>.</summary>
        private GeoGeometry? ParseGeometry(Dimensions parentDimensions, int depth)
        {
            SkipWhitespace();
            var start = _position;
            var word = ReadWord();
            if (word.IsEmpty)
                throw Error("Expected a geometry type", start);

            var dimensions = Dimensions.Untagged;
            if (word.EndsWith("ZM", StringComparison.OrdinalIgnoreCase) && TryGetType(word[..^2]) is not null)
            {
                dimensions = Dimensions.ZM;
                word = word[..^2];
            }
            else if (word.EndsWith('Z') || word.EndsWith('z'))
            {
                if (TryGetType(word[..^1]) is not null)
                {
                    dimensions = Dimensions.Z;
                    word = word[..^1];
                }
            }
            else if (word.EndsWith('M') || word.EndsWith('m'))
            {
                if (TryGetType(word[..^1]) is not null)
                {
                    dimensions = Dimensions.M;
                    word = word[..^1];
                }
            }

            var type = TryGetType(word) ?? throw Error($"'{word}' is not a supported geometry type", start);
            if (dimensions is Dimensions.Untagged)
            {
                if (TryReadKeyword("ZM"))
                {
                    dimensions = Dimensions.ZM;
                }
                else if (TryReadKeyword("Z"))
                {
                    dimensions = Dimensions.Z;
                }
                else if (TryReadKeyword("M"))
                {
                    dimensions = Dimensions.M;
                }
                else
                {
                    dimensions = parentDimensions;
                }
            }

            if (TryReadKeyword("EMPTY"))
            {
                return type switch
                {
                    GeoGeometryType.Point => null,
                    GeoGeometryType.LineString => new GeoLineString([]),
                    GeoGeometryType.Polygon => new GeoPolygon([]),
                    GeoGeometryType.MultiPoint => new GeoMultiPoint([]),
                    GeoGeometryType.MultiLineString => new GeoMultiLineString([]),
                    GeoGeometryType.MultiPolygon => new GeoMultiPolygon([]),
                    _ => new GeoGeometryCollection([]),
                };
            }

            switch (type)
            {
                case GeoGeometryType.Point:
                    Expect('(');
                    var position = ReadPosition(dimensions);
                    Expect(')');
                    return new GeoPoint(position);

                case GeoGeometryType.LineString:
                    return new GeoLineString(ReadPositions(dimensions));

                case GeoGeometryType.Polygon:
                    return ReadPolygon(dimensions);

                case GeoGeometryType.MultiPoint:
                    var positions = new List<GeoPosition>();
                    ReadList(() =>
                    {
                        if (TryReadKeyword("EMPTY"))
                            return;

                        SkipWhitespace();
                        if (_position < text.Length && text[_position] is '(')
                        {
                            _position++;
                            positions.Add(ReadPosition(dimensions));
                            Expect(')');
                        }
                        else
                        {
                            positions.Add(ReadPosition(dimensions));
                        }
                    });
                    return new GeoMultiPoint(positions);

                case GeoGeometryType.MultiLineString:
                    var lineStrings = new List<GeoLineString>();
                    ReadList(() => lineStrings.Add(new GeoLineString(ReadPositions(dimensions))));
                    return new GeoMultiLineString(lineStrings);

                case GeoGeometryType.MultiPolygon:
                    var polygons = new List<GeoPolygon>();
                    ReadList(() => polygons.Add(ReadPolygon(dimensions)));
                    return new GeoMultiPolygon(polygons);

                default:
                    if (depth >= MaxDepth)
                        throw Error($"The geometry collections are nested more than {MaxDepth} levels deep", start);

                    var geometries = new List<GeoGeometry>();
                    ReadList(() => geometries.Add(ParseGeometry(dimensions, depth + 1) ?? new GeoMultiPoint([])));
                    return new GeoGeometryCollection(geometries);
            }
        }

        private GeoPolygon ReadPolygon(Dimensions dimensions)
        {
            var rings = new List<List<GeoPosition>>();
            ReadList(() => rings.Add(ReadPositions(dimensions)));
            return new GeoPolygon(rings);
        }

        /// <summary>Reads <c>EMPTY</c> or a list of positions in parentheses.</summary>
        private List<GeoPosition> ReadPositions(Dimensions dimensions)
        {
            var positions = new List<GeoPosition>();
            ReadList(() => positions.Add(ReadPosition(dimensions)));
            return positions;
        }

        /// <summary>Reads <c>EMPTY</c> or a list of items separated by commas, in parentheses.</summary>
        private void ReadList(Action readItem)
        {
            if (TryReadKeyword("EMPTY"))
                return;

            Expect('(');
            while (true)
            {
                readItem();
                SkipWhitespace();
                if (_position < text.Length && text[_position] is ',')
                {
                    _position++;
                    continue;
                }

                Expect(')');
                return;
            }
        }

        private GeoPosition ReadPosition(Dimensions dimensions)
        {
            var start = _position;
            var x = ReadNumber();
            var y = ReadNumber();
            Span<double> others = stackalloc double[2];
            var count = 0;
            while (true)
            {
                SkipWhitespace();
                if (_position >= text.Length || !IsNumberChar(text[_position]))
                    break;

                if (count == 2)
                    throw Error("A position has more than 4 numbers", start);

                others[count++] = ReadNumber();
            }

            var expected = dimensions switch
            {
                Dimensions.Z or Dimensions.M => 1,
                Dimensions.ZM => 2,
                _ => count,
            };

            if (count != expected)
                throw Error($"A position of a geometry tagged {dimensions} must have {expected + 2} numbers", start);

            double? altitude = count > 0 && dimensions is not Dimensions.M ? others[0] : null;
            return new GeoPosition(x, y, altitude);
        }

        private double ReadNumber()
        {
            SkipWhitespace();
            var start = _position;
            while (_position < text.Length && IsNumberChar(text[_position]))
            {
                _position++;
            }

            if (start == _position)
                throw Error("Expected a number", start);

            if (!double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
                throw Error($"'{text[start.._position]}' is not a valid number", start);

            return value;
        }

        private static bool IsNumberChar(char c) => char.IsAsciiDigit(c) || c is '-' or '+' or '.' or 'e' or 'E';

        private ReadOnlySpan<char> ReadWord()
        {
            var start = _position;
            while (_position < text.Length && char.IsAsciiLetter(text[_position]))
            {
                _position++;
            }

            return text.AsSpan(start, _position - start);
        }

        /// <summary>Reads a keyword when the next word is this keyword, in any case.</summary>
        private bool TryReadKeyword(string keyword)
        {
            SkipWhitespace();
            var end = _position;
            while (end < text.Length && char.IsAsciiLetter(text[end]))
            {
                end++;
            }

            if (!text.AsSpan(_position, end - _position).Equals(keyword, StringComparison.OrdinalIgnoreCase))
                return false;

            _position = end;
            return true;
        }

        private void Expect(char c)
        {
            SkipWhitespace();
            if (_position >= text.Length)
                throw Error($"Expected '{c}' but the text ends", _position);

            if (text[_position] != c)
                throw Error($"Expected '{c}' but found '{text[_position]}'", _position);

            _position++;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private static GeoGeometryType? TryGetType(ReadOnlySpan<char> word)
        {
            if (word.Equals("POINT", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.Point;

            if (word.Equals("LINESTRING", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.LineString;

            if (word.Equals("POLYGON", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.Polygon;

            if (word.Equals("MULTIPOINT", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.MultiPoint;

            if (word.Equals("MULTILINESTRING", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.MultiLineString;

            if (word.Equals("MULTIPOLYGON", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.MultiPolygon;

            if (word.Equals("GEOMETRYCOLLECTION", StringComparison.OrdinalIgnoreCase))
                return GeoGeometryType.GeometryCollection;

            return null;
        }

        private static FormatException Error(string message, int position) => new($"{message} at position {position.ToString(CultureInfo.InvariantCulture)} of the WKT.");
    }
}
