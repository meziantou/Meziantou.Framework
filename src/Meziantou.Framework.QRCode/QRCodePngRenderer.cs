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
        var imageData = CreateImageData(qrCode, width, height, options);

        PngWriter.WriteRgba(stream, width, height, imageData);
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

    private static byte[] CreateImageData(QRCode qrCode, int width, int height, QRCodePngOptions options)
    {
        var stride = (width * 4) + 1;
        var dataLength = (long)stride * height;
        if (dataLength > int.MaxValue)
        {
            throw new ArgumentOutOfRangeException("options.ModuleSize", "The output image is too large.");
        }

        var result = new byte[(int)dataLength];

        for (var row = 0; row < height; row++)
        {
            var rowOffset = row * stride;
            var sourceRow = (row / options.ModuleSize) - options.QuietZoneModules;
            for (var col = 0; col < width; col++)
            {
                var sourceCol = (col / options.ModuleSize) - options.QuietZoneModules;
                var isDark = sourceRow >= 0
                    && sourceRow < qrCode.Height
                    && sourceCol >= 0
                    && sourceCol < qrCode.Width
                    && qrCode[sourceRow, sourceCol];
                var color = isDark ? options.DarkColor : options.LightColor;
                var pixelOffset = rowOffset + 1 + (col * 4);
                result[pixelOffset] = color.Red;
                result[pixelOffset + 1] = color.Green;
                result[pixelOffset + 2] = color.Blue;
                result[pixelOffset + 3] = color.Alpha;
            }
        }

        return result;
    }
}
