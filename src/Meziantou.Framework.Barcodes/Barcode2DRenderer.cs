using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a two-dimensional barcode (Data Matrix, PDF417, Aztec) as a vector scene or as an SVG document.
/// </summary>
/// <remarks>
/// The scene is what every renderer draws: <see cref="ToSvg"/> writes it as SVG, <c>Meziantou.Framework.Imaging.Barcodes</c>
/// draws it as an image (PNG, WebP, JPEG...), and <c>Meziantou.Framework.Pdf.VectorGraphics</c> as a PDF.
/// </remarks>
public static class Barcode2DRenderer
{
    /// <summary>Creates the scene of the barcode: the quiet zone and the modules.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The scene of the barcode, as large as the symbol and its quiet zone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="Barcode2DRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    public static Scene CreateScene(this Barcode2D barcode, Barcode2DRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        options ??= new Barcode2DRenderOptions();
        ArgumentNullException.ThrowIfNull(options.DarkPaint);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);
        var moduleSize = SymbolSceneBuilder.GetModuleSize(options.ModuleSize, options.SnapToPixels, nameof(options.ModuleSize));

        return SymbolSceneBuilder.Create(new SymbolSceneContent(
            barcode.Modules,
            moduleSize,
            moduleSize,
            options.QuietZoneModules,
            options.QuietZoneModules,
            options.DarkPaint,
            options.LightPaint,
            options.SnapToPixels,
            Text: null,
            TextValue: null,
            Logo: null,
            nameof(options.ModuleSize)));
    }

    /// <summary>Renders the barcode as an SVG document.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="svgOptions">The options of the SVG writer, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <see cref="Barcode2DRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    public static string ToSvg(this Barcode2D barcode, Barcode2DRenderOptions? options = null, SvgRenderOptions? svgOptions = null)
    {
        return SvgSceneRenderer.Render(CreateScene(barcode, options), svgOptions);
    }
}
