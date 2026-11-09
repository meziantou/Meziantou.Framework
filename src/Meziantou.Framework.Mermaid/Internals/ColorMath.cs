namespace Meziantou.Framework.Mermaid.Internals;

/// <summary>The color functions of khroma, which the themes of Mermaid derive their colors with.</summary>
internal static class ColorMath
{
    /// <summary>Changes the hue (in degrees), the saturation and the lightness (in percentage points) of a color.</summary>
    public static SceneColor Adjust(SceneColor color, double hue = 0, double saturation = 0, double lightness = 0)
    {
        var (h, s, l) = ToHsl(color);
        h = (h + hue) % 360;
        if (h < 0)
        {
            h += 360;
        }

        s = Math.Clamp(s + saturation, 0, 100);
        l = Math.Clamp(l + lightness, 0, 100);
        return FromHsl(h, s, l, color.A);
    }

    public static SceneColor Invert(SceneColor color) => new((byte)(255 - color.R), (byte)(255 - color.G), (byte)(255 - color.B), color.A);

    /// <summary>The border of a color in Mermaid: less saturated, darker in a light theme and lighter in a dark one.</summary>
    public static SceneColor MakeBorder(SceneColor color, bool darkMode) => Adjust(color, saturation: -40, lightness: darkMode ? 10 : -10);

    private static (double H, double S, double L) ToHsl(SceneColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
            return (0, 0, l * 100);

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h * 60, s * 100, l * 100);
    }

    private static SceneColor FromHsl(double h, double s, double l, byte alpha) => SceneColor.FromHsl(h, s / 100, l / 100) with { A = alpha };
}
