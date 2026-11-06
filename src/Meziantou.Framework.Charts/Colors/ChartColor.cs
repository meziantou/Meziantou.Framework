using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.Charts;

/// <summary>An sRGB color with an alpha channel, 8 bits per channel.</summary>
/// <param name="R">The red channel.</param>
/// <param name="G">The green channel.</param>
/// <param name="B">The blue channel.</param>
/// <param name="A">The alpha channel: 0 is transparent, 255 is opaque.</param>
[StructLayout(LayoutKind.Auto)]
public readonly record struct ChartColor(byte R, byte G, byte B, byte A = 255)
{
    private static readonly Dictionary<string, ChartColor> NamedColors = new(StringComparer.OrdinalIgnoreCase)
    {
        ["black"] = new(0, 0, 0),
        ["white"] = new(255, 255, 255),
        ["transparent"] = new(0, 0, 0, 0),
        ["red"] = new(255, 0, 0),
        ["green"] = new(0, 128, 0),
        ["blue"] = new(0, 0, 255),
        ["yellow"] = new(255, 255, 0),
        ["orange"] = new(255, 165, 0),
        ["purple"] = new(128, 0, 128),
        ["pink"] = new(255, 192, 203),
        ["brown"] = new(165, 42, 42),
        ["gray"] = new(128, 128, 128),
        ["grey"] = new(128, 128, 128),
        ["lightgray"] = new(211, 211, 211),
        ["lightgrey"] = new(211, 211, 211),
        ["darkgray"] = new(169, 169, 169),
        ["darkgrey"] = new(169, 169, 169),
        ["silver"] = new(192, 192, 192),
        ["navy"] = new(0, 0, 128),
        ["teal"] = new(0, 128, 128),
        ["olive"] = new(128, 128, 0),
        ["maroon"] = new(128, 0, 0),
        ["lime"] = new(0, 255, 0),
        ["aqua"] = new(0, 255, 255),
        ["cyan"] = new(0, 255, 255),
        ["fuchsia"] = new(255, 0, 255),
        ["magenta"] = new(255, 0, 255),
        ["steelblue"] = new(70, 130, 180),
        ["tomato"] = new(255, 99, 71),
        ["gold"] = new(255, 215, 0),
        ["crimson"] = new(220, 20, 60),
        ["seagreen"] = new(46, 139, 87),
        ["slategray"] = new(112, 128, 144),
        ["slategrey"] = new(112, 128, 144),
        ["indigo"] = new(75, 0, 130),
        ["coral"] = new(255, 127, 80),
        ["salmon"] = new(250, 128, 114),
        ["skyblue"] = new(135, 206, 235),
        ["royalblue"] = new(65, 105, 225),
        ["forestgreen"] = new(34, 139, 34),
        ["firebrick"] = new(178, 34, 34),
        ["darkorange"] = new(255, 140, 0),
        ["whitesmoke"] = new(245, 245, 245),
    };

    /// <summary>Gets the opaque black color.</summary>
    public static ChartColor Black => new(0, 0, 0);

    /// <summary>Gets the opaque white color.</summary>
    public static ChartColor White => new(255, 255, 255);

    /// <summary>Gets the transparent color.</summary>
    public static ChartColor Transparent => new(0, 0, 0, 0);

    /// <summary>Gets a value that indicates whether the color is fully opaque.</summary>
    public bool IsOpaque => A == 255;

    /// <summary>Gets the opacity of the color, from 0 to 1.</summary>
    public double Opacity => A / 255.0;

    /// <summary>Parses a color: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, or a common CSS color name such as <c>steelblue</c>.</summary>
    /// <param name="value">The text to parse.</param>
    /// <returns>The color.</returns>
    /// <exception cref="FormatException"><paramref name="value"/> is not a supported color.</exception>
    public static ChartColor Parse(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        if (TryParse(value, out var color))
            return color;

        throw new FormatException($"'{value}' is not a supported color. Use #rgb, #rrggbb, #rrggbbaa or a CSS color name.");
    }

    /// <summary>Tries to parse a color: <c>#rgb</c>, <c>#rgba</c>, <c>#rrggbb</c>, <c>#rrggbbaa</c>, or a common CSS color name.</summary>
    /// <param name="value">The text to parse.</param>
    /// <param name="color">The color, when the method returns <see langword="true"/>.</param>
    /// <returns><see langword="true"/> when <paramref name="value"/> is a supported color.</returns>
    public static bool TryParse([NotNullWhen(true)] string? value, out ChartColor color)
    {
        color = default;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.AsSpan().Trim();
        if (text[0] is not '#')
            return NamedColors.TryGetValue(text.ToString(), out color);

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

                    color = new ChartColor((byte)(channels[0] * 17), (byte)(channels[1] * 17), (byte)(channels[2] * 17), (byte)(channels[3] * 17));
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

                    color = new ChartColor(channels[0], channels[1], channels[2], channels[3]);
                    return true;
                }

            default:
                return false;
        }
    }

    /// <summary>Converts a text to a color with <see cref="Parse(string)"/>.</summary>
    /// <param name="value">The text to parse.</param>
    public static implicit operator ChartColor(string value) => Parse(value);

    /// <summary>Returns a copy of the color with another opacity.</summary>
    /// <param name="opacity">The opacity, from 0 to 1.</param>
    /// <returns>The color.</returns>
    public ChartColor WithOpacity(double opacity) => this with { A = (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255, MidpointRounding.AwayFromZero) };

    /// <summary>Returns the color as <c>#rrggbb</c>, without its alpha channel.</summary>
    /// <returns>The hexadecimal notation of the color.</returns>
    public string ToHexRgb() => string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}");

    /// <summary>Returns the color as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is not opaque.</summary>
    /// <returns>The hexadecimal notation of the color.</returns>
    public override string ToString() => IsOpaque ? ToHexRgb() : string.Create(CultureInfo.InvariantCulture, $"#{R:x2}{G:x2}{B:x2}{A:x2}");

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
