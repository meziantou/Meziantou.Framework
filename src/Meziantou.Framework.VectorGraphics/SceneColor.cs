using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.VectorGraphics;

/// <summary>An sRGB color with an alpha channel, 8 bits per channel.</summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <param name="A">The alpha channel: 0 is transparent, 255 is opaque.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct SceneColor(byte R, byte G, byte B, byte A = 255)
{
    // The named colors of CSS Color 4
    private static readonly Dictionary<string, SceneColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["transparent"] = new(0, 0, 0, 0),
        ["aliceblue"] = new(240, 248, 255),
        ["antiquewhite"] = new(250, 235, 215),
        ["aqua"] = new(0, 255, 255),
        ["aquamarine"] = new(127, 255, 212),
        ["azure"] = new(240, 255, 255),
        ["beige"] = new(245, 245, 220),
        ["bisque"] = new(255, 228, 196),
        ["black"] = new(0, 0, 0),
        ["blanchedalmond"] = new(255, 235, 205),
        ["blue"] = new(0, 0, 255),
        ["blueviolet"] = new(138, 43, 226),
        ["brown"] = new(165, 42, 42),
        ["burlywood"] = new(222, 184, 135),
        ["cadetblue"] = new(95, 158, 160),
        ["chartreuse"] = new(127, 255, 0),
        ["chocolate"] = new(210, 105, 30),
        ["coral"] = new(255, 127, 80),
        ["cornflowerblue"] = new(100, 149, 237),
        ["cornsilk"] = new(255, 248, 220),
        ["crimson"] = new(220, 20, 60),
        ["cyan"] = new(0, 255, 255),
        ["darkblue"] = new(0, 0, 139),
        ["darkcyan"] = new(0, 139, 139),
        ["darkgoldenrod"] = new(184, 134, 11),
        ["darkgray"] = new(169, 169, 169),
        ["darkgreen"] = new(0, 100, 0),
        ["darkgrey"] = new(169, 169, 169),
        ["darkkhaki"] = new(189, 183, 107),
        ["darkmagenta"] = new(139, 0, 139),
        ["darkolivegreen"] = new(85, 107, 47),
        ["darkorange"] = new(255, 140, 0),
        ["darkorchid"] = new(153, 50, 204),
        ["darkred"] = new(139, 0, 0),
        ["darksalmon"] = new(233, 150, 122),
        ["darkseagreen"] = new(143, 188, 143),
        ["darkslateblue"] = new(72, 61, 139),
        ["darkslategray"] = new(47, 79, 79),
        ["darkslategrey"] = new(47, 79, 79),
        ["darkturquoise"] = new(0, 206, 209),
        ["darkviolet"] = new(148, 0, 211),
        ["deeppink"] = new(255, 20, 147),
        ["deepskyblue"] = new(0, 191, 255),
        ["dimgray"] = new(105, 105, 105),
        ["dimgrey"] = new(105, 105, 105),
        ["dodgerblue"] = new(30, 144, 255),
        ["firebrick"] = new(178, 34, 34),
        ["floralwhite"] = new(255, 250, 240),
        ["forestgreen"] = new(34, 139, 34),
        ["fuchsia"] = new(255, 0, 255),
        ["gainsboro"] = new(220, 220, 220),
        ["ghostwhite"] = new(248, 248, 255),
        ["gold"] = new(255, 215, 0),
        ["goldenrod"] = new(218, 165, 32),
        ["gray"] = new(128, 128, 128),
        ["green"] = new(0, 128, 0),
        ["greenyellow"] = new(173, 255, 47),
        ["grey"] = new(128, 128, 128),
        ["honeydew"] = new(240, 255, 240),
        ["hotpink"] = new(255, 105, 180),
        ["indianred"] = new(205, 92, 92),
        ["indigo"] = new(75, 0, 130),
        ["ivory"] = new(255, 255, 240),
        ["khaki"] = new(240, 230, 140),
        ["lavender"] = new(230, 230, 250),
        ["lavenderblush"] = new(255, 240, 245),
        ["lawngreen"] = new(124, 252, 0),
        ["lemonchiffon"] = new(255, 250, 205),
        ["lightblue"] = new(173, 216, 230),
        ["lightcoral"] = new(240, 128, 128),
        ["lightcyan"] = new(224, 255, 255),
        ["lightgoldenrodyellow"] = new(250, 250, 210),
        ["lightgray"] = new(211, 211, 211),
        ["lightgreen"] = new(144, 238, 144),
        ["lightgrey"] = new(211, 211, 211),
        ["lightpink"] = new(255, 182, 193),
        ["lightsalmon"] = new(255, 160, 122),
        ["lightseagreen"] = new(32, 178, 170),
        ["lightskyblue"] = new(135, 206, 250),
        ["lightslategray"] = new(119, 136, 153),
        ["lightslategrey"] = new(119, 136, 153),
        ["lightsteelblue"] = new(176, 196, 222),
        ["lightyellow"] = new(255, 255, 224),
        ["lime"] = new(0, 255, 0),
        ["limegreen"] = new(50, 205, 50),
        ["linen"] = new(250, 240, 230),
        ["magenta"] = new(255, 0, 255),
        ["maroon"] = new(128, 0, 0),
        ["mediumaquamarine"] = new(102, 205, 170),
        ["mediumblue"] = new(0, 0, 205),
        ["mediumorchid"] = new(186, 85, 211),
        ["mediumpurple"] = new(147, 112, 219),
        ["mediumseagreen"] = new(60, 179, 113),
        ["mediumslateblue"] = new(123, 104, 238),
        ["mediumspringgreen"] = new(0, 250, 154),
        ["mediumturquoise"] = new(72, 209, 204),
        ["mediumvioletred"] = new(199, 21, 133),
        ["midnightblue"] = new(25, 25, 112),
        ["mintcream"] = new(245, 255, 250),
        ["mistyrose"] = new(255, 228, 225),
        ["moccasin"] = new(255, 228, 181),
        ["navajowhite"] = new(255, 222, 173),
        ["navy"] = new(0, 0, 128),
        ["oldlace"] = new(253, 245, 230),
        ["olive"] = new(128, 128, 0),
        ["olivedrab"] = new(107, 142, 35),
        ["orange"] = new(255, 165, 0),
        ["orangered"] = new(255, 69, 0),
        ["orchid"] = new(218, 112, 214),
        ["palegoldenrod"] = new(238, 232, 170),
        ["palegreen"] = new(152, 251, 152),
        ["paleturquoise"] = new(175, 238, 238),
        ["palevioletred"] = new(219, 112, 147),
        ["papayawhip"] = new(255, 239, 213),
        ["peachpuff"] = new(255, 218, 185),
        ["peru"] = new(205, 133, 63),
        ["pink"] = new(255, 192, 203),
        ["plum"] = new(221, 160, 221),
        ["powderblue"] = new(176, 224, 230),
        ["purple"] = new(128, 0, 128),
        ["rebeccapurple"] = new(102, 51, 153),
        ["red"] = new(255, 0, 0),
        ["rosybrown"] = new(188, 143, 143),
        ["royalblue"] = new(65, 105, 225),
        ["saddlebrown"] = new(139, 69, 19),
        ["salmon"] = new(250, 128, 114),
        ["sandybrown"] = new(244, 164, 96),
        ["seagreen"] = new(46, 139, 87),
        ["seashell"] = new(255, 245, 238),
        ["sienna"] = new(160, 82, 45),
        ["silver"] = new(192, 192, 192),
        ["skyblue"] = new(135, 206, 235),
        ["slateblue"] = new(106, 90, 205),
        ["slategray"] = new(112, 128, 144),
        ["slategrey"] = new(112, 128, 144),
        ["snow"] = new(255, 250, 250),
        ["springgreen"] = new(0, 255, 127),
        ["steelblue"] = new(70, 130, 180),
        ["tan"] = new(210, 180, 140),
        ["teal"] = new(0, 128, 128),
        ["thistle"] = new(216, 191, 216),
        ["tomato"] = new(255, 99, 71),
        ["turquoise"] = new(64, 224, 208),
        ["violet"] = new(238, 130, 238),
        ["wheat"] = new(245, 222, 179),
        ["white"] = new(255, 255, 255),
        ["whitesmoke"] = new(245, 245, 245),
        ["yellow"] = new(255, 255, 0),
        ["yellowgreen"] = new(154, 205, 50),
    };

    /// <summary>Gets the opaque black color.</summary>
    public static SceneColor Black => new(0, 0, 0);

    /// <summary>Gets the opaque white color.</summary>
    public static SceneColor White => new(255, 255, 255);

    /// <summary>Gets the transparent color.</summary>
    public static SceneColor Transparent => new(0, 0, 0, 0);

    /// <summary>Gets a value that indicates whether the color is fully opaque.</summary>
    public bool IsOpaque => A == 255;

    /// <summary>Gets the opacity of the color, from 0 to 1.</summary>
    public double Opacity => A / 255.0;

    /// <summary>
    /// Parses a CSS color: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, a CSS color name such as
    /// <c>steelblue</c>, or the functions <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c> and <c>hsla()</c>, with commas or spaces.
    /// </summary>
    /// <param name="value">The text to parse.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a supported color.</exception>
    public static SceneColor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (TryParse(value, out var color))
            return color;

        throw new FormatException($"'{value}' is not a supported color. Use #rgb, #rrggbb, #rrggbbaa, rgb(), hsl() or a CSS color name.");
    }

    /// <summary>Tries to parse a CSS color: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, a CSS color name, or <c>rgb()</c>, <c>rgba()</c>, <c>hsl()</c> and <c>hsla()</c>.</summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="color">The color, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a supported color.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out SceneColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.AsSpan().Trim();
        if (text[0] is not '#')
        {
            if (TryParseFunction(text, out color))
                return true;

            return NamedColors.TryGetValue(text.ToString(), out color);
        }

        text = text[1..];
        switch (text.Length)
        {
            case 3 or 4:
                {
                    Span<byte> channels = [0, 0, 0, 15];
                    for (var i = 0; i < text.Length; i++)
                    {
                        if (!TryParseHexDigit(text[i], out var digit))
                            return false;

                        channels[i] = digit;
                    }

                    color = new SceneColor((byte)(channels[0] * 17), (byte)(channels[1] * 17), (byte)(channels[2] * 17), (byte)(channels[3] * 17));
                    return true;
                }

            case 6 or 8:
                {
                    // Not uint.TryParse: it accepts trailing NUL characters
                    Span<byte> channels = [0, 0, 0, 255];
                    for (var i = 0; i < text.Length; i += 2)
                    {
                        if (!TryParseHexDigit(text[i], out var high) || !TryParseHexDigit(text[i + 1], out var low))
                            return false;

                        channels[i / 2] = (byte)((high << 4) | low);
                    }

                    color = new SceneColor(channels[0], channels[1], channels[2], channels[3]);
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>Converts a text to a color with <see cref="Parse(string)"/>.</summary>
    /// <param name="value">The text to parse.</param>
    public static implicit operator SceneColor(string value) => Parse(value);

    /// <summary>Returns a copy of the color with another opacity.</summary>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
    /// <returns>The color.</returns>
    public SceneColor WithOpacity(double opacity) => this with { A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero) };

    /// <summary>Returns the color as <c>#rrggbb</c>, without its alpha channel.</summary>
    /// <returns>The hexadecimal notation of the color.</returns>
    public string ToHexRgb() => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

    /// <summary>Returns the color as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is not opaque.</summary>
    /// <returns>The hexadecimal notation of the color.</returns>
    public override string ToString() => IsOpaque ? ToHexRgb() : string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}{A:x2}");

    /// <summary>Parses <c>rgb(r, g, b)</c>, <c>rgb(r g b / a)</c>, <c>hsl(h, s%, l%)</c> and their <c>a</c> variants.</summary>
    private static bool TryParseFunction(ReadOnlySpan<char> text, out SceneColor color)
    {
        color = default;
        var open = text.IndexOf('(');
        if (open <= 0 || text[^1] is not ')')
            return false;

        var name = text[..open].Trim();
        var isRgb = name.Equals("rgb", StringComparison.OrdinalIgnoreCase) || name.Equals("rgba", StringComparison.OrdinalIgnoreCase);
        var isHsl = name.Equals("hsl", StringComparison.OrdinalIgnoreCase) || name.Equals("hsla", StringComparison.OrdinalIgnoreCase);
        if (!isRgb && !isHsl)
            return false;

        // The arguments: separated by commas, or by spaces with a slash before the alpha
        Span<double> values = stackalloc double[4];
        Span<bool> percents = stackalloc bool[4];
        var count = 0;
        var arguments = text[(open + 1)..^1];
        var hasComma = arguments.Contains(',');
        while (true)
        {
            arguments = arguments.TrimStart();
            if (arguments.IsEmpty)
                break;

            if (count == 3 && !hasComma)
            {
                if (arguments[0] is not '/')
                    return false;

                arguments = arguments[1..].TrimStart();
            }

            var end = 0;
            while (end < arguments.Length && arguments[end] is not (',' or ' ' or '/' or '\t'))
            {
                end++;
            }

            if (end == 0 || count == 4)
                return false;

            var token = arguments[..end];
            arguments = arguments[end..].TrimStart();
            if (hasComma && count < 3)
            {
                if (arguments.IsEmpty || arguments[0] is not ',')
                {
                    if (!(count == 2 && arguments.IsEmpty))
                        return false;
                }
                else
                {
                    arguments = arguments[1..];
                }
            }
            else if (hasComma && count == 3 && !arguments.IsEmpty)
            {
                return false;
            }

            percents[count] = token[^1] is '%';
            if (percents[count])
            {
                token = token[..^1];
            }
            else if (isHsl && count == 0 && token.EndsWith("deg", StringComparison.OrdinalIgnoreCase))
            {
                token = token[..^3];
            }

            if (!double.TryParse(token, System.Globalization.NumberStyles.Float, CultureInfo.InvariantCulture, out values[count]) || !double.IsFinite(values[count]))
                return false;

            count++;
        }

        if (count is not (3 or 4))
            return false;

        var alpha = count == 4 ? (percents[3] ? values[3] / 100 : values[3]) : 1;
        alpha = Math.Clamp(alpha, 0, 1);
        double r;
        double g;
        double b;
        if (isRgb)
        {
            r = percents[0] ? values[0] * 255 / 100 : values[0];
            g = percents[1] ? values[1] * 255 / 100 : values[1];
            b = percents[2] ? values[2] * 255 / 100 : values[2];
        }
        else
        {
            var hue = values[0] % 360;
            if (hue < 0)
            {
                hue += 360;
            }

            var saturation = Math.Clamp(values[1] / 100, 0, 1);
            var lightness = Math.Clamp(values[2] / 100, 0, 1);
            (r, g, b) = HslToRgb(hue, saturation, lightness);
        }

        color = new SceneColor(ToByte(r), ToByte(g), ToByte(b), ToByte(alpha * 255));
        return true;

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    /// <summary>Creates a color from its hue, saturation and lightness, as the CSS function <c>hsl()</c>.</summary>
    /// <param name="hue">The hue, in degrees; it wraps around 360.</param>
    /// <param name="saturation">The saturation, from 0 to 1.</param>
    /// <param name="lightness">The lightness, from 0 to 1.</param>
    /// <param name="alpha">The opacity, from 0 to 1.</param>
    /// <returns>The color, its channels rounded to the nearest integer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">A value is not finite.</exception>
    public static SceneColor FromHsl(double hue, double saturation, double lightness, double alpha = 1)
    {
        if (!double.IsFinite(hue))
            throw new ArgumentOutOfRangeException(nameof(hue), hue, "The value must be a finite number.");

        if (!double.IsFinite(saturation))
            throw new ArgumentOutOfRangeException(nameof(saturation), saturation, "The value must be a finite number.");

        if (!double.IsFinite(lightness))
            throw new ArgumentOutOfRangeException(nameof(lightness), lightness, "The value must be a finite number.");

        if (!double.IsFinite(alpha))
            throw new ArgumentOutOfRangeException(nameof(alpha), alpha, "The value must be a finite number.");

        hue %= 360;
        if (hue < 0)
        {
            hue += 360;
        }

        var (r, g, b) = HslToRgb(hue, Math.Clamp(saturation, 0, 1), Math.Clamp(lightness, 0, 1));
        return new SceneColor(ToByte(r), ToByte(g), ToByte(b), ToByte(Math.Clamp(alpha, 0, 1) * 255));

        static byte ToByte(double value) => (byte)Math.Clamp(Math.Round(value, MidpointRounding.AwayFromZero), 0, 255);
    }

    private static (double R, double G, double B) HslToRgb(double hue, double saturation, double lightness)
    {
        var chroma = (1 - Math.Abs((2 * lightness) - 1)) * saturation;
        var h = hue / 60;
        var x = chroma * (1 - Math.Abs((h % 2) - 1));
        var (r, g, b) = h switch
        {
            < 1 => (chroma, x, 0.0),
            < 2 => (x, chroma, 0.0),
            < 3 => (0.0, chroma, x),
            < 4 => (0.0, x, chroma),
            < 5 => (x, 0.0, chroma),
            _ => (chroma, 0.0, x),
        };
        var m = lightness - (chroma / 2);
        return ((r + m) * 255, (g + m) * 255, (b + m) * 255);
    }

    private static bool TryParseHexDigit(char c, out byte digit)
    {
        digit = c switch
        {
            >= '0' and <= '9' => (byte)(c - '0'),
            >= 'a' and <= 'f' => (byte)(c - 'a' + 10),
            >= 'A' and <= 'F' => (byte)(c - 'A' + 10),
            _ => 255,
        };
        return digit != 255;
    }
}
