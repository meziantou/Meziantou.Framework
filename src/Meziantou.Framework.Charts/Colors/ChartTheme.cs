namespace Meziantou.Framework.Charts;

/// <summary>The colors, fonts and stroke widths of a chart.</summary>
public sealed record ChartTheme
{
    // The "Observable 10" categorical palette of d3-scale-chromatic (ISC license)
    private static readonly ChartColor[] DefaultPalette =
    [
        "#4269d0", "#efb118", "#ff725c", "#6cc5b0", "#3ca951", "#ff8ab7", "#a463f2", "#97bbf5", "#9c6b4e", "#9498a0",
    ];

    /// <summary>Gets a theme with dark text on a white background.</summary>
    public static ChartTheme Light { get; } = new();

    /// <summary>Gets a theme with light text on a dark background.</summary>
    public static ChartTheme Dark { get; } = new()
    {
        Background = "#1b1e23",
        Foreground = "#e6e6e6",
        MutedForeground = "#a7adb5",
        GridColor = "#3a3f47",
        AxisColor = "#7d848e",
        NeutralColor = "#8b939e",
        MarkStrokeColor = "#1b1e23",
    };

    /// <summary>Gets the color of the background, or <see langword="null"/> for a transparent background.</summary>
    public ChartColor? Background { get; init; } = ChartColor.White;

    /// <summary>Gets the color of the titles, the labels and the legend.</summary>
    public ChartColor Foreground { get; init; } = "#1f2328";

    /// <summary>Gets the color of the tick labels and of the subtitle.</summary>
    public ChartColor MutedForeground { get; init; } = "#59636e";

    /// <summary>Gets the color of the grid lines.</summary>
    public ChartColor GridColor { get; init; } = "#e3e6ea";

    /// <summary>Gets the color of the axis lines and of the ticks.</summary>
    public ChartColor AxisColor { get; init; } = "#8c959f";

    /// <summary>Gets the color of the outlines that separate adjacent shapes, such as the slices of a pie.</summary>
    public ChartColor MarkStrokeColor { get; init; } = ChartColor.White;

    /// <summary>Gets the colors of the categories, used in order.</summary>
    public IReadOnlyList<ChartColor> Palette { get; init; } = DefaultPalette;

    /// <summary>Gets the color of increases (waterfall, rising candlesticks).</summary>
    public ChartColor PositiveColor { get; init; } = "#3ca951";

    /// <summary>Gets the color of decreases (waterfall, falling candlesticks).</summary>
    public ChartColor NegativeColor { get; init; } = "#e5484d";

    /// <summary>Gets the color of totals and of neutral elements.</summary>
    public ChartColor NeutralColor { get; init; } = "#6e7781";

    /// <summary>Gets the font family, or <see langword="null"/> to use the family of the text measurer.</summary>
    public string? FontFamily { get; init; }

    /// <summary>Gets the size of the axis and legend labels.</summary>
    public double FontSize { get; init; } = 12;

    /// <summary>Gets the size of the tick labels.</summary>
    public double TickFontSize { get; init; } = 11;

    /// <summary>Gets the size of the title. The title is bold.</summary>
    public double TitleFontSize { get; init; } = 16;

    /// <summary>Gets the size of the subtitle.</summary>
    public double SubtitleFontSize { get; init; } = 12;

    /// <summary>Gets the stroke width of lines.</summary>
    public double LineWidth { get; init; } = 1.5;

    /// <summary>Gets the color of a category, by index in <see cref="Palette"/>, cycling when the index is past its end.</summary>
    /// <param name="index">The index of the category.</param>
    /// <returns>The color.</returns>
    public ChartColor GetPaletteColor(int index) => Palette.Count is 0 ? Foreground : Palette[((index % Palette.Count) + Palette.Count) % Palette.Count];
}
