namespace Meziantou.Framework.Mermaid.Internals.PieDiagrams;

/// <summary>
/// The colors of a pie chart: those of the themes of Mermaid for its themes, derived from the primary, secondary and
/// tertiary colors of the theme as the base theme of Mermaid does otherwise, then replaced by the colors of the chart.
/// </summary>
internal sealed class PieChartColors
{
    public const int SliceColorCount = 12;

    // pie1...pie12, then the colors of the percentages and of the legend
    private static readonly string?[] DefaultColors = ["#ececff", "#ffffde", "#b5ff20", "#b9b9ff", "#ffff45", "#d7ff86", "#ff86ff", "#20ffff", "#ff2020", "#ff20ff", "#20ff8f", "#ff5353", "#333333", "#000000"];

    // The dark theme of Mermaid has no pie12: the twelfth slice has no fill color, so the browser fills it in black
    private static readonly string?[] DarkColors = ["#0b0000", "#4d1037", "#3f5258", "#4f2f1b", "#6e0a0a", "#3b0048", "#995a01", "#154706", "#161722", "#00296f", "#01629c", null, "#cccccc", "#d3d3d3"];

    private static readonly string?[] ForestColors = ["#cde498", "#cdffb2", "#e1efc0", "#8cb42f", "#6aff19", "#33b42f", "#70d990", "#d99070", "#98cde4", "#1a6330", "#63301a", "#1a4d63", "#000000", "#000000"];

    private static readonly string?[] NeutralColors = ["#f4f4f4", "#555555", "#bbbbbb", "#777777", "#999999", "#dddddd", "#ffffff", "#dddddd", "#bbbbbb", "#999999", "#777777", "#555555", "#000000", "#333333"];

    private static readonly string?[] BaseColors = ["#fff4dd", "#f4ddff", "#f6f9ff", "#ffe3aa", "#e3aaff", "#c3d7ff", "#c5ffaa", "#ffaac5", "#ddfff4", "#a3ff77", "#ff77a3", "#aaffe3", "#333333", "#333333"];

    private readonly SceneColor[] _slices = new SceneColor[SliceColorCount];

    public PieChartColors(PieChart chart)
    {
        var theme = chart.Theme;
        var colors = theme switch
        {
            _ when ReferenceEquals(theme, DiagramTheme.Default) => DefaultColors,
            _ when ReferenceEquals(theme, DiagramTheme.Dark) => DarkColors,
            _ when ReferenceEquals(theme, DiagramTheme.Forest) => ForestColors,
            _ when ReferenceEquals(theme, DiagramTheme.Neutral) => NeutralColors,
            _ when ReferenceEquals(theme, DiagramTheme.Base) => BaseColors,
            _ => null,
        };

        for (var i = 0; i < SliceColorCount; i++)
        {
            if (chart.SliceColors.TryGetValue(i, out var color))
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

        SectionText = chart.SectionTextColor ?? (colors is null ? theme.TextColor : SceneColor.Parse(colors[12]!));
        LegendText = chart.LegendTextColor ?? (colors is null ? theme.TextColor : SceneColor.Parse(colors[13]!));
    }

    /// <summary>Gets the color of the percentages.</summary>
    public SceneColor SectionText { get; }

    /// <summary>Gets the color of the texts of the legend.</summary>
    public SceneColor LegendText { get; }

    /// <summary>Gets the color of the slice at a position: the colors repeat every 12 slices.</summary>
    public SceneColor Slice(int index) => _slices[((index % SliceColorCount) + SliceColorCount) % SliceColorCount];

    // The formulas of the base theme of Mermaid
    private static SceneColor Derive(DiagramTheme theme, int index) => index switch
    {
        0 => theme.PrimaryColor,
        1 => theme.SecondaryColor,
        2 => theme.TertiaryColor,
        3 => ColorMath.Adjust(theme.PrimaryColor, lightness: -10),
        4 => ColorMath.Adjust(theme.SecondaryColor, lightness: -10),
        5 => ColorMath.Adjust(theme.TertiaryColor, lightness: -10),
        6 => ColorMath.Adjust(theme.PrimaryColor, hue: 60, lightness: -10),
        7 => ColorMath.Adjust(theme.PrimaryColor, hue: -60, lightness: -10),
        8 => ColorMath.Adjust(theme.PrimaryColor, hue: 120),
        9 => ColorMath.Adjust(theme.PrimaryColor, hue: 60, lightness: -20),
        10 => ColorMath.Adjust(theme.PrimaryColor, hue: -60, lightness: -20),
        _ => ColorMath.Adjust(theme.PrimaryColor, hue: 120, lightness: -10),
    };
}
