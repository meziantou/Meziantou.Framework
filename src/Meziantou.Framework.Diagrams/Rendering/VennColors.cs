namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The colors of the sets of a Venn diagram (Mermaid <c>venn1</c> to <c>venn8</c>): those of the themes of Mermaid for its
/// themes, derived from the colors of the theme otherwise, then replaced by the colors of the diagram.
/// </summary>
internal sealed class VennColors
{
    private static readonly string[] DefaultColors = ["#5353ff", "#ffff45", "#b5ff20", "#ff53ff", "#53ffff", "#45ff45", "#ff5353", "#45ffff"];
    private static readonly string[] DarkColors = ["#6a6e6e", "#a40000", "#cc2a91", "#87a1a9", "#bf7344", "#eb2626", "#b800e1", "#feab35"];
    private static readonly string[] ForestColors = ["#8cb42f", "#6aff19", "#a6cf47", "#2fb457", "#b4572f", "#19ffae", "#2f8cb4", "#196aff"];
    private static readonly string[] NeutralColors = ["#555555", "#f4f4f4", "#555555", "#bbbbbb", "#777777", "#999999", "#dddddd", "#ffffff"];
    private static readonly string[] BaseColors = ["#ffc244", "#c244ff", "#5d92ff", "#80ff44", "#ff4480", "#ff4480", "#44ffc2", "#ffc244"];

    private readonly SceneColor[] _sets = new SceneColor[8];

    public VennColors(VennDiagram diagram, DiagramTheme theme)
    {
        var colors = theme switch
        {
            _ when ReferenceEquals(theme, DiagramTheme.Default) => DefaultColors,
            _ when ReferenceEquals(theme, DiagramTheme.Dark) => DarkColors,
            _ when ReferenceEquals(theme, DiagramTheme.Forest) => ForestColors,
            _ when ReferenceEquals(theme, DiagramTheme.Neutral) => NeutralColors,
            _ when ReferenceEquals(theme, DiagramTheme.Base) => BaseColors,
            _ => null,
        };

        for (var i = 0; i < 8; i++)
        {
            if (diagram.SetColors.TryGetValue(i, out var color))
            {
                _sets[i] = color;
            }
            else if (colors is not null)
            {
                _sets[i] = SceneColor.Parse(colors[i]);
            }
            else if (theme.IsDark && theme.Palette.Count > 0)
            {
                // The dark themes of Mermaid lighten the colors of the categories
                _sets[i] = AdjustLightness(theme.GetPaletteColor(i), 30);
            }
            else
            {
                // The other themes of Mermaid darken the main colors, turned around the color wheel
                _sets[i] = i switch
                {
                    0 => Adjust(theme.PrimaryColor, 0),
                    1 => Adjust(theme.SecondaryColor, 0),
                    2 => Adjust(theme.TertiaryColor, 0),
                    3 => Adjust(theme.PrimaryColor, 60),
                    4 => Adjust(theme.PrimaryColor, -60),
                    5 => Adjust(theme.SecondaryColor, 60),
                    6 => Adjust(theme.PrimaryColor, 120),
                    _ => Adjust(theme.SecondaryColor, 120),
                };
            }
        }

        TextColor = diagram.TextColor ?? theme.TextColor;
        IsDark = theme.Background is { } background ? Luminance(background) < 0.5 : theme.IsDark;

        static SceneColor Adjust(SceneColor color, double hue) => AdjustLightness(RotateHue(color, hue), -30);
    }

    /// <summary>Gets the color of the labels of the overlaps and of the texts (Mermaid <c>vennSetTextColor</c>).</summary>
    public SceneColor TextColor { get; }

    /// <summary>Gets a value indicating whether the background is dark, which lightens the labels of the sets instead of darkening them.</summary>
    public bool IsDark { get; }

    /// <summary>Returns the color of the n-th set.</summary>
    public SceneColor GetSetColor(int index) => _sets[index % 8];

    /// <summary>Returns the color of the label of a set: its color, 30% darker on a light background, or lighter on a dark one, as Mermaid does.</summary>
    public SceneColor GetLabelColor(SceneColor color) => AdjustLightness(color, IsDark ? 30 : -30) with { A = 255 };

    /// <summary>Adds percents to the lightness of a color, as khroma does.</summary>
    internal static SceneColor AdjustLightness(SceneColor color, double amount)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return SceneColor.FromHsl(hue, saturation, Math.Clamp(lightness + (amount / 100), 0, 1), color.A / 255.0);
    }

    private static SceneColor RotateHue(SceneColor color, double degrees)
    {
        var (hue, saturation, lightness) = ToHsl(color);
        return SceneColor.FromHsl(hue + degrees, saturation, lightness, color.A / 255.0);
    }

    /// <summary>Returns the relative luminance of a color (WCAG), as khroma <c>isDark</c> uses.</summary>
    private static double Luminance(SceneColor color) => (0.2126 * ToLinear(color.R)) + (0.7152 * ToLinear(color.G)) + (0.0722 * ToLinear(color.B));

    private static double ToLinear(byte channel)
    {
        var value = channel / 255.0;
        return value <= 0.03928 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);
    }

    private static (double Hue, double Saturation, double Lightness) ToHsl(SceneColor color)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        if (max == min)
            return (0, 0, lightness);

        var delta = max - min;
        var saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == r)
        {
            hue = ((g - b) / delta) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            hue = ((b - r) / delta) + 2;
        }
        else
        {
            hue = ((r - g) / delta) + 4;
        }

        return (hue * 60, saturation, lightness);
    }
}
