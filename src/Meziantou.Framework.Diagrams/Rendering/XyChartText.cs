namespace Meziantou.Framework.Diagrams.Rendering;

/// <summary>Measures and draws the texts of an XY chart, which are single lines anchored on a point as in Mermaid.</summary>
internal sealed class XyChartText(ITextMeasurer measurer)
{
    // The height of a line, as the bounding boxes of the texts of a browser
    private const double LineSpacing = 1.2;

    /// <summary>Returns the width of the widest text and the height of a line, or zeros when there is no text.</summary>
    public (double Width, double Height) MeasureMax(IEnumerable<string> texts, double fontSize)
    {
        var font = new SceneFont(fontSize);
        var width = 0.0;
        var height = 0.0;
        foreach (var text in texts)
        {
            var metrics = measurer.Measure(text, font);
            width = Math.Max(width, metrics.Width);
            height = Math.Max(height, Math.Max(metrics.Height, fontSize * LineSpacing));
        }

        return (width, height);
    }

    public double MeasureWidth(string text, double fontSize) => measurer.Measure(text, new SceneFont(fontSize)).Width;

    /// <summary>Draws a text anchored on a point, by its top or its middle, rotated around the point.</summary>
    public void Draw(SceneGroup group, string text, double x, double y, double fontSize, SceneColor color, XyChartTextBaseline baseline, SceneTextAnchor anchor, double rotation)
    {
        if (text.Length is 0)
            return;

        var font = new SceneFont(fontSize);
        var metrics = measurer.Measure(text, font);

        // The distance from the point to the baseline, before the rotation
        var offset = baseline is XyChartTextBaseline.Top ? metrics.Ascent : (metrics.Ascent - metrics.Descent) / 2;
        var angle = rotation * Math.PI / 180;
        var baselineX = x - (offset * Math.Sin(angle));
        var baselineY = y + (offset * Math.Cos(angle));
        group.Add(new SceneText(baselineX, baselineY, text, font, color) { Anchor = anchor, Rotation = rotation, MeasuredWidth = metrics.Width });
    }
}
