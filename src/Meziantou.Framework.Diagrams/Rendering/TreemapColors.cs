namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>
/// The colors of the nodes of a treemap, as the ordinal scales of Mermaid: each new name takes the next color, in the
/// order Mermaid asks for them. The fill scale starts with a transparent color, which the root takes.
/// </summary>
internal sealed class TreemapColors
{
    private readonly DiagramTheme _theme;
    private readonly SceneColor[] _peers;
    private readonly SceneColor[] _labels;
    private readonly Dictionary<string, int> _fillIndexes = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _labelIndexes = new(StringComparer.Ordinal);

    public TreemapColors(DiagramTheme theme)
    {
        _theme = theme;
        (_peers, _labels) = GetPeerAndLabelColors(theme);
    }

    /// <summary>Returns the fill of the nodes of a section (Mermaid <c>cScale</c>, after a transparent color).</summary>
    public SceneColor GetFill(string name)
    {
        var index = GetIndex(_fillIndexes, name) % 13;
        return index is 0 ? SceneColor.Transparent : _theme.GetPaletteColor(index - 1);
    }

    /// <summary>Returns the border of a section (Mermaid <c>cScalePeer</c>, after a transparent color).</summary>
    public SceneColor GetPeer(string name)
    {
        var index = GetIndex(_fillIndexes, name) % 13;
        return index is 0 ? SceneColor.Transparent : _peers[(index - 1) % _peers.Length];
    }

    /// <summary>Returns the color of the texts of a node (Mermaid <c>cScaleLabel</c>).</summary>
    public SceneColor GetLabel(string name) => _labels[GetIndex(_labelIndexes, name) % _labels.Length];

    private static int GetIndex(Dictionary<string, int> indexes, string name)
    {
        if (!indexes.TryGetValue(name, out var index))
        {
            index = indexes.Count;
            indexes.Add(name, index);
        }

        return index;
    }

    private static (SceneColor[] Peers, SceneColor[] Labels) GetPeerAndLabelColors(DiagramTheme theme)
    {
        if (ReferenceEquals(theme, DiagramTheme.Default))
            return (Parse("#3939ff", "#f7f700", "#b5ff20", "#9c39ff", "#ff39ff", "#ff399c", "#ff3939", "#ff9c39", "#9cff39", "#39ff9c", "#39ffff", "#399cff"), Parse("#ffffff", "#000000", "#000000", "#ffffff", "#000000", "#000000", "#000000", "#000000", "#000000", "#000000", "#000000", "#000000"));

        if (ReferenceEquals(theme, DiagramTheme.Dark))
            return (Parse("#383a3a", "#3e0000", "#771955", "#546e76", "#754628", "#9d0e0e", "#65007b", "#cc7801", "#23760a", "#2a2c41", "#003ca2", "#0182cf"), Parse("#d3d3d3"));

        if (ReferenceEquals(theme, DiagramTheme.Forest))
            return (Parse("#7ca02a", "#47cb00", "#8cb42f", "#41a02a", "#2aa04d", "#2aa088", "#2a7ca0", "#2a41a0", "#882aa0", "#a02a41", "#a04d2a", "#a0882a"), Parse("#000000"));

        if (ReferenceEquals(theme, DiagramTheme.Neutral))
            return (Parse("#3b3b3b", "#dadada", "#3b3b3b", "#a1a1a1", "#5e5e5e", "#808080", "#c4c4c4", "#e6e6e6", "#c4c4c4", "#a1a1a1", "#808080", "#5e5e5e"), Parse("#f4f4f4", "#333333", "#f4f4f4", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333", "#333333"));

        if (ReferenceEquals(theme, DiagramTheme.Base))
            return (Parse("#ffba2a", "#ba2aff", "#4480ff", "#daff2a", "#6fff2a", "#2aff50", "#2affba", "#2adaff", "#6c4dff", "#ff2ada", "#ff2a6f", "#ff502a"), Parse("#333333"));

        // Another theme: the borders are the colors 10% lighter on a dark theme, darker otherwise, as the base theme of Mermaid
        var palette = theme.Palette.Count is 0 ? [theme.PrimaryColor] : theme.Palette.ToArray();
        var peers = palette.Select(color => AdjustLightness(color, theme.IsDark ? 0.1 : -0.1)).ToArray();
        return (peers, [theme.IsDark ? SceneColor.Parse("#d3d3d3") : theme.TextColor]);
    }

    private static SceneColor[] Parse(params string[] colors) => [.. colors.Select(SceneColor.Parse)];

    private static SceneColor AdjustLightness(SceneColor color, double amount)
    {
        var r = color.R / 255.0;
        var g = color.G / 255.0;
        var b = color.B / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var lightness = (max + min) / 2;
        double hue = 0;
        double saturation = 0;
        if (max != min)
        {
            var delta = max - min;
            saturation = lightness > 0.5 ? delta / (2 - max - min) : delta / (max + min);
            hue = max == r ? ((g - b) / delta) + (g < b ? 6 : 0) : max == g ? ((b - r) / delta) + 2 : ((r - g) / delta) + 4;
            hue *= 60;
        }

        return SceneColor.FromHsl(hue, saturation, Math.Clamp(lightness + amount, 0, 1), color.Opacity);
    }
}
