namespace Meziantou.Framework.Charts;

/// <summary>An element of a <see cref="ChartScene"/>.</summary>
public abstract class SceneElement
{
    private protected SceneElement()
    {
    }

    /// <summary>Gets or sets the CSS class of the element in the SVG output.</summary>
    public string? CssClass { get; set; }

    /// <summary>Gets or sets the text shown when hovering the element (an SVG <c>title</c>).</summary>
    public string? Tooltip { get; set; }

    /// <summary>Gets or sets the opacity of the element, from 0 to 1.</summary>
    public double Opacity { get; set; } = 1;
}
