using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to render a QR code as an <see cref="Image{TPixel}"/>.
/// </summary>
public static class QRCodeImageRenderer
{
    /// <summary>Renders the QR code as an image with default options.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    public static Image<Rgba32> ToImage(this QRCode qrCode)
    {
        return ToImage(qrCode, new QRCodeImageOptions());
    }

    /// <summary>Renders the QR code as an image with the specified options.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the image dimensions are too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="ImageConfiguration.Default"/>.</exception>
    public static Image<Rgba32> ToImage(this QRCode qrCode, QRCodeImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var width = ModuleImageRenderer.GetTotalDimension(qrCode.Width, options.QuietZoneModules, options.ModuleSize, nameof(options.ModuleSize));
        var height = ModuleImageRenderer.GetTotalDimension(qrCode.Height, options.QuietZoneModules, options.ModuleSize, nameof(options.ModuleSize));
        var grid = new ModuleGrid(
            Columns: qrCode.Width,
            Rows: qrCode.Height,
            IsDark: (row, column) => qrCode[row, column],
            ModuleWidth: options.ModuleSize,
            ModuleHeight: options.ModuleSize,
            HorizontalQuietZoneModules: options.QuietZoneModules,
            VerticalQuietZoneModules: options.QuietZoneModules);

        return ModuleImageRenderer.Render(grid, width, height, options.LightColor, options.DarkColor);
    }

    /// <summary>Measures the rectangle that <see cref="DrawQRCode(DrawingCanvas, QRCode, PointD, QRCodeDrawingOptions?)"/> covers, quiet zone included.</summary>
    /// <param name="qrCode">The QR code to measure.</param>
    /// <param name="options">The drawing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public static RectangleD MeasureQRCode(QRCode qrCode, QRCodeDrawingOptions? options = null)
    {
        var content = GetDrawingContent(qrCode, options ?? new QRCodeDrawingOptions());
        var layout = GetLayout(content);
        return new RectangleD(0, 0, layout.Width, layout.Height);
    }

    /// <summary>Draws the QR code on a canvas, as a vector shape.</summary>
    /// <remarks>
    /// The QR code is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply. The
    /// dark modules are one shape, painted with <see cref="QRCodeDrawingOptions.DarkBrush"/>, so adjacent modules have
    /// no seam. Use <see cref="MeasureQRCode(QRCode, QRCodeDrawingOptions?)"/> to know the size before drawing.
    /// </remarks>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="qrCode">The QR code to draw.</param>
    /// <param name="position">The top-left corner of the quiet zone. It is rounded to whole units when <see cref="QRCodeDrawingOptions.SnapToPixels"/> is set.</param>
    /// <param name="options">The drawing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers, quiet zone included.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeDrawingOptions.DarkBrush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="position"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the magnitude of a coordinate of the drawing exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="ImageResourceLimitException">The working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The drawing was canceled.</exception>
    public static RectangleD DrawQRCode(this DrawingCanvas canvas, QRCode qrCode, PointD position, QRCodeDrawingOptions? options = null)
    {
        var content = GetDrawingContent(qrCode, options ?? new QRCodeDrawingOptions());
        return ModuleCanvasRenderer.Draw(canvas, position, GetLayout(content), content);
    }

    private static ModuleCanvasContent GetDrawingContent(QRCode qrCode, QRCodeDrawingOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(options.DarkBrush);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);
        var moduleSize = ModuleCanvasRenderer.GetModuleSize(options.ModuleSize, options.SnapToPixels, nameof(options.ModuleSize));

        return new ModuleCanvasContent(
            Columns: qrCode.Width,
            Rows: qrCode.Height,
            IsDark: (row, column) => qrCode[row, column],
            ModuleWidth: moduleSize,
            ModuleHeight: moduleSize,
            HorizontalQuietZoneModules: options.QuietZoneModules,
            VerticalQuietZoneModules: options.QuietZoneModules,
            options.DarkBrush,
            options.LightBrush,
            Text: null,
            TextBrush: null,
            options.SnapToPixels);
    }

    private static ModuleCanvasLayout GetLayout(ModuleCanvasContent content)
    {
        return ModuleCanvasRenderer.GetLayout(content.Columns, content.Rows, content.ModuleWidth, content.ModuleHeight, content.HorizontalQuietZoneModules, content.VerticalQuietZoneModules, text: null, textSpacing: 0, content.SnapToPixels);
    }
}
