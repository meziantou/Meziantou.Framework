using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.VectorGraphics;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to render a QR code as an <see cref="Image{TPixel}"/>, or to draw it on a canvas.
/// </summary>
/// <remarks>
/// The drawing is the scene of <see cref="QRCodeRenderer.CreateScene"/>, drawn by <see cref="SceneImageRenderer"/>: the image is
/// the same as the SVG of <see cref="QRCodeRenderer.ToSvg"/>, logo included.
/// </remarks>
public static class QRCodeImageRenderer
{
    /// <summary>Renders the QR code as an image of the size of its scene times <see cref="SceneImageOptions.Scale"/>, rounded up.</summary>
    /// <remarks>
    /// With the default <see cref="QRCodeRenderOptions.SnapToPixels"/> and scale, the modules are whole pixels and the image has two colors when
    /// the paints are solid. With a <see langword="null"/> <see cref="QRCodeRenderOptions.LightPaint"/>, the background is transparent.
    /// </remarks>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the image dimensions are too large.</exception>
    /// <exception cref="ArgumentException">The data of the logo is not a PNG, JPEG, GIF or WebP file.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="ImageConfiguration.Default"/>.</exception>
    public static Image<Rgba32> ToImage(this QRCode qrCode, QRCodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        return qrCode.CreateScene(options).ToImage(imageOptions);
    }

    /// <summary>Measures the rectangle that <see cref="DrawQRCode"/> covers, quiet zone included.</summary>
    /// <param name="qrCode">The QR code to measure.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    /// <exception cref="ArgumentException">The data of the logo is not a PNG, JPEG, GIF or WebP file.</exception>
    public static RectangleD MeasureQRCode(QRCode qrCode, QRCodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        return SceneImageRenderer.MeasureScene(qrCode.CreateScene(options), imageOptions);
    }

    /// <summary>Draws the QR code on a canvas, as vector shapes and its logo.</summary>
    /// <remarks>
    /// The QR code is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply. The
    /// dark modules are one shape, painted with <see cref="QRCodeRenderOptions.DarkPaint"/>, so adjacent modules have no seam. Use
    /// <see cref="MeasureQRCode"/> to know the size before drawing.
    /// </remarks>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="qrCode">The QR code to draw.</param>
    /// <param name="position">The top-left corner of the drawing. It is rounded to whole units when <see cref="QRCodeRenderOptions.SnapToPixels"/> is set.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="imageOptions">The options of the image renderer (scale, fonts), or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers, quiet zone included.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="position"/> is not a finite number.</exception>
    /// <exception cref="ArgumentException">The data of the logo is not a PNG, JPEG, GIF or WebP file.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the magnitude of a coordinate of the drawing exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The drawing was canceled.</exception>
    public static RectangleD DrawQRCode(this DrawingCanvas canvas, QRCode qrCode, PointD position, QRCodeRenderOptions? options = null, SceneImageOptions? imageOptions = null)
    {
        var scene = qrCode.CreateScene(options);
        if (options?.SnapToPixels ?? true)
        {
            position = new PointD(Math.Round(position.X, MidpointRounding.AwayFromZero), Math.Round(position.Y, MidpointRounding.AwayFromZero));
        }

        return canvas.DrawScene(scene, position, imageOptions);
    }
}
