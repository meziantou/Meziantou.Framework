using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Barcodes;

/// <summary>
/// Provides methods to render a barcode as an <see cref="Image{TPixel}"/>.
/// </summary>
public static class BarcodeImageRenderer
{
    /// <summary>Renders the barcode as an image: what <see cref="DrawBarcode"/> draws on a canvas of the size <see cref="MeasureBarcode"/> gives, rounded up.</summary>
    /// <remarks>
    /// With the default <see cref="BarcodeDrawingOptions.SnapToPixels"/>, the modules are whole pixels and the image has two colors when
    /// the brushes are solid. With a <see langword="null"/> <see cref="BarcodeDrawingOptions.LightBrush"/>, the background is transparent.
    /// </remarks>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The drawing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="BarcodeDrawingOptions.DarkBrush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the image dimensions are too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="ImageConfiguration.Default"/>.</exception>
    public static Image<Rgba32> ToImage(this Barcode barcode, BarcodeDrawingOptions? options = null)
    {
        options ??= new BarcodeDrawingOptions();
        var (content, textSpacing) = GetDrawingContent(barcode, options);
        return ModuleCanvasRenderer.Render(content, GetLayout(content, textSpacing), nameof(options.ModuleWidth));
    }

    /// <summary>Measures the rectangle that <see cref="DrawBarcode(DrawingCanvas, Barcode, PointD, BarcodeDrawingOptions?)"/> covers, quiet zone and text included.</summary>
    /// <param name="barcode">The barcode to measure.</param>
    /// <param name="options">The drawing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The size of the drawing, as a rectangle located at the origin.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The text exceeds a limit of the configuration of the font.</exception>
    public static RectangleD MeasureBarcode(Barcode barcode, BarcodeDrawingOptions? options = null)
    {
        var (content, textSpacing) = GetDrawingContent(barcode, options ?? new BarcodeDrawingOptions());
        var layout = GetLayout(content, textSpacing);
        return new RectangleD(0, 0, layout.Width, layout.Height);
    }

    /// <summary>Draws the barcode on a canvas, as a vector shape, with its human-readable text when a font is set.</summary>
    /// <remarks>
    /// The barcode is drawn in the coordinates of the canvas, so the transform, clip and layers of the canvas apply. The
    /// bars are one shape, painted with <see cref="BarcodeDrawingOptions.DarkBrush"/>. The text (<see cref="Barcode.Text"/>)
    /// is centered under the bars; a text wider than the barcode widens the drawing, and the barcode is centered above it.
    /// Use <see cref="MeasureBarcode(Barcode, BarcodeDrawingOptions?)"/> to know the size before drawing.
    /// </remarks>
    /// <param name="canvas">The canvas to draw on.</param>
    /// <param name="barcode">The barcode to draw.</param>
    /// <param name="position">The top-left corner of the drawing. It is rounded to whole units when <see cref="BarcodeDrawingOptions.SnapToPixels"/> is set.</param>
    /// <param name="options">The drawing options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The rectangle the drawing covers, quiet zone and text included.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="BarcodeDrawingOptions.DarkBrush"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">A coordinate of <paramref name="position"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the magnitude of a coordinate of the drawing exceeds 16777216.</exception>
    /// <exception cref="InvalidOperationException">The callback that received the canvas has returned.</exception>
    /// <exception cref="InvalidImageContentException">The data of a glyph of the text is malformed.</exception>
    /// <exception cref="UnsupportedImageFeatureException">The outlines of the font use a feature that is not supported.</exception>
    /// <exception cref="ImageResourceLimitException">The text exceeds a limit of the configuration of the font, or the working storage would exceed the allocation limit.</exception>
    /// <exception cref="OperationCanceledException">The drawing was canceled.</exception>
    public static RectangleD DrawBarcode(this DrawingCanvas canvas, Barcode barcode, PointD position, BarcodeDrawingOptions? options = null)
    {
        var (content, textSpacing) = GetDrawingContent(barcode, options ?? new BarcodeDrawingOptions());
        return ModuleCanvasRenderer.Draw(canvas, position, GetLayout(content, textSpacing), content);
    }

    private static (ModuleCanvasContent Content, double TextSpacing) GetDrawingContent(Barcode barcode, BarcodeDrawingOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(options.DarkBrush);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);
        var moduleWidth = ModuleCanvasRenderer.GetModuleSize(options.ModuleWidth, options.SnapToPixels, nameof(options.ModuleWidth));
        var moduleHeight = ModuleCanvasRenderer.GetModuleSize(options.ModuleHeight, options.SnapToPixels, nameof(options.ModuleHeight));

        TextLayout? text = null;
        if (options.Font is not null)
        {
            ModuleCanvasRenderer.ThrowIfNotPositiveFinite(options.FontSize, nameof(options.FontSize));
            ModuleCanvasRenderer.ThrowIfNegativeOrNotFinite(options.TextSpacing, nameof(options.TextSpacing));

            text = TextLayout.Create(barcode.Text, options.Font, options.FontSize);
        }

        var content = new ModuleCanvasContent(
            Columns: barcode.Width,
            Rows: barcode.Height,
            IsDark: (row, column) => barcode[row, column],
            ModuleWidth: moduleWidth,
            ModuleHeight: moduleHeight,
            HorizontalQuietZoneModules: options.QuietZoneModules,
            VerticalQuietZoneModules: 0,
            options.DarkBrush,
            options.LightBrush,
            text,
            options.TextBrush,
            options.SnapToPixels);
        return (content, options.TextSpacing);
    }

    private static ModuleCanvasLayout GetLayout(ModuleCanvasContent content, double textSpacing)
    {
        return ModuleCanvasRenderer.GetLayout(content.Columns, content.Rows, content.ModuleWidth, content.ModuleHeight, content.HorizontalQuietZoneModules, content.VerticalQuietZoneModules, content.Text, textSpacing, content.SnapToPixels);
    }
}
