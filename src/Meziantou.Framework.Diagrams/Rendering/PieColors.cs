namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The colors of a pie chart: those of the themes of Mermaid for its themes, derived from the primary, secondary and
/// tertiary colors of the theme as the base theme of Mermaid does otherwise, then replaced by the colors of the diagram.
/// </summary>
internal sealed class PieColors
{
    // pie1...pie12, then the colors of the percentages and of the legend
    private static readonly string?[] DefaultColors = ["#ececff", "#ffffde", "#b5ff20", "#b9b9ff", "#ffff45", "#d7ff86", "#ff86ff", "#20ffff", "#ff2020", "#ff20ff", "#20ff8f", "#ff5353", "#333333", "#000000"];

    // The dark theme of Mermaid has no pie12: the twelfth slice has no fill color, so the browser fills it in black
    private static readonly string?[] DarkColors = ["#0b0000", "#4d1037", "#3f5258", "#4f2f1b", "#6e0a0a", "#3b0048", "#995a01", "#154706", "#161722", "#00296f", "#01629c", null, "#cccccc", "#d3d3d3"];

    private static readonly string?[] ForestColors = ["#cde498", "#cdffb2", "#e1efc0", "#8cb42f", "#6aff19", "#33b42f", "#70d990", "#d99070", "#98cde4", "#1a6330", "#63301a", "#1a4d63", "#000000", "#000000"];

    private static readonly string?[] NeutralColors = ["#f4f4f4", "#555555", "#bbbbbb", "#777777", "#999999", "#dddddd", "#ffffff", "#dddddd", "#bbbbbb", "#999999", "#777777", "#555555", "#000000", "#333333"];

    private static readonly string?[] BaseColors = ["#fff4dd", "#f4ddff", "#f6f9ff", "#ffe3aa", "#e3aaff", "#c3d7ff", "#c5ffaa", "#ffaac5", "#ddfff4", "#a3ff77", "#ff77a3", "#aaffe3", "#333333", "#333333"];

    private readonly SceneColor[] _slices = new SceneColor[12];

    public PieColors(PieDiagram diagram, DiagramTheme theme)
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

        for (var i = 0; i < 12; i++)
        {
            if (diagram.SliceColors.TryGetValue(i, out var color))
            {
                _slices[i] = color;
            }
            else if (colors is not null)
            {
                _slices[i] = colors[i] is { } value ? SceneColor.Parse(value) : SceneColor.Black;
            }
            else
            {
                _slices[i] = Derive(theme, i);
            }
        }

        SectionText = diagram.SectionTextColor ?? (colors is null ? theme.TextColor : SceneColor.Parse(colors[12]!));
        LegendText = diagram.LegendTextColor ?? (colors is null ? theme.TextColor : SceneColor.Parse(colors[13]!));
    }

    public SceneColor SectionText { get; }

    public SceneColor LegendText { get; }

    public SceneColor Slice(int index) => _slices[((index % 12) + 12) % 12];

    // The formulas of the base theme of Mermaid
    private static SceneColor Derive(DiagramTheme theme, int index) => index switch
    {
        0 => theme.PrimaryColor,
        1 => theme.SecondaryColor,
        2 => theme.TertiaryColor,
        3 => Adjust(theme.PrimaryColor, 0, -10),
        4 => Adjust(theme.SecondaryColor, 0, -10),
        5 => Adjust(theme.TertiaryColor, 0, -10),
        6 => Adjust(theme.PrimaryColor, 60, -10),
        7 => Adjust(theme.PrimaryColor, -60, -10),
        8 => Adjust(theme.PrimaryColor, 120, 0),
        9 => Adjust(theme.PrimaryColor, 60, -20),
        10 => Adjust(theme.PrimaryColor, -60, -20),
        _ => Adjust(theme.PrimaryColor, 120, -10),
    };

    /// <summary>Changes the hue (in degrees) and the lightness (in percents) of a color, as the <c>adjust</c> function of khroma.</summary>
    internal static SceneColor Adjust(SceneColor color, double hue, double lightness)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        double h = 0;
        double s = 0;
        var delta = max - min;
        if (delta > 0)
        {
            s = l > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            if (max == r)
            {
                h = ((g - b) / delta) + (g < b ? 6 : 0);
            }
            else if (max == g)
            {
                h = ((b - r) / delta) + 2;
            }
            else
            {
                h = ((r - g) / delta) + 4;
            }

            h *= 60;
        }

        return SceneColor.FromHsl(h + hue, s, Math.Clamp(l + (lightness / 100), 0, 1), color.Opacity);
    }
}
