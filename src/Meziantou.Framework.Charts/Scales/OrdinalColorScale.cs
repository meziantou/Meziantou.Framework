namespace Meziantou.Framework.Charts;

/// <summary>Gives each category a color of a palette, in order.</summary>
public sealed class OrdinalColorScale : ColorScale
{
    /// <summary>Gets or sets the categories, in order, or <see langword="null"/> to use the values of the data in the order they first appear.</summary>
    public IReadOnlyList<string>? Domain { get; set; }

    /// <summary>Gets or sets the colors, or <see langword="null"/> for the palette of the theme (<see cref="ChartTheme.Palette"/>). They are reused when there are more categories than colors.</summary>
    public IReadOnlyList<SceneColor>? Palette { get; set; }
}
