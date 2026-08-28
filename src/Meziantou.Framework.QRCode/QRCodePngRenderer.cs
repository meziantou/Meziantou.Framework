using Meziantou.Framework.Internal;

namespace Meziantou.Framework;

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

        PngWriter.WritePalette(stream, width, height, options.LightColor, options.DarkColor, (row, y) =>
        {
            var sourceRow = (y / options.ModuleSize) - options.QuietZoneModules;
            if (sourceRow < 0 || sourceRow >= qrCode.Height)
                return;

            for (var x = 0; x < width; x++)
            {
                var sourceColumn = (x / options.ModuleSize) - options.QuietZoneModules;
                if (sourceColumn >= 0 && sourceColumn < qrCode.Width && qrCode[sourceRow, sourceColumn])
                {
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }
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
