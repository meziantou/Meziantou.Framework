using System.Globalization;
using System.Text;

namespace Meziantou.Framework.Geo.Internals;

/// <summary>Reads the WKT 1 of the OGC and of ESRI that defines a coordinate reference system, as in a <c>.prj</c> file.</summary>
internal static class CoordinateSystemWktReader
{
    private const int MaxDepth = 32;

    public static CoordinateReferenceSystem Read(string wkt)
    {
        var parser = new Parser(wkt);
        var root = parser.ParseRoot();
        return ReadCoordinateSystem(root);
    }

    private static CoordinateReferenceSystem ReadCoordinateSystem(Node node)
    {
        var epsgCode = GetEpsgCode(node);
        if (epsgCode is { } code && EpsgCoordinateSystems.Get(code) is { } builtIn)
            return builtIn;

        return Normalize(node.Keyword) switch
        {
            "projcs" => ReadProjected(node, epsgCode),
            "geogcs" => ReadGeographic(node, epsgCode),
            "compdcs" => node.Children.FirstOrDefault(child => Normalize(child.Keyword) is "projcs" or "geogcs") is { } horizontal
                ? ReadCoordinateSystem(horizontal)
                : throw new NotSupportedException("The compound coordinate system has no horizontal coordinate system (PROJCS or GEOGCS)."),
            "projcrs" or "projectedcrs" or "geogcrs" or "geographiccrs" or "geodcrs" or "geodeticcrs" or "compoundcrs" or "boundcrs"
                => throw new NotSupportedException("A WKT 2 definition is only supported with the EPSG code of a built-in coordinate reference system, such as ID[\"EPSG\",2154]."),
            "geoccs" => throw new NotSupportedException("Geocentric coordinate systems (GEOCCS) are not supported."),
            _ => throw new FormatException($"The WKT is not the definition of a coordinate reference system: it starts with '{node.Keyword}'."),
        };
    }

    private static GeographicCoordinateSystem ReadGeographic(Node node, int? epsgCode)
    {
        var name = node.GetString(0) ?? "Unknown";
        var datum = node.GetChild("DATUM") ?? throw new FormatException($"The geographic coordinate system '{name}' has no DATUM.");
        var spheroid = datum.GetChild("SPHEROID") ?? datum.GetChild("ELLIPSOID") ?? throw new FormatException($"The datum of '{name}' has no SPHEROID.");
        var semiMajorAxis = spheroid.GetNumber(1) ?? throw new FormatException($"The SPHEROID of '{name}' has no semi-major axis.");
        var inverseFlattening = spheroid.GetNumber(2) ?? throw new FormatException($"The SPHEROID of '{name}' has no inverse flattening.");
        if (semiMajorAxis <= 0 || (inverseFlattening is not 0 && inverseFlattening <= 1))
            throw new FormatException($"The SPHEROID of '{name}' is not valid: the semi-major axis must be positive, and the inverse flattening 0 or greater than 1.");

        var ellipsoid = new Ellipsoid(spheroid.GetString(0) ?? "Unknown", semiMajorAxis, inverseFlattening);

        HelmertTransformation? toWgs84;
        if (datum.GetChild("TOWGS84") is { } towgs84)
        {
            var values = towgs84.Values.OfType<double>().ToArray();
            toWgs84 = values.Length switch
            {
                3 => new HelmertTransformation(values[0], values[1], values[2]),
                7 => new HelmertTransformation(values[0], values[1], values[2], values[3], values[4], values[5], values[6]),
                _ => throw new FormatException($"The TOWGS84 of '{name}' must have 3 or 7 numbers."),
            };
        }
        else
        {
            // ESRI definitions have no TOWGS84: give the datums that differ much from WGS 84 the shift of the EPSG dataset
            toWgs84 = Normalize(datum.GetString(0) ?? "") switch
            {
                "dntf" or "ntf" or "nouvelletriangulationfrancaise" or "nouvelletriangulationfrancaiseparis" => EpsgCoordinateSystems.NtfToWgs84,
                "dosgb1936" or "osgb1936" or "osgb36" => EpsgCoordinateSystems.Osgb36ToWgs84,
                _ => null,
            };
        }

        var primeMeridian = node.GetChild("PRIMEM")?.GetNumber(1) ?? 0;
        var angularUnit = node.GetChild("UNIT")?.GetNumber(1) ?? (Math.PI / 180);
        if (!double.IsFinite(primeMeridian) || !double.IsFinite(angularUnit) || angularUnit <= 0)
            throw new FormatException($"The PRIMEM or the UNIT of '{name}' is not valid.");

        // The definitions round the degree to 0.0174532925199433: use the exact value, so WGS 84 converts without rounding
        angularUnit = SnapAngularUnit(angularUnit);

        return new GeographicCoordinateSystem(name, ellipsoid)
        {
            EpsgCode = epsgCode,
            ToWgs84Transformation = toWgs84,
            PrimeMeridian = primeMeridian,
            AngularUnit = angularUnit,
        };
    }

    private static ProjectedCoordinateSystem ReadProjected(Node node, int? epsgCode)
    {
        var name = node.GetString(0) ?? "Unknown";
        var geogcs = node.GetChild("GEOGCS") ?? throw new FormatException($"The projected coordinate system '{name}' has no GEOGCS.");
        var geographic = ReadGeographic(geogcs, GetEpsgCode(geogcs));
        var linearUnit = node.GetChild("UNIT")?.GetNumber(1) ?? 1;
        if (!double.IsFinite(linearUnit) || linearUnit <= 0)
            throw new FormatException($"The UNIT of '{name}' is not valid.");

        var parameters = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var parameter in node.Children.Where(child => Normalize(child.Keyword) is "parameter"))
        {
            if (parameter.GetString(0) is { } parameterName && parameter.GetNumber(1) is { } value)
            {
                parameters[Normalize(parameterName)] = value;
            }
        }

        double Get(double defaultValue, params string[] names)
        {
            foreach (var parameterName in names)
            {
                if (parameters.TryGetValue(parameterName, out var value))
                    return value;
            }

            return defaultValue;
        }

        // The angles are in the unit of the GEOGCS, such as the grads of NTF (Paris), as GDAL and ESRI write them
        var angleToDegrees = geographic.AngularUnit * 180 / Math.PI;
        double GetAngle(params string[] names) => Get(0, names) * angleToDegrees;

        double? GetOptionalAngle(string parameterName) => parameters.TryGetValue(parameterName, out var value) ? value * angleToDegrees : null;

        var method = node.GetChild("PROJECTION")?.GetString(0) ?? throw new FormatException($"The projected coordinate system '{name}' has no PROJECTION.");
        var latitudeOfOrigin = GetAngle("latitudeoforigin", "latitudeofcenter", "latitudeoffalseorigin");
        var centralMeridian = GetAngle("centralmeridian", "longitudeofcenter", "longitudeoforigin", "longitudeoffalseorigin");
        var scaleFactor = Get(1, "scalefactor", "scalefactoratnaturalorigin");

        // The false easting and northing are in the linear unit of the PROJCS
        var falseEasting = Get(0, "falseeasting", "eastingoffalseorigin") * linearUnit;
        var falseNorthing = Get(0, "falsenorthing", "northingoffalseorigin") * linearUnit;

        CoordinateConversion conversion = Normalize(method) switch
        {
            "transversemercator" or "gausskruger" => new TransverseMercatorConversion
            {
                LatitudeOfOrigin = latitudeOfOrigin,
                CentralMeridian = centralMeridian,
                ScaleFactor = scaleFactor,
                FalseEasting = falseEasting,
                FalseNorthing = falseNorthing,
            },
            "lambertconformalconic" or "lambertconformalconic1sp" or "lambertconformalconic2sp" => new LambertConformalConicConversion
            {
                LatitudeOfOrigin = latitudeOfOrigin,
                CentralMeridian = centralMeridian,
                StandardParallel1 = GetOptionalAngle("standardparallel1"),
                StandardParallel2 = GetOptionalAngle("standardparallel2"),
                ScaleFactor = scaleFactor,
                FalseEasting = falseEasting,
                FalseNorthing = falseNorthing,
            },
            "mercator" or "mercator1sp" or "mercator2sp" => new MercatorConversion
            {
                CentralMeridian = centralMeridian,
                StandardParallel = GetOptionalAngle("standardparallel1"),
                ScaleFactor = scaleFactor,
                FalseEasting = falseEasting,
                FalseNorthing = falseNorthing,
                IsSpherical = IsSphericalMercatorExtension(node),
            },
            "mercatorauxiliarysphere" or "popularvisualisationpseudomercator" or "pseudomercator" => new MercatorConversion
            {
                CentralMeridian = centralMeridian,
                StandardParallel = GetOptionalAngle("standardparallel1") is { } parallel and not 0 ? parallel : null,
                FalseEasting = falseEasting,
                FalseNorthing = falseNorthing,
                IsSpherical = true,
            },
            _ => throw new NotSupportedException($"The projection '{method}' of '{name}' is not supported. The supported projections are the transverse Mercator, the Lambert conformal conic and the Mercator."),
        };

        return new ProjectedCoordinateSystem(name, geographic, conversion) { EpsgCode = epsgCode, LinearUnit = linearUnit };
    }

    private static double SnapAngularUnit(double unit)
    {
        foreach (var exact in (ReadOnlySpan<double>)[Math.PI / 180, Math.PI / 200])
        {
            if (Math.Abs((unit / exact) - 1) < 1e-12)
                return exact;
        }

        return unit;
    }

    // GDAL writes Web Mercator as a Mercator_1SP with EXTENSION["PROJ4","+proj=merc +a=6378137 +b=6378137 ..."]
    private static bool IsSphericalMercatorExtension(Node node)
    {
        var extension = node.Children.FirstOrDefault(child => Normalize(child.Keyword) is "extension" && string.Equals(child.GetString(0), "PROJ4", StringComparison.OrdinalIgnoreCase));
        if (extension?.GetString(1) is not { } proj4)
            return false;

        var parts = proj4.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var a = parts.FirstOrDefault(part => part.StartsWith("+a=", StringComparison.Ordinal));
        var b = parts.FirstOrDefault(part => part.StartsWith("+b=", StringComparison.Ordinal));
        return a is not null && b is not null && string.Equals(a[3..], b[3..], StringComparison.Ordinal);
    }

    private static int? GetEpsgCode(Node node)
    {
        foreach (var child in node.Children)
        {
            if (Normalize(child.Keyword) is not ("authority" or "id"))
                continue;

            if (!string.Equals(child.GetString(0), "EPSG", StringComparison.OrdinalIgnoreCase) || child.Values.Count < 2)
                continue;

            switch (child.Values[1])
            {
                case string text when int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var code):
                    return code;
                case double number when number is >= 0 and <= int.MaxValue && number == Math.Floor(number):
                    return (int)number;
            }
        }

        return null;
    }

    // Lowercase ASCII letters and digits only: ESRI writes Standard_Parallel_1, the OGC standard_parallel_1, and GDAL COMPD_CS
    private static string Normalize(string value)
    {
        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            if (char.IsAsciiLetterOrDigit(c))
            {
                builder.Append(char.ToLowerInvariant(c));
            }
        }

        return builder.ToString();
    }

    /// <summary>A keyword and its values: <see cref="string"/> (a quoted text), <see cref="double"/> or <see cref="Node"/> (a keyword, with or without values).</summary>
    private sealed class Node(string keyword, List<object> values)
    {
        public string Keyword { get; } = keyword;

        public List<object> Values { get; } = values;

        public IEnumerable<Node> Children => Values.OfType<Node>();

        public Node? GetChild(string keyword) => Children.FirstOrDefault(child => string.Equals(child.Keyword, keyword, StringComparison.OrdinalIgnoreCase));

        public string? GetString(int index) => index < Values.Count ? Values[index] as string : null;

        public double? GetNumber(int index) => index < Values.Count && Values[index] is double value ? value : null;
    }

    private sealed class Parser(string text)
    {
        private int _position;

        public Node ParseRoot()
        {
            SkipWhitespace();
            if (_position < text.Length && text[_position] is '\uFEFF')
            {
                _position++;
                SkipWhitespace();
            }

            var node = ParseNode(depth: 0);
            SkipWhitespace();
            if (_position < text.Length)
                throw Error("Unexpected text after the definition");

            return node;
        }

        private Node ParseNode(int depth)
        {
            if (depth > MaxDepth)
                throw Error($"The definition is nested more than {MaxDepth.ToString(CultureInfo.InvariantCulture)} levels deep");

            var start = _position;
            while (_position < text.Length && (char.IsAsciiLetterOrDigit(text[_position]) || text[_position] is '_'))
            {
                _position++;
            }

            if (_position == start)
                throw Error("Expected a keyword");

            var keyword = text[start.._position];
            var values = new List<object>();
            SkipWhitespace();
            if (_position < text.Length && text[_position] is '[' or '(')
            {
                var close = text[_position] is '[' ? ']' : ')';
                _position++;
                SkipWhitespace();
                if (_position < text.Length && text[_position] == close)
                {
                    _position++;
                    return new Node(keyword, values);
                }

                while (true)
                {
                    SkipWhitespace();
                    values.Add(ParseValue(depth));
                    SkipWhitespace();
                    if (_position >= text.Length)
                        throw Error($"Expected ',' or '{close}'");

                    if (text[_position] == close)
                    {
                        _position++;
                        break;
                    }

                    if (text[_position] is not ',')
                        throw Error($"Expected ',' or '{close}'");

                    _position++;
                }
            }

            return new Node(keyword, values);
        }

        private object ParseValue(int depth)
        {
            if (_position >= text.Length)
                throw Error("Expected a value");

            var c = text[_position];
            if (c is '"')
                return ParseString();

            if (char.IsAsciiDigit(c) || c is '-' or '+' or '.')
                return ParseNumber();

            return ParseNode(depth + 1);
        }

        private string ParseString()
        {
            var builder = new StringBuilder();
            _position++;
            while (true)
            {
                if (_position >= text.Length)
                    throw Error("Unterminated text");

                var c = text[_position++];
                if (c is '"')
                {
                    // WKT 2 escapes a quote by doubling it
                    if (_position < text.Length && text[_position] is '"')
                    {
                        builder.Append('"');
                        _position++;
                        continue;
                    }

                    return builder.ToString();
                }

                builder.Append(c);
            }
        }

        private double ParseNumber()
        {
            var start = _position;
            while (_position < text.Length && (char.IsAsciiDigit(text[_position]) || text[_position] is '-' or '+' or '.' or 'e' or 'E'))
            {
                _position++;
            }

            if (!double.TryParse(text.AsSpan(start, _position - start), NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || !double.IsFinite(value))
            {
                _position = start;
                throw Error("Expected a number");
            }

            return value;
        }

        private void SkipWhitespace()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private FormatException Error(string message) => new($"{message} at position {_position.ToString(CultureInfo.InvariantCulture)} of the WKT.");
    }
}
