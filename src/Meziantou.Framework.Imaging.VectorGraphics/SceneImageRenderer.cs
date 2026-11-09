using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.VectorGraphics.Internal;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.VectorGraphics;

/// <summary>Draws the scenes of <c>Meziantou.Framework.VectorGraphics</c> (charts, diagrams) as images, or on a <see cref="DrawingCanvas"/>.</summary>
/// <remarks>
/// A source is laid out with the fonts that draw it (<see cref="ImageTextMeasurer"/>), so its texts fit. Save the image
/// in any format: <c>image.Save("drawing.png")</c>.
/// </remarks>
public static class SceneImageRenderer
{
    /// <summary>Lays a source out with the fonts of the options and renders it as an image of its size times <see cref="SceneImageOptions.Scale"/>, rounded up.</summary>
    /// <param name="source">The chart or the diagram.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The image is too large.</exception>
    public static Image<Rgba32> ToImage(this ISceneSource source, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new SceneImageOptions();
        return ToImage(source.CreateScene(CreateMeasurer(options)), options);
    }

    /// <summary>Renders a scene as an image of its size times <see cref="SceneImageOptions.Scale"/>, rounded up.</summary>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The image is too large.</exception>
    public static Image<Rgba32> ToImage(this Scene scene, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new SceneImageOptions();
        var width = Math.Ceiling(scene.Width * options.Scale);
        var height = Math.Ceiling(scene.Height * options.Scale);
        if (!(width <= int.MaxValue) || !(height <= int.MaxValue))
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

    /// <summary>Lays a source out with the fonts of the options and draws it on a canvas, as vector shapes and text.</summary>
    /// <remarks>The drawing is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply.</remarks>
    /// <param name="canvas">The canvas.</param>
    /// <param name="source">The chart or the diagram.</param>
    /// <param name="position">The top-left corner of the drawing.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers.</returns>
    public static RectangleD DrawScene(this DrawingCanvas canvas, ISceneSource source, PointD position, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(source);
        options ??= new SceneImageOptions();
        return canvas.DrawScene(source.CreateScene(CreateMeasurer(options)), position, options);
    }

    /// <summary>Draws a scene on a canvas, as vector shapes and text.</summary>
    /// <remarks>The scene is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply.</remarks>
    /// <param name="canvas">The canvas.</param>
    /// <param name="scene">The scene, laid out with <see cref="CreateMeasurer"/> so its texts fit.</param>
    /// <param name="position">The top-left corner of the drawing.</param>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers.</returns>
    public static RectangleD DrawScene(this DrawingCanvas canvas, Scene scene, PointD position, SceneImageOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(scene);
        options ??= new SceneImageOptions();
        SceneRenderer.Draw(canvas, scene, position, options);
        return new RectangleD(position.X, position.Y, scene.Width * options.Scale, scene.Height * options.Scale);
    }

    /// <summary>Creates the text measurer of the fonts of the options, to lay a source out with <see cref="ISceneSource.CreateScene"/>.</summary>
    /// <param name="options">The options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The measurer.</returns>
    public static ImageTextMeasurer CreateMeasurer(SceneImageOptions? options = null)
    {
        options ??= new SceneImageOptions();
        return new ImageTextMeasurer(
            options.GetFont(new SceneFont(1)),
            options.GetFont(new SceneFont(1, Bold: true)),
            options.GetFont(new SceneFont(1, Italic: true)),
            options.GetFont(new SceneFont(1, Bold: true, Italic: true)));
    }
}
