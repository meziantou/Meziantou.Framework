using Meziantou.Framework.Imaging;
using Meziantou.Framework.Internal;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a barcode as an <see cref="Image{TPixel}"/>.
/// </summary>
public static class BarcodeImageRenderer
{
    /// <summary>Renders the barcode as an image with default options.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    public static Image<Rgba32> ToImage(this Barcode barcode)
    {
        return ToImage(barcode, new BarcodeImageOptions());
    }

    /// <summary>Renders the barcode as an image with the specified options.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options.</param>
    /// <returns>A single-frame image. The caller owns it and must dispose it.</returns>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the image dimensions are too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image exceeds a limit of <see cref="ImageConfiguration.Default"/>.</exception>
    public static Image<Rgba32> ToImage(this Barcode barcode, BarcodeImageOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        // The quiet zone is on the left and right sides only, as for the other barcode renderers
        var width = ModuleImageRenderer.GetTotalDimension(barcode.Width, options.QuietZoneModules, options.ModuleWidth, nameof(options.ModuleWidth));
        var height = ModuleImageRenderer.GetTotalDimension(barcode.Height, quietZoneModules: 0, options.ModuleHeight, nameof(options.ModuleHeight));
        var grid = new ModuleGrid(
            Columns: barcode.Width,
            Rows: barcode.Height,
            IsDark: (row, column) => barcode[row, column],
            ModuleWidth: options.ModuleWidth,
            ModuleHeight: options.ModuleHeight,
            HorizontalQuietZoneModules: options.QuietZoneModules,
            VerticalQuietZoneModules: 0);

        return ModuleImageRenderer.Render(grid, width, height, options.LightColor, options.DarkColor);
    }
}
