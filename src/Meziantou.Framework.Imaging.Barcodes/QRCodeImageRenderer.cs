using Meziantou.Framework.Barcodes;
using Meziantou.Framework.Imaging.Barcodes.Internal;

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
}
