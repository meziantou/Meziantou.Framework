namespace Meziantou.Framework.Mermaid;

/// <summary>The position of the legend of a pie chart, relative to the pie (Mermaid <c>legendPosition</c>).</summary>
public enum PieLegendPosition
{
    /// <summary>On the right of the pie.</summary>
    Right,

    /// <summary>On the left of the pie. It is written back, but the legend is drawn on the right.</summary>
    Left,

    /// <summary>Above the pie.</summary>
    Top,

    /// <summary>Below the pie.</summary>
    Bottom,

    /// <summary>Over the center of the pie. It is written back, but the legend is drawn on the right.</summary>
    Center,
}
