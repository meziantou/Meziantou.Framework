using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a QR code as a PNG image.
/// </summary>
public static class QRCodePngRenderer
{
    /// <summary>Renders the QR code as PNG bytes with default options.</summary>
    public static byte[] ToPng(this QRCode qrCode)
    {
        return ToPng(qrCode, new QRCodePngOptions());
    }

    /// <summary>Renders the QR code as PNG bytes with the specified options.</summary>
    public static byte[] ToPng(this QRCode qrCode, QRCodePngOptions options)
    {
        using var stream = new MemoryStream();
        WriteToPng(qrCode, stream, options);

        return stream.ToArray();
    }

    /// <summary>Writes the QR code as PNG to the specified stream with default options.</summary>
    public static void WriteToPng(this QRCode qrCode, Stream stream)
    {
        WriteToPng(qrCode, stream, new QRCodePngOptions());
    }

    /// <summary>Writes the QR code as PNG to the specified stream with the specified options.</summary>
    public static void WriteToPng(this QRCode qrCode, Stream stream, QRCodePngOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var width = GetTotalDimension(qrCode.Width, options.QuietZoneModules, options.ModuleSize);
        var height = GetTotalDimension(qrCode.Height, options.QuietZoneModules, options.ModuleSize);

        var moduleSize = options.ModuleSize;
        var quietZoneModules = options.QuietZoneModules;
        PngWriter.WritePalette(stream, width, height, options.LightColor, options.DarkColor, (row, y) =>
        {
            // Every pixel row of a module row is the same, and the quiet zones above and below
            // the code are blank, so each of them is rendered once for all of its rows.
            var sourceRow = (y / moduleSize) - quietZoneModules;
            if (sourceRow < 0)
                return (quietZoneModules * moduleSize) - y;

            if (sourceRow >= qrCode.Height)
                return height - y;

            var column = 0;
            while (column < qrCode.Width)
            {
                if (!qrCode[sourceRow, column])
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column < qrCode.Width && qrCode[sourceRow, column])
                {
                    column++;
                }

                PngWriter.SetPixels(row, (quietZoneModules + runStart) * moduleSize, (column - runStart) * moduleSize);
            }

            return moduleSize - (y % moduleSize);
        });
    }

    private static int GetTotalDimension(int size, int quietZoneModules, int moduleSize)
    {
        var value = ((long)size + (2L * quietZoneModules)) * moduleSize;
        if (value > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException(nameof(moduleSize), "The output image dimensions are too large.");
        }

        return (int)value;
    }
}
