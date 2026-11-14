using System.Globalization;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Reads the values of the attributes of SVG: numbers, lengths, lists, transformations and colors.</summary>
internal static class SvgValues
{
    private static readonly Dictionary<string, uint> NamedColors = CreateNamedColors();

    /// <summary>Reads a number: <c>[+-]digits[.digits][e[+-]digits]</c>, as in the grammar of path data.</summary>
    public static bool TryReadNumber(ref ReadOnlySpan<char> text, out double value)
    {
        value = 0;
        var i = 0;
        if (i < text.Length && text[i] is '+' or '-')
        {
            i++;
        }

        var digits = 0;
        while (i < text.Length && char.IsAsciiDigit(text[i]))
        {
            i++;
            digits++;
        }

        if (i < text.Length && text[i] == '.')
        {
            i++;
            while (i < text.Length && char.IsAsciiDigit(text[i]))
            {
                i++;
                digits++;
            }
        }

        if (digits == 0)
            return false;

        if (i < text.Length && text[i] is 'e' or 'E')
        {
            var exponent = i + 1;
            if (exponent < text.Length && text[exponent] is '+' or '-')
            {
                exponent++;
            }

            if (exponent < text.Length && char.IsAsciiDigit(text[exponent]))
            {
                i = exponent;
                while (i < text.Length && char.IsAsciiDigit(text[i]))
                {
                    i++;
                }
            }
        }

        if (!double.TryParse(text[..i], NumberStyles.Float, CultureInfo.InvariantCulture, out value) || !double.IsFinite(value))
            return false;

        text = text[i..];
        return true;
    }

    /// <summary>Skips the white space and at most one comma that separate the values of a list.</summary>
    public static void SkipSeparators(ref ReadOnlySpan<char> text)
    {
        text = text.TrimStart();
        if (text is [',', ..])
        {
            text = text[1..].TrimStart();
        }
    }

    /// <summary>Reads a number alone.</summary>
    public static bool TryParseNumber(string? text, out double value)
    {
        value = 0;
        if (text is null)
            return false;

        var span = text.AsSpan().Trim();
        return TryReadNumber(ref span, out value) && span.IsEmpty;
    }

    /// <summary>Reads a number, or a percentage of a reference, alone.</summary>
    public static bool TryParseNumberOrPercentage(string? text, double reference, out double value)
    {
        value = 0;
        if (text is null)
            return false;

        var span = text.AsSpan().Trim();
        if (!TryReadNumber(ref span, out value))
            return false;

        if (span is ['%'])
        {
            value = value / 100 * reference;
            return true;
        }

        return span.IsEmpty;
    }

    /// <summary>Reads a length in user units: a number, a percentage of a reference, or a number with an absolute unit of CSS.</summary>
    /// <remarks>The relative units <c>em</c> and <c>ex</c> are not allowed by the specification: such a length is invalid.</remarks>
    public static bool TryParseLength(string? text, double reference, out double value)
    {
        value = 0;
        if (text is null)
            return false;

        var span = text.AsSpan().Trim();
        if (!TryReadNumber(ref span, out value))
            return false;

        value *= GetUnitFactor(span, reference);
        return double.IsFinite(value);
    }

    /// <summary>Reads a list of numbers separated by white space or commas.</summary>
    /// <returns>The numbers read before the first one that is not a number.</returns>
    public static List<double> ParseNumberList(string? text)
    {
        var values = new List<double>();
        if (text is null)
            return values;

        var span = text.AsSpan().Trim();
        while (!span.IsEmpty && TryReadNumber(ref span, out var value))
        {
            values.Add(value);
            SkipSeparators(ref span);
        }

        return values;
    }

    // The factor of a unit of a length: the absolute units of CSS, or a percentage of a reference; NaN for another unit.
    private static double GetUnitFactor(ReadOnlySpan<char> unit, double reference) => unit switch
    {
        [] or "px" => 1,
        "%" => reference / 100,
        "in" => 96,
        "cm" => 96 / 2.54,
        "mm" => 96 / 25.4,
        "pt" => 96.0 / 72,
        "pc" => 16,
        _ => double.NaN,
    };

    /// <summary>Reads a list of lengths separated by white space or commas (<see cref="TryParseLength"/>).</summary>
    /// <returns><see langword="null"/> when an item is not a length: the list is read up to that item, without copying it.</returns>
    public static List<double>? ParseLengthList(string text, double reference)
    {
        var values = new List<double>();
        var span = text.AsSpan();
        SkipSeparators(ref span);
        while (!span.IsEmpty)
        {
            var end = span.IndexOfAny(" ,\t\n\r");
            var item = end < 0 ? span : span[..end];
            if (!TryReadNumber(ref item, out var value))
                return null;

            value *= GetUnitFactor(item, reference);
            if (!double.IsFinite(value))
                return null;

            values.Add(value);
            span = end < 0 ? default : span[end..];
            SkipSeparators(ref span);
        }

        return values;
    }

    /// <summary>Reads the value of a <c>transform</c> attribute: the transformations of the list apply from the last one.</summary>
    /// <returns>The identity for a list that is not valid, as SVG 1.1 says.</returns>
    public static AffineTransform ParseTransform(string? text)
    {
        if (text is null)
            return AffineTransform.Identity;

        var span = text.AsSpan().Trim();
        var result = AffineTransform.Identity;
        Span<double> arguments = stackalloc double[6];
        while (!span.IsEmpty)
        {
            var open = span.IndexOf('(');
            var close = span.IndexOf(')');
            if (open <= 0 || close < open)
                return AffineTransform.Identity;

            var name = span[..open].Trim();
            var list = span[(open + 1)..close].Trim();
            var count = 0;
            while (!list.IsEmpty)
            {
                if (count == arguments.Length || !TryReadNumber(ref list, out arguments[count]))
                    return AffineTransform.Identity;

                count++;
                SkipSeparators(ref list);
            }

            AffineTransform? item = name switch
            {
                "matrix" when count == 6 => new AffineTransform(arguments[0], arguments[1], arguments[2], arguments[3], arguments[4], arguments[5]),
                "translate" when count is 1 or 2 => AffineTransform.CreateTranslation(arguments[0], count == 2 ? arguments[1] : 0),
                "scale" when count is 1 or 2 => AffineTransform.CreateScale(arguments[0], count == 2 ? arguments[1] : arguments[0]),
                "rotate" when count == 1 => AffineTransform.CreateRotation(arguments[0]),
                "rotate" when count == 3 => AffineTransform.CreateRotation(arguments[0], new PointD(arguments[1], arguments[2])),
                "skewX" when count == 1 && Math.Abs(arguments[0] % 180) != 90 => AffineTransform.CreateSkew(NormalizeSkew(arguments[0]), 0),
                "skewY" when count == 1 && Math.Abs(arguments[0] % 180) != 90 => AffineTransform.CreateSkew(0, NormalizeSkew(arguments[0])),
                _ => null,
            };

            if (item is not { } transform)
                return AffineTransform.Identity;

            // "transform='A B'" is A applied to what B gives.
            result = transform.Then(result);
            span = span[(close + 1)..];
            SkipSeparators(ref span);
        }

        return result;

        // The tangent repeats every half turn: the angle is brought into (-90, 90).
        static double NormalizeSkew(double degrees)
        {
            var value = degrees % 180;
            return value switch
            {
                > 90 => value - 180,
                < -90 => value + 180,
                _ => value,
            };
        }
    }

    /// <summary>Reads a color: a keyword of SVG 1.1, <c>#rgb</c>, <c>#rrggbb</c> or <c>rgb()</c> with numbers or percentages.</summary>
    /// <returns><see langword="false"/> for a value that is not one of these colors (<c>currentColor</c> included).</returns>
    public static bool TryParseColor(ReadOnlySpan<char> text, out double red, out double green, out double blue)
    {
        red = green = blue = 0;
        text = text.Trim();
        if (text is ['#', .. var hex])
        {
            if (hex.Length is not (3 or 6) || !uint.TryParse(hex, NumberStyles.AllowHexSpecifier, CultureInfo.InvariantCulture, out var rgb))
                return false;

            if (hex.Length == 3)
            {
                rgb = ((rgb >> 8 & 0xF) * 0x110000) | ((rgb >> 4 & 0xF) * 0x1100) | ((rgb & 0xF) * 0x11);
            }

            SetComponents(rgb, out red, out green, out blue);
            return true;
        }

        if (text.StartsWith("rgb(", StringComparison.OrdinalIgnoreCase) && text is [.., ')'])
        {
            var list = text[4..^1].Trim();
            Span<double> components = stackalloc double[3];
            for (var i = 0; i < 3; i++)
            {
                if (!TryReadNumber(ref list, out var value))
                    return false;

                if (list is ['%', ..])
                {
                    value = value * 255 / 100;
                    list = list[1..];
                }

                components[i] = Math.Clamp(value, 0, 255) / 255;
                SkipSeparators(ref list);
            }

            red = components[0];
            green = components[1];
            blue = components[2];
            return list.IsEmpty;
        }

        var lookup = NamedColors.GetAlternateLookup<ReadOnlySpan<char>>();
        if (lookup.TryGetValue(text, out var named))
        {
            SetComponents(named, out red, out green, out blue);
            return true;
        }

        return false;

        static void SetComponents(uint rgb, out double red, out double green, out double blue)
        {
            red = (rgb >> 16 & 0xFF) / 255.0;
            green = (rgb >> 8 & 0xFF) / 255.0;
            blue = (rgb & 0xFF) / 255.0;
        }
    }

    // The color keywords of CSS (SVG 1.1, "Recognized color keyword names", and rebeccapurple), ASCII case-insensitive
    private static Dictionary<string, uint> CreateNamedColors() => new(StringComparer.OrdinalIgnoreCase)
    {
        ["aliceblue"] = 0xF0F8FF,
        ["antiquewhite"] = 0xFAEBD7,
        ["aqua"] = 0x00FFFF,
        ["aquamarine"] = 0x7FFFD4,
        ["azure"] = 0xF0FFFF,
        ["beige"] = 0xF5F5DC,
        ["bisque"] = 0xFFE4C4,
        ["black"] = 0x000000,
        ["blanchedalmond"] = 0xFFEBCD,
        ["blue"] = 0x0000FF,
        ["blueviolet"] = 0x8A2BE2,
        ["brown"] = 0xA52A2A,
        ["burlywood"] = 0xDEB887,
        ["cadetblue"] = 0x5F9EA0,
        ["chartreuse"] = 0x7FFF00,
        ["chocolate"] = 0xD2691E,
        ["coral"] = 0xFF7F50,
        ["cornflowerblue"] = 0x6495ED,
        ["cornsilk"] = 0xFFF8DC,
        ["crimson"] = 0xDC143C,
        ["cyan"] = 0x00FFFF,
        ["darkblue"] = 0x00008B,
        ["darkcyan"] = 0x008B8B,
        ["darkgoldenrod"] = 0xB8860B,
        ["darkgray"] = 0xA9A9A9,
        ["darkgreen"] = 0x006400,
        ["darkgrey"] = 0xA9A9A9,
        ["darkkhaki"] = 0xBDB76B,
        ["darkmagenta"] = 0x8B008B,
        ["darkolivegreen"] = 0x556B2F,
        ["darkorange"] = 0xFF8C00,
        ["darkorchid"] = 0x9932CC,
        ["darkred"] = 0x8B0000,
        ["darksalmon"] = 0xE9967A,
        ["darkseagreen"] = 0x8FBC8F,
        ["darkslateblue"] = 0x483D8B,
        ["darkslategray"] = 0x2F4F4F,
        ["darkslategrey"] = 0x2F4F4F,
        ["darkturquoise"] = 0x00CED1,
        ["darkviolet"] = 0x9400D3,
        ["deeppink"] = 0xFF1493,
        ["deepskyblue"] = 0x00BFFF,
        ["dimgray"] = 0x696969,
        ["dimgrey"] = 0x696969,
        ["dodgerblue"] = 0x1E90FF,
        ["firebrick"] = 0xB22222,
        ["floralwhite"] = 0xFFFAF0,
        ["forestgreen"] = 0x228B22,
        ["fuchsia"] = 0xFF00FF,
        ["gainsboro"] = 0xDCDCDC,
        ["ghostwhite"] = 0xF8F8FF,
        ["gold"] = 0xFFD700,
        ["goldenrod"] = 0xDAA520,
        ["gray"] = 0x808080,
        ["green"] = 0x008000,
        ["greenyellow"] = 0xADFF2F,
        ["grey"] = 0x808080,
        ["honeydew"] = 0xF0FFF0,
        ["hotpink"] = 0xFF69B4,
        ["indianred"] = 0xCD5C5C,
        ["indigo"] = 0x4B0082,
        ["ivory"] = 0xFFFFF0,
        ["khaki"] = 0xF0E68C,
        ["lavender"] = 0xE6E6FA,
        ["lavenderblush"] = 0xFFF0F5,
        ["lawngreen"] = 0x7CFC00,
        ["lemonchiffon"] = 0xFFFACD,
        ["lightblue"] = 0xADD8E6,
        ["lightcoral"] = 0xF08080,
        ["lightcyan"] = 0xE0FFFF,
        ["lightgoldenrodyellow"] = 0xFAFAD2,
        ["lightgray"] = 0xD3D3D3,
        ["lightgreen"] = 0x90EE90,
        ["lightgrey"] = 0xD3D3D3,
        ["lightpink"] = 0xFFB6C1,
        ["lightsalmon"] = 0xFFA07A,
        ["lightseagreen"] = 0x20B2AA,
        ["lightskyblue"] = 0x87CEFA,
        ["lightslategray"] = 0x778899,
        ["lightslategrey"] = 0x778899,
        ["lightsteelblue"] = 0xB0C4DE,
        ["lightyellow"] = 0xFFFFE0,
        ["lime"] = 0x00FF00,
        ["limegreen"] = 0x32CD32,
        ["linen"] = 0xFAF0E6,
        ["magenta"] = 0xFF00FF,
        ["maroon"] = 0x800000,
        ["mediumaquamarine"] = 0x66CDAA,
        ["mediumblue"] = 0x0000CD,
        ["mediumorchid"] = 0xBA55D3,
        ["mediumpurple"] = 0x9370DB,
        ["mediumseagreen"] = 0x3CB371,
        ["mediumslateblue"] = 0x7B68EE,
        ["mediumspringgreen"] = 0x00FA9A,
        ["mediumturquoise"] = 0x48D1CC,
        ["mediumvioletred"] = 0xC71585,
        ["midnightblue"] = 0x191970,
        ["mintcream"] = 0xF5FFFA,
        ["mistyrose"] = 0xFFE4E1,
        ["moccasin"] = 0xFFE4B5,
        ["navajowhite"] = 0xFFDEAD,
        ["navy"] = 0x000080,
        ["oldlace"] = 0xFDF5E6,
        ["olive"] = 0x808000,
        ["olivedrab"] = 0x6B8E23,
        ["orange"] = 0xFFA500,
        ["orangered"] = 0xFF4500,
        ["orchid"] = 0xDA70D6,
        ["palegoldenrod"] = 0xEEE8AA,
        ["palegreen"] = 0x98FB98,
        ["paleturquoise"] = 0xAFEEEE,
        ["palevioletred"] = 0xDB7093,
        ["papayawhip"] = 0xFFEFD5,
        ["peachpuff"] = 0xFFDAB9,
        ["peru"] = 0xCD853F,
        ["pink"] = 0xFFC0CB,
        ["plum"] = 0xDDA0DD,
        ["powderblue"] = 0xB0E0E6,
        ["purple"] = 0x800080,
        ["rebeccapurple"] = 0x663399,
        ["red"] = 0xFF0000,
        ["rosybrown"] = 0xBC8F8F,
        ["royalblue"] = 0x4169E1,
        ["saddlebrown"] = 0x8B4513,
        ["salmon"] = 0xFA8072,
        ["sandybrown"] = 0xF4A460,
        ["seagreen"] = 0x2E8B57,
        ["seashell"] = 0xFFF5EE,
        ["sienna"] = 0xA0522D,
        ["silver"] = 0xC0C0C0,
        ["skyblue"] = 0x87CEEB,
        ["slateblue"] = 0x6A5ACD,
        ["slategray"] = 0x708090,
        ["slategrey"] = 0x708090,
        ["snow"] = 0xFFFAFA,
        ["springgreen"] = 0x00FF7F,
        ["steelblue"] = 0x4682B4,
        ["tan"] = 0xD2B48C,
        ["teal"] = 0x008080,
        ["thistle"] = 0xD8BFD8,
        ["tomato"] = 0xFF6347,
        ["turquoise"] = 0x40E0D0,
        ["violet"] = 0xEE82EE,
        ["wheat"] = 0xF5DEB3,
        ["white"] = 0xFFFFFF,
        ["whitesmoke"] = 0xF5F5F5,
        ["yellow"] = 0xFFFF00,
        ["yellowgreen"] = 0x9ACD32,
    };
}
