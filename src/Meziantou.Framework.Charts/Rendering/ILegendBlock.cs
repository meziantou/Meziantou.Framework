namespace Meziantou.Framework.Charts;

/// <summary>A legend that takes its space next to the plot area: the legend of the color scale or of the size scale.</summary>
internal interface ILegendBlock
{
    double Width { get; }

    double Height { get; }

    /// <summary>Lays the legend out in a width: in rows that wrap, or one item per row when <paramref name="vertical"/>.</summary>
    void Measure(double maxWidth, bool vertical);

    void Render(SceneGroup target, double left, double top);
}
