using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a linear barcode as a vector scene or as an SVG document.
/// </summary>
/// <remarks>
/// The scene is what every renderer draws: <see cref="ToSvg"/> writes it as SVG, <c>Meziantou.Framework.Imaging.Barcodes</c>
/// draws it as an image (PNG, WebP, JPEG...), and <c>Meziantou.Framework.Pdf.VectorGraphics</c> as a PDF.
/// </remarks>
public static class BarcodeRenderer
{
    /// <summary>Creates the scene of the barcode: the quiet zone, the bars and the human-readable text.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The scene of the barcode, as large as the bars, their quiet zone and the text.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/>, <see cref="BarcodeRenderOptions.DarkPaint"/> or <see cref="BarcodeTextOptions.Fonts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    public static Scene CreateScene(this Barcode barcode, BarcodeRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        options ??= new BarcodeRenderOptions();
        ArgumentNullException.ThrowIfNull(options.DarkPaint);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);
        var moduleWidth = SymbolSceneBuilder.GetModuleSize(options.ModuleWidth, options.SnapToPixels, nameof(options.ModuleWidth));

        // The rows of a stacked barcode are one module high: as high as a module is wide
        var moduleHeight = barcode.IsStacked ? moduleWidth : SymbolSceneBuilder.GetModuleSize(options.ModuleHeight, options.SnapToPixels, nameof(options.ModuleHeight));

        var text = options.Text;
        if (text is not null)
        {
            if (text.Fonts is null)
                throw new ArgumentNullException(nameof(options), "The fonts of the text cannot be null.");

            SymbolSceneBuilder.ThrowIfNotPositiveFinite(text.FontSize, nameof(options.Text.FontSize));
            SymbolSceneBuilder.ThrowIfNegativeOrNotFinite(text.Spacing, nameof(options.Text.Spacing));
        }

        var content = new SymbolSceneContent(
            barcode.Modules,
            moduleWidth,
            moduleHeight,
            options.QuietZoneModules,
            VerticalQuietZoneModules: 0,
            options.DarkPaint,
            options.LightPaint,
            options.SnapToPixels,
            text,
            PrintableText.Create(barcode.Text),
            Logo: null,
            nameof(options.ModuleWidth));

        // EAN and UPC print their digits in groups between the guards, which extend below the other bars
        if (text is not null && barcode.EanUpcLayout is { } layout)
            return EanUpcSceneBuilder.Create(content, layout);

        return SymbolSceneBuilder.Create(content);
    }

    /// <summary>Renders the barcode as an SVG document.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="svgOptions">The options of the SVG writer, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    /// <remarks>
    /// The text is drawn with the fonts of <see cref="BarcodeTextOptions.Fonts"/>: set <see cref="SvgRenderOptions.EmbedFonts"/>
    /// so that viewers that do not have them draw the text as it was measured.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/>, <see cref="BarcodeRenderOptions.DarkPaint"/> or <see cref="BarcodeTextOptions.Fonts"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    public static string ToSvg(this Barcode barcode, BarcodeRenderOptions? options = null, SvgRenderOptions? svgOptions = null)
    {
        return SvgSceneRenderer.Render(CreateScene(barcode, options), svgOptions);
    }
}
