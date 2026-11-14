using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.VectorGraphics;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to render a linear barcode as an <see cref="Image{TPixel}"/>, or to draw it on a canvas.
/// </summary>
/// <remarks>
/// The drawing is the scene of <see cref="BarcodeRenderer.CreateScene"/>, drawn by <see cref="SceneImageRenderer"/>: the image is
/// the same as the SVG of <see cref="BarcodeRenderer.ToSvg"/>, human-readable text included.
/// </remarks>
public static class BarcodeImageRenderer
{
    /// <summary>Renders the barcode as an image of the size of its scene times <see cref="SceneImageOptions.Scale"/>, rounded up.</summary>
    /// <remarks>
    /// With the default <see cref="BarcodeRenderOptions.SnapToPixels"/> and scale, the modules are whole pixels and the image has two colors when
    /// the paints are solid. With a <see langword="null"/> <see cref="BarcodeRenderOptions.LightPaint"/>, the background is transparent.
    /// </remarks>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="BarcodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the image dimensions are too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="ImageConfiguration.Default"/>.</exception>
    public static Image<Rgba32> ToImage(this Barcode barcode, BarcodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        return barcode.CreateScene(options).ToImage(imageOptions);
    }

    /// <summary>Measures the rectangle that <see cref="DrawBarcode"/> covers, quiet zone and text included.</summary>
    /// <param name="barcode">The barcode to measure.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="BarcodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    public static RectangleD MeasureBarcode(Barcode barcode, BarcodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        return SceneImageRenderer.MeasureScene(barcode.CreateScene(options), imageOptions);
    }

    /// <summary>Draws the barcode on a canvas, as vector shapes and text.</summary>
    /// <remarks>
    /// The barcode is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply. The
    /// dark modules are one shape, painted with <see cref="BarcodeRenderOptions.DarkPaint"/>, so adjacent modules have no seam. Use
    /// <see cref="MeasureBarcode"/> to know the size before drawing.
    /// </remarks>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="barcode">The barcode to draw.</param>
    /// <param name="position">The top-left corner of the drawing. It is rounded to whole units when <see cref="BarcodeRenderOptions.SnapToPixels"/> is set.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers, quiet zone and text included.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="BarcodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="position"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the magnitude of a coordinate of the drawing exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The drawing was canceled.</exception>
    public static RectangleD DrawBarcode(this DrawingCanvas canvas, Barcode barcode, PointD position, BarcodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        var scene = barcode.CreateScene(options);
        if (options?.SnapToPixels ?? true)
        {
            position = new PointD(Math.Round(position.X, MidpointRounding.AwayFromZero), Math.Round(position.Y, MidpointRounding.AwayFromZero));
        }

        return canvas.DrawScene(scene, position, imageOptions);
    }
}
