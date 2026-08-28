using Meziantou.Framework.Internal;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a barcode as a PNG image.
/// </summary>
public static class BarcodePngRenderer
{
    /// <summary>Renders the barcode as PNG bytes with default options.</summary>
    public static byte[] ToPng(this Barcode barcode)
    {
        return ToPng(barcode, new BarcodePngOptions());
    }

    /// <summary>Renders the barcode as PNG bytes with the specified options.</summary>
    public static byte[] ToPng(this Barcode barcode, BarcodePngOptions options)
    {
        using var stream = new MemoryStream();
        WriteToPng(barcode, stream, options);
        return stream.ToArray();
    }

    /// <summary>Writes the barcode as PNG to the specified stream with default options.</summary>
    public static void WriteToPng(this Barcode barcode, Stream stream)
    {
        WriteToPng(barcode, stream, new BarcodePngOptions());
    }

    /// <summary>Writes the barcode as PNG to the specified stream with the specified options.</summary>
    public static void WriteToPng(this Barcode barcode, Stream stream, BarcodePngOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var width = GetTotalDimensionWithQuietZone(barcode.Width, options.QuietZoneModules, options.ModuleWidth, nameof(options.ModuleWidth));
        var height = GetTotalDimension(barcode.Height, options.ModuleHeight, nameof(options.ModuleHeight));
        var imageData = CreateImageData(barcode, width, height, options);

        PngWriter.WriteRgba(stream, width, height, imageData);
    }

    private static int GetTotalDimensionWithQuietZone(int size, int quietZoneModules, int moduleSize, string parameterName)
    {
        var value = ((long)size + (2L * quietZoneModules)) * moduleSize;
        if (value > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "The output image dimensions are too large.");

        return (int)value;
    }

    private static int GetTotalDimension(int size, int moduleSize, string parameterName)
    {
        var value = (long)size * moduleSize;
        if (value > int.MaxValue)
            throw new ArgumentOutOfRangeException(parameterName, "The output image dimensions are too large.");

        return (int)value;
    }

    private static byte[] CreateImageData(Barcode barcode, int width, int height, BarcodePngOptions options)
    {
        var stride = (width * 4) + 1;
        var dataLength = (long)stride * height;
        if (dataLength > int.MaxValue)
            throw new ArgumentOutOfRangeException(nameof(options), "The output image is too large.");

        var result = new byte[(int)dataLength];
        for (var row = 0; row < height; row++)
        {
            var rowOffset = row * stride;
            var sourceRow = row / options.ModuleHeight;
            for (var col = 0; col < width; col++)
            {
                var sourceCol = (col / options.ModuleWidth) - options.QuietZoneModules;
                var isDark = sourceRow >= 0
                    && sourceRow < barcode.Height
                    && sourceCol >= 0
                    && sourceCol < barcode.Width
                    && barcode[sourceRow, sourceCol];

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
