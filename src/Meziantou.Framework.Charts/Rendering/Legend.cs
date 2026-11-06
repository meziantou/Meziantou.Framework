namespace Meziantou.Framework.Charts;

/// <summary>The options of the legend of the color scale.</summary>
public sealed class Legend
{
    /// <summary>Gets or sets a value that indicates whether the legend is drawn, or <see langword="null"/> for the default: drawn when the color scale has more than one category, or when it is continuous.</summary>
    public bool? Visible { get; set; }

    /// <summary>Gets or sets the title of the legend, or <see langword="null"/> for none.</summary>
    public string? Title { get; set; }

    /// <summary>Gets or sets where the legend is drawn. The default is <see cref="LegendPosition.Top"/>.</summary>
    public LegendPosition Position { get; set; }
}
