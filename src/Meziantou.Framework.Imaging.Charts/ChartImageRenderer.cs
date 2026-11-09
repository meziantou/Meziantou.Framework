using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.VectorGraphics;

namespace Meziantou.Framework.Imaging.Charts;

/// <summary>Draws the charts of <c>Meziantou.Framework.Charts</c> as images, or on a <see cref="DrawingCanvas"/>.</summary>
/// <remarks>
/// The plot is laid out with the fonts that draw it (<see cref="ImageTextMeasurer"/>), so the margins, the rotated labels
/// and the truncated labels fit the text. Save the image in any format: <c>image.Save("chart.png")</c>. To draw a
/// <see cref="Meziantou.Framework.VectorGraphics.Scene"/>, use <see cref="SceneImageRenderer"/>.
/// </remarks>
public static class ChartImageRenderer
{
    /// <summary>Renders a plot as an image of its size times <see cref="SceneImageOptions.Scale"/>, rounded up.</summary>
    /// <param name="plot">The plot.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    public static Image<Rgba32> ToImage(this Plot plot, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        return SceneImageRenderer.ToImage(plot, options);
    }

    /// <summary>Measures the rectangle that <see cref="DrawChart"/> covers.</summary>
    /// <param name="plot">The plot.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    public static RectangleD MeasureChart(Plot plot, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        var scale = (options ?? new SceneImageOptions()).Scale;
        return new RectangleD(0, 0, plot.Width * scale, plot.Height * scale);
    }

    /// <summary>Draws a plot on a canvas, as vector shapes and text.</summary>
    /// <remarks>The chart is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply.</remarks>
    /// <param name="canvas">The canvas.</param>
    /// <param name="plot">The plot.</param>
    /// <param name="position">The top-left corner of the chart.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the chart covers.</returns>
    public static RectangleD DrawChart(this DrawingCanvas canvas, Plot plot, PointD position, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        return canvas.DrawScene(plot, position, options);
    }
}
