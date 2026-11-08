namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a two-dimensional barcode as an SVG string.
/// </summary>
public static class Barcode2DSvgRenderer
{
    /// <summary>Renders the barcode as an SVG string with default options.</summary>
    public static string ToSvg(this Barcode2D barcode)
    {
        return ToSvg(barcode, new Barcode2DSvgOptions());
    }

    /// <summary>Renders the barcode as an SVG string with the specified options.</summary>
    public static string ToSvg(this Barcode2D barcode, Barcode2DSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleSize, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var moduleSize = options.ModuleSize;
        var quietZone = options.QuietZoneModules;
        var totalWidthStr = (((long)barcode.Width + (2L * quietZone)) * moduleSize).ToString(CultureInfo.InvariantCulture);
        var totalHeightStr = (((long)barcode.Height + (2L * quietZone)) * moduleSize).ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder(capacity: 1024);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ").Append(totalWidthStr).Append(' ').Append(totalHeightStr).Append("\">");
        sb.Append("<rect width=\"").Append(totalWidthStr).Append("\" height=\"").Append(totalHeightStr).Append("\" fill=\"").Append(options.LightColor.ToCssString()).Append("\"/>");
        sb.Append("<path fill=\"").Append(options.DarkColor.ToCssString()).Append("\" d=\"");

        for (var row = 0; row < barcode.Height; row++)
        {
            // Consecutive rows that are the same (the rows of PDF417 are several modules high) are one rectangle
            var rowCount = 1;
            while (row + rowCount < barcode.Height && SameRow(barcode, row, row + rowCount))
            {
                rowCount++;
            }

            var heightStr = ((long)rowCount * moduleSize).ToString(CultureInfo.InvariantCulture);
            var column = 0;
            while (column < barcode.Width)
            {
                if (!barcode[row, column])
                {
                    column++;
                    continue;
                }

                var runStart = column;
                while (column < barcode.Width && barcode[row, column])
                {
                    column++;
                }

                var width = ((long)column - runStart) * moduleSize;
                sb.Append('M').Append((((long)runStart + quietZone) * moduleSize).ToString(CultureInfo.InvariantCulture));
                sb.Append(' ').Append((((long)row + quietZone) * moduleSize).ToString(CultureInfo.InvariantCulture));
                sb.Append('h').Append(width.ToString(CultureInfo.InvariantCulture));
                sb.Append('v').Append(heightStr);
                sb.Append('h').Append((-width).ToString(CultureInfo.InvariantCulture));
                sb.Append('z');
            }

            row += rowCount - 1;
        }

        sb.Append("\"/></svg>");
        return sb.ToString();
    }

    private static bool SameRow(Barcode2D barcode, int row1, int row2)
    {
        for (var column = 0; column < barcode.Width; column++)
        {
            if (barcode[row1, column] != barcode[row2, column])
                return false;
        }

        return true;
    }
}
