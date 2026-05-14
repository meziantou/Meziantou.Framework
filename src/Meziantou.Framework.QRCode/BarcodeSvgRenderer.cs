using System.Globalization;
using System.Security;
using System.Text;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a barcode as an SVG string.
/// </summary>
public static class BarcodeSvgRenderer
{
    /// <summary>Renders the barcode as an SVG string with default options.</summary>
    public static string ToSvg(this Barcode barcode)
    {
        return ToSvg(barcode, new BarcodeSvgOptions());
    }

    /// <summary>Renders the barcode as an SVG string with the specified options.</summary>
    public static string ToSvg(this Barcode barcode, BarcodeSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DarkColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LightColor);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var quietZone = options.QuietZoneModules;
        var moduleWidth = options.ModuleWidth;
        var moduleHeight = options.ModuleHeight;
        var totalWidth = ((long)barcode.Width + (2L * quietZone)) * moduleWidth;
        var totalHeight = (long)barcode.Height * moduleHeight;
        var totalWidthStr = totalWidth.ToString(CultureInfo.InvariantCulture);
        var totalHeightStr = totalHeight.ToString(CultureInfo.InvariantCulture);
        var moduleHeightStr = moduleHeight.ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder(capacity: 1024);
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
        sb.Append(totalWidthStr);
        sb.Append(' ');
        sb.Append(totalHeightStr);
        sb.Append("\">");

        sb.Append("<rect width=\"");
        sb.Append(totalWidthStr);
        sb.Append("\" height=\"");
        sb.Append(totalHeightStr);
        sb.Append("\" fill=\"");
        sb.Append(SecurityElement.Escape(options.LightColor));
        sb.Append("\"/>");

        sb.Append("<path fill=\"");
        sb.Append(SecurityElement.Escape(options.DarkColor));
        sb.Append("\" d=\"");

        for (var row = 0; row < barcode.Height; row++)
        {
            var col = 0;
            while (col < barcode.Width)
            {
                if (barcode[row, col])
                {
                    var runStart = col;
                    while (col < barcode.Width && barcode[row, col])
                    {
                        col++;
                    }

                    var x = ((long)runStart + quietZone) * moduleWidth;
                    var y = (long)row * moduleHeight;
                    var width = ((long)col - runStart) * moduleWidth;
                    var xStr = x.ToString(CultureInfo.InvariantCulture);
                    var yStr = y.ToString(CultureInfo.InvariantCulture);
                    var widthStr = width.ToString(CultureInfo.InvariantCulture);

                    sb.Append('M');
                    sb.Append(xStr);
                    sb.Append(' ');
                    sb.Append(yStr);
                    sb.Append('h');
                    sb.Append(widthStr);
                    sb.Append('v');
                    sb.Append(moduleHeightStr);
                    sb.Append('h');
                    sb.Append((-width).ToString(CultureInfo.InvariantCulture));
                    sb.Append('z');
                }
                else
                {
                    col++;
                }
            }
        }

        sb.Append("\"/></svg>");
        return sb.ToString();
    }
}
