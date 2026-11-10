using Meziantou.Framework.Charts;

namespace Meziantou.Framework.Mermaid.Internals.Charts;

/// <summary>Gives a chart the colors and the font of a theme of Mermaid.</summary>
internal static class MermaidChartTheme
{
    public static ChartTheme Create(DiagramTheme theme)
    {
        var chartTheme = theme.IsDark ? ChartTheme.Dark : ChartTheme.Light;
        return chartTheme with
        {
            Background = theme.Background,
            Foreground = theme.TextColor,
            AxisColor = theme.LineColor,
            Palette = theme.Palette.Count > 0 ? theme.Palette : chartTheme.Palette,
            FontFamily = theme.FontFamily,
        };
    }
}
