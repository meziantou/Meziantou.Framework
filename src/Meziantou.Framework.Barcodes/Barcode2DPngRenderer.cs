using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a two-dimensional barcode as a PNG image.
/// </summary>
public static class Barcode2DPngRenderer
{
    /// <summary>Renders the barcode as PNG bytes with default options.</summary>
    public static byte[] ToPng(this Barcode2D barcode)
    {
        return ToPng(barcode, new Barcode2DPngOptions());
    }

    /// <summary>Renders the barcode as PNG bytes with the specified options.</summary>
    public static byte[] ToPng(this Barcode2D barcode, Barcode2DPngOptions options)
    {
        using var stream = new MemoryStream();
        WriteToPng(barcode, stream, options);
        return stream.ToArray();
    }

    /// <summary>Writes the barcode as PNG to the specified stream with default options.</summary>
    public static void WriteToPng(this Barcode2D barcode, Stream stream)
    {
        WriteToPng(barcode, stream, new Barcode2DPngOptions());
    }

    /// <summary>Writes the barcode as PNG to the specified stream with the specified options.</summary>
    public static void WriteToPng(this Barcode2D barcode, Stream stream, Barcode2DPngOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(stream);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var moduleSize = options.ModuleSize;
        var quietZoneModules = options.QuietZoneModules;
        var width = GetTotalDimension(barcode.Width, quietZoneModules, moduleSize);
        var height = GetTotalDimension(barcode.Height, quietZoneModules, moduleSize);
        PngWriter.WritePalette(stream, width, height, options.LightColor, options.DarkColor, (row, y) =>
        {
            // Every pixel row of a module row is the same, and the quiet zones above and below
            // the code are blank, so each of them is rendered once for all of its rows.
            var sourceRow = (y / moduleSize) - quietZoneModules;
            if (sourceRow < 0)
                return (quietZoneModules * moduleSize) - y;

            if (sourceRow >= barcode.Height)
                return height - y;

            var column = 0;
            while (column < barcode.Width)
            {
                if (!barcode[sourceRow, column])
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column < barcode.Width && barcode[sourceRow, column])
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
            throw new ArgumentOutOfRangeException(nameof(moduleSize), "The output image dimensions are too large.");

        return (int)value;
    }
}
