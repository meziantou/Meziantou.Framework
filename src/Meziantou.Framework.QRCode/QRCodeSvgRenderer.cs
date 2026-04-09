using System.Globalization;
using System.Security;
using System.Text;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a QR code as an SVG string.
/// </summary>
public static class QRCodeSvgRenderer
{
    /// <summary>Renders the QR code as an SVG string with default options.</summary>
    public static string ToSvg(this QRCode qrCode)
    {
        return ToSvg(qrCode, new QRCodeSvgOptions());
    }

    /// <summary>Renders the QR code as an SVG string with the specified options.</summary>
    public static string ToSvg(this QRCode qrCode, QRCodeSvgOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.DarkColor);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.LightColor);

        var moduleSize = options.ModuleSize;
        if (moduleSize <= 0)
            throw new ArgumentOutOfRangeException("options.ModuleSize", options.ModuleSize, "ModuleSize must be greater than 0.");

        var quietZone = options.QuietZoneModules;
        if (quietZone < 0)
            throw new ArgumentOutOfRangeException("options.QuietZoneModules", options.QuietZoneModules, "QuietZoneModules must be greater than or equal to 0.");

        var totalWidth = ((long)qrCode.Width + (2L * quietZone)) * moduleSize;
        var totalHeight = ((long)qrCode.Height + (2L * quietZone)) * moduleSize;
        var totalWidthStr = totalWidth.ToString(CultureInfo.InvariantCulture);
        var totalHeightStr = totalHeight.ToString(CultureInfo.InvariantCulture);
        var moduleSizeStr = moduleSize.ToString(CultureInfo.InvariantCulture);

        var sb = new StringBuilder(capacity: 1024);

        // SVG header
        sb.Append("<svg xmlns=\"http://www.w3.org/2000/svg\" viewBox=\"0 0 ");
        sb.Append(totalWidthStr);
        sb.Append(' ');
        sb.Append(totalHeightStr);
        sb.Append("\">");

        // Background
        sb.Append("<rect width=\"");
        sb.Append(totalWidthStr);
        sb.Append("\" height=\"");
        sb.Append(totalHeightStr);
        sb.Append("\" fill=\"");
        sb.Append(SecurityElement.Escape(options.LightColor));
        sb.Append("\"/>");

        // Dark modules as a single path
        sb.Append("<path fill=\"");
        sb.Append(SecurityElement.Escape(options.DarkColor));
        sb.Append("\" d=\"");

        for (var row = 0; row < qrCode.Height; row++)
        {
            var col = 0;
            while (col < qrCode.Width)
            {
                if (qrCode[row, col])
                {
                    // Find the run of consecutive dark modules in this row
                    var runStart = col;
                    while (col < qrCode.Width && qrCode[row, col])
                    {
                        col++;
                    }

                    var x = ((long)runStart + quietZone) * moduleSize;
                    var y = ((long)row + quietZone) * moduleSize;
                    var width = ((long)col - runStart) * moduleSize;

                    sb.Append('M');
                    sb.Append(x.ToString(CultureInfo.InvariantCulture));
                    sb.Append(' ');
                    sb.Append(y.ToString(CultureInfo.InvariantCulture));
                    sb.Append('h');
                    sb.Append(width.ToString(CultureInfo.InvariantCulture));
                    sb.Append('v');
                    sb.Append(moduleSizeStr);
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
