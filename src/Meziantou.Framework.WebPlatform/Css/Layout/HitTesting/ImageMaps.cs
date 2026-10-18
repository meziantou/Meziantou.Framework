using System.Globalization;
using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The hit testing of the image maps (https://html.spec.whatwg.org/multipage/image-maps.html#image-map-processing-model): the
/// area of the image map of an image that contains a point of the image.
/// </summary>
internal static class ImageMaps
{
    /// <summary>
    /// Returns the first area of the image map of an image (its <c>usemap</c> attribute) whose shape contains a point (in the
    /// coordinates of the fragment of the image), or null.
    /// </summary>
    public static Element? HitTest(BoxFragment fragment, HtmlImageElement image, PhysicalOffset point)
    {
        if (GetMap(image) is not { } map)
            return null;

        // The coordinates of the areas are CSS pixels relative to the image
        var content = fragment.ContentBoxRect;
        var zoom = fragment.Box.Style.Layout.EffectiveZoom;
        var x = (point.X - content.X) / zoom;
        var y = (point.Y - content.Y) / zoom;
        for (var node = map.FirstChild; node is not null; node = node.GetNextInTreeOrder(map))
        {
            if (node is HtmlAreaElement area && Contains(area, x, y))
                return area;
        }

        return null;
    }

    /// <summary>
    /// Returns the map of an image (https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-hash-name-reference):
    /// the first map element in tree order whose id or name is the name after the first # of the usemap attribute.
    /// </summary>
    private static HtmlMapElement? GetMap(HtmlImageElement image)
    {
        // "If the string being parsed does not contain a U+0023 NUMBER SIGN character, or if the first such character in the
        // string is the last character in the string, then return null"
        var useMap = image.GetAttributeValue("usemap");
        var hash = useMap?.IndexOf('#', StringComparison.Ordinal) ?? -1;
        if (hash < 0 || hash == useMap!.Length - 1)
            return null;

        var name = useMap[(hash + 1)..];
        var root = image.GetRootNode();
        for (var node = root.FirstChild; node is not null; node = node.GetNextInTreeOrder(root))
        {
            if (node is HtmlMapElement map && (map.GetAttributeValue("id") == name || map.GetAttributeValue("name") == name))
                return map;
        }

        return null;
    }

    /// <summary>Whether the shape of an area (https://html.spec.whatwg.org/multipage/image-maps.html#attr-area-shape) contains a point.</summary>
    private static bool Contains(HtmlAreaElement area, double x, double y)
    {
        var shape = area.GetAttributeValue("shape")?.ToLowerInvariant();
        if (shape == "default")
            return true;

        var coords = ParseListOfFloatingPointNumbers(area.GetAttributeValue("coords") ?? "");
        switch (shape)
        {
            case "circle" or "circ":
            {
                if (coords.Count < 3 || coords[2] <= 0)
                    return false;

                var dx = x - coords[0];
                var dy = y - coords[1];
                return (dx * dx) + (dy * dy) <= coords[2] * coords[2];
            }

            case "poly" or "polygon":
            {
                if (coords.Count < 6)
                    return false;

                // The even-odd rule
                var count = coords.Count / 2;
                var inside = false;
                for (int i = 0, j = count - 1; i < count; j = i++)
                {
                    var xi = coords[2 * i];
                    var yi = coords[(2 * i) + 1];
                    var xj = coords[2 * j];
                    var yj = coords[(2 * j) + 1];
                    if ((yi > y) != (yj > y) && x < ((xj - xi) * (y - yi) / (yj - yi)) + xi)
                    {
                        inside = !inside;
                    }
                }

                return inside || IsOnPolygonEdge(coords, count, x, y);
            }

            default:
            {
                // rect, rectangle, and the missing and invalid values
                if (coords.Count < 4)
                    return false;

                var left = Math.Min(coords[0], coords[2]);
                var right = Math.Max(coords[0], coords[2]);
                var top = Math.Min(coords[1], coords[3]);
                var bottom = Math.Max(coords[1], coords[3]);
                return x >= left && x < right && y >= top && y < bottom;
            }
        }
    }

    /// <summary>Whether a point is on an edge of a polygon (the edges belong to the area, as in the browsers).</summary>
    private static bool IsOnPolygonEdge(List<double> coords, int count, double x, double y)
    {
        for (int i = 0, j = count - 1; i < count; j = i++)
        {
            var (x1, y1, x2, y2) = (coords[2 * j], coords[(2 * j) + 1], coords[2 * i], coords[(2 * i) + 1]);
            var cross = ((x2 - x1) * (y - y1)) - ((y2 - y1) * (x - x1));
            if (Math.Abs(cross) < 1e-9 && x >= Math.Min(x1, x2) && x <= Math.Max(x1, x2) && y >= Math.Min(y1, y2) && y <= Math.Max(y1, y2))
                return true;
        }

        return false;
    }

    /// <summary>https://html.spec.whatwg.org/multipage/common-microsyntaxes.html#rules-for-parsing-a-list-of-floating-point-numbers</summary>
    private static List<double> ParseListOfFloatingPointNumbers(string input)
    {
        var numbers = new List<double>();
        var position = 0;
        SkipSeparators();
        while (position < input.Length)
        {
            // Skip the garbage before the number ("not ASCII whitespace, ',', ';', ASCII digits, '.', or '-'"), collect a sequence of
            // characters that are not separators, and parse its number prefix (0 when there is none)
            while (position < input.Length && !IsSeparator(input[position]) && !char.IsAsciiDigit(input[position]) && input[position] is not ('.' or '-'))
            {
                position++;
            }

            var start = position;
            while (position < input.Length && !IsSeparator(input[position]))
            {
                position++;
            }

            numbers.Add(ParseNumberPrefix(input.AsSpan(start, position - start)));
            SkipSeparators();
        }

        return numbers;

        void SkipSeparators()
        {
            while (position < input.Length && IsSeparator(input[position]))
            {
                position++;
            }
        }

        static bool IsSeparator(char c) => c is ' ' or '\t' or '\n' or '\f' or '\r' or ',' or ';';

        static double ParseNumberPrefix(ReadOnlySpan<char> value)
        {
            // The longest prefix that is a number (the garbage after it is ignored)
            for (var length = value.Length; length > 0; length--)
            {
                if (double.TryParse(value[..length], NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint | NumberStyles.AllowExponent, CultureInfo.InvariantCulture, out var number) && double.IsFinite(number))
                    return number;
            }

            return 0;
        }
    }
}
