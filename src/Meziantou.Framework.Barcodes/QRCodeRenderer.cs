using Meziantou.Framework.Barcodes.Internal;
using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a QR code as a vector scene or as an SVG document.
/// </summary>
/// <remarks>
/// The scene is what every renderer draws: <see cref="ToSvg"/> writes it as SVG, <c>Meziantou.Framework.Imaging.Barcodes</c>
/// draws it as an image (PNG, WebP, JPEG...), and <c>Meziantou.Framework.Pdf.VectorGraphics</c> as a PDF.
/// </remarks>
public static class QRCodeRenderer
{
    /// <summary>Creates the scene of the QR code: the quiet zone, the modules and the logo.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The scene of the QR code, as large as the QR code and its quiet zone.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    /// <exception cref="ArgumentException">The data of the logo is not a PNG, JPEG, GIF or WebP file.</exception>
    public static Scene CreateScene(this QRCode qrCode, QRCodeRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        options ??= new QRCodeRenderOptions();
        ArgumentNullException.ThrowIfNull(options.DarkPaint);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);
        var moduleSize = SymbolSceneBuilder.GetModuleSize(options.ModuleSize, options.SnapToPixels, nameof(options.ModuleSize));

        return SymbolSceneBuilder.Create(new SymbolSceneContent(
            qrCode.Modules,
            moduleSize,
            moduleSize,
            options.QuietZoneModules,
            options.QuietZoneModules,
            options.DarkPaint,
            options.LightPaint,
            options.SnapToPixels,
            Text: null,
            TextValue: null,
            options.Logo,
            nameof(options.ModuleSize),
            nameof(options.Logo)));
    }

    /// <summary>Renders the QR code as an SVG document.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <param name="svgOptions">The options of the SVG writer, or <see langword="null"/> for the defaults.</param>
    /// <returns>The SVG document, without an XML declaration.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <see cref="QRCodeRenderOptions.DarkPaint"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the dimensions of the drawing are too large.</exception>
    /// <exception cref="ArgumentException">The data of the logo is not a PNG, JPEG, GIF or WebP file.</exception>
    public static string ToSvg(this QRCode qrCode, QRCodeRenderOptions? options = null, SvgRenderOptions? svgOptions = null)
    {
        return SvgSceneRenderer.Render(CreateScene(qrCode, options), svgOptions);
    }
}
