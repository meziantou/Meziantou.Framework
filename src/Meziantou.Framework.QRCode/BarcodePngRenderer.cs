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

        PngWriter.WritePalette(stream, width, height, options.LightColor, options.DarkColor, (row, y) =>
        {
            var sourceRow = y / options.ModuleHeight;
            if (sourceRow < 0 || sourceRow >= barcode.Height)
                return;

            for (var x = 0; x < width; x++)
            {
                var sourceColumn = (x / options.ModuleWidth) - options.QuietZoneModules;
                if (sourceColumn >= 0 && sourceColumn < barcode.Width && barcode[sourceRow, sourceColumn])
                {
                    row[x >> 3] |= (byte)(0x80 >> (x & 7));
                }
            }
        });
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
}
