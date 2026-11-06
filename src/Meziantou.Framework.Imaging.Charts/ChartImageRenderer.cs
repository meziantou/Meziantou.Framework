using Meziantou.Framework.Charts;
using Meziantou.Framework.Imaging.Charts.Internal;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Charts;

/// <summary>Draws the charts of <c>Meziantou.Framework.Charts</c> as images, or on a <see cref="DrawingCanvas"/>.</summary>
/// <remarks>
/// The plot is laid out with the fonts that draw it (<see cref="ImageTextMeasurer"/>), so the margins, the rotated labels
/// and the truncated labels fit the text. Save the image in any format: <c>image.Save("chart.png")</c>.
/// </remarks>
public static class ChartImageRenderer
{
    /// <summary>Renders a plot as an image of its size times <see cref="ChartImageOptions.Scale"/>, rounded up.</summary>
    /// <param name="plot">The plot.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    public static Image<Rgba32> ToImage(this Plot plot, ChartImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        options ??= new ChartImageOptions();
        return ToImage(plot.CreateScene(CreateMeasurer(options)), options);
    }

    /// <summary>Renders a scene as an image of its size times <see cref="ChartImageOptions.Scale"/>, rounded up.</summary>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The image is too large.</exception>
    public static Image<Rgba32> ToImage(this ChartScene scene, ChartImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new ChartImageOptions();
        var width = Math.Ceiling(scene.Width * options.Scale);
        var height = Math.Ceiling(scene.Height * options.Scale);
        if (width > int.MaxValue || height > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), "The output image dimensions are too large.");

        var image = new Image<Rgba32>((int)width, (int)height, Colors.Transparent);
        try
        {
            image.Draw((scene, options), static (canvas, state) => SceneRenderer.Draw(canvas, state.scene, default, state.options));
        }
        catch
        {
            image.Dispose();
            throw;
        }

        return image;
    }

    /// <summary>Measures the rectangle that <see cref="DrawChart"/> covers.</summary>
    /// <param name="plot">The plot.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    public static RectangleD MeasureChart(Plot plot, ChartImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        var scale = (options ?? new ChartImageOptions()).Scale;
        return new RectangleD(0, 0, plot.Width * scale, plot.Height * scale);
    }

    /// <summary>Draws a plot on a canvas, as vector shapes and text.</summary>
    /// <remarks>The chart is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply.</remarks>
    /// <param name="canvas">The canvas.</param>
    /// <param name="plot">The plot.</param>
    /// <param name="position">The top-left corner of the chart.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the chart covers.</returns>
    public static RectangleD DrawChart(this DrawingCanvas canvas, Plot plot, PointD position, ChartImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(plot);
        options ??= new ChartImageOptions();
        return canvas.DrawChartScene(plot.CreateScene(CreateMeasurer(options)), position, options);
    }

    /// <summary>Draws a scene on a canvas, as vector shapes and text.</summary>
    /// <param name="canvas">The canvas.</param>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="position">The top-left corner of the chart.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the chart covers.</returns>
    public static RectangleD DrawChartScene(this DrawingCanvas canvas, ChartScene scene, PointD position, ChartImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new ChartImageOptions();
        SceneRenderer.Draw(canvas, scene, position, options);
        return new RectangleD(position.X, position.Y, scene.Width * options.Scale, scene.Height * options.Scale);
    }

    /// <summary>Creates the text measurer of the fonts of the options, to lay a plot out with <see cref="Plot.CreateScene"/>.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The measurer.</returns>
    public static ImageTextMeasurer CreateMeasurer(ChartImageOptions? options = null)
    {
        options ??= new ChartImageOptions();
        return new ImageTextMeasurer(options.RegularFont, options.BoldOrRegularFont);
    }
}
