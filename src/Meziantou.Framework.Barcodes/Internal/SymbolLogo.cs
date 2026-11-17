using Meziantou.Framework.VectorGraphics;

namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// The logo of a symbol, placed: the square the image is centered in, relative to the top-left corner of the quiet zone,
/// and the modules cleared under it.
/// </summary>
internal sealed record SymbolLogo(ReadOnlyMemory<byte> Data, double X, double Y, double Size, ModuleRectangle? ClearedModules, ScenePaint? BackdropPaint, double BackdropCornerRadius)
{
    // Absorbs the rounding errors of fractional module sizes: a logo edge on a module boundary does not clear the next module
    private const double Tolerance = 1e-9;

    /// <summary>Places a logo over the center of a symbol.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An option of the logo is out of range.</exception>
    public static SymbolLogo Create(BarcodeLogo logo, int rows, int columns, double moduleWidth, double moduleHeight, int horizontalQuietZoneModules, int verticalQuietZoneModules, bool snapToPixels, string parameterName)
    {
        if (logo.SizePercent is < 1 or > 100)
            throw new ArgumentOutOfRangeException(parameterName, logo.SizePercent, "The size of the logo must be between 1 and 100 percent.");

        if (logo.MarginModules < 0)
            throw new ArgumentOutOfRangeException(parameterName, logo.MarginModules, "The margin of the logo must be zero or more modules.");

        if (!double.IsFinite(logo.BackdropCornerRadius) || logo.BackdropCornerRadius < 0)
            throw new ArgumentOutOfRangeException(parameterName, logo.BackdropCornerRadius, "The corner radius of the backdrop must be zero or a positive finite number.");

        var symbolWidth = (columns + (2.0 * horizontalQuietZoneModules)) * moduleWidth;
        var symbolHeight = (rows + (2.0 * verticalQuietZoneModules)) * moduleHeight;
        var size = Math.Max(1, Math.Min(symbolWidth, symbolHeight) * logo.SizePercent / 100);
        var left = (symbolWidth - size) / 2;
        var top = (symbolHeight - size) / 2;
        if (snapToPixels)
        {
            size = Math.Max(1, Math.Floor(size));
            left = Math.Floor((symbolWidth - size) / 2);
            top = Math.Floor((symbolHeight - size) / 2);
        }

        ModuleRectangle? cleared = null;
        if (logo.ClearModules)
        {
            // The pixels keep their proportions, centered in the square (XMidYMid): a wide logo covers a band of it. When
            // the size of the pixels is unknown, the whole square is cleared.
            var (imageLeft, imageTop, imageWidth, imageHeight) = (left, top, size, size);
            if (LogoImageSize.TryGetSize(logo.Data.Span, out var pixelWidth, out var pixelHeight))
            {
                if (pixelWidth > pixelHeight)
                {
                    imageHeight = size * pixelHeight / pixelWidth;
                    imageTop += (size - imageHeight) / 2;
                }
                else
                {
                    imageWidth = size * pixelWidth / pixelHeight;
                    imageLeft += (size - imageWidth) / 2;
                }
            }

            var (firstColumn, endColumn) = GetModuleRange(imageLeft, imageWidth, horizontalQuietZoneModules, moduleWidth, logo.MarginModules, columns);
            var (firstRow, endRow) = GetModuleRange(imageTop, imageHeight, verticalQuietZoneModules, moduleHeight, logo.MarginModules, rows);
            if (endColumn > firstColumn && endRow > firstRow)
            {
                cleared = new ModuleRectangle(firstRow, firstColumn, endRow - firstRow, endColumn - firstColumn);
            }
        }

        return new SymbolLogo(logo.Data, left, top, size, cleared, logo.BackdropPaint, logo.BackdropCornerRadius);
    }

    // The modules the segment [start, start + length) of the drawing covers, grown by the margin and clamped to the symbol
    private static (int Start, int End) GetModuleRange(double start, double length, int quietZoneModules, double moduleSize, int margin, int count)
    {
        var first = Math.Floor((start / moduleSize) - quietZoneModules + Tolerance) - margin;
        var end = Math.Ceiling(((start + length) / moduleSize) - quietZoneModules - Tolerance) + margin;
        return ((int)Math.Clamp(first, 0, count), (int)Math.Clamp(end, 0, count));
    }
}
