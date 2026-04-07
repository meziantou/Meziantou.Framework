using System.Text;

namespace Meziantou.Framework;

/// <summary>
/// Provides methods to render a QR code using Unicode block characters.
/// </summary>
public static class QRCodeConsoleRenderer
{
    private const char FullBlock = '\u2588';     // Both dark
    private const char UpperHalfBlock = '\u2580'; // Top dark, bottom light
    private const char LowerHalfBlock = '\u2584'; // Top light, bottom dark

    /// <summary>Renders the QR code to <see cref="Console.Out"/> with default options.</summary>
    public static void WriteToConsole(this QRCode qrCode)
    {
        WriteTo(qrCode, Console.Out, new QRCodeConsoleOptions());
    }

    /// <summary>Renders the QR code to <see cref="Console.Out"/> with the specified options.</summary>
    public static void WriteToConsole(this QRCode qrCode, QRCodeConsoleOptions options)
    {
        WriteTo(qrCode, Console.Out, options);
    }

    /// <summary>Renders the QR code to a TextWriter using Unicode block characters with default options.</summary>
    public static void WriteTo(this QRCode qrCode, TextWriter writer)
    {
        WriteTo(qrCode, writer, new QRCodeConsoleOptions());
    }

    /// <summary>Renders the QR code to a TextWriter using Unicode block characters.</summary>
    public static void WriteTo(this QRCode qrCode, TextWriter writer, QRCodeConsoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(writer);
        ArgumentNullException.ThrowIfNull(options);

        writer.Write(ToConsoleString(qrCode, options));
    }

    /// <summary>Renders the QR code as a string using Unicode block characters with default options.</summary>
    public static string ToConsoleString(this QRCode qrCode)
    {
        return ToConsoleString(qrCode, new QRCodeConsoleOptions());
    }

    /// <summary>Renders the QR code as a string using Unicode block characters.</summary>
    public static string ToConsoleString(this QRCode qrCode, QRCodeConsoleOptions options)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(options);

        var quietZone = options.QuietZoneModules;
        var totalWidth = qrCode.Width + (2 * quietZone);
        var totalHeight = qrCode.Height + (2 * quietZone);

        var sb = new StringBuilder();

        // Process two rows at a time using half-block characters
        for (var row = 0; row < totalHeight; row += 2)
        {
            var lineStart = sb.Length;
            for (var col = 0; col < totalWidth; col++)
            {
                var topDark = IsDark(qrCode, row - quietZone, col - quietZone, options.InvertColors);
                var bottomDark = row + 1 < totalHeight && IsDark(qrCode, row + 1 - quietZone, col - quietZone, options.InvertColors);

                if (topDark && bottomDark)
                {
                    sb.Append(FullBlock);
                }
                else if (topDark)
                {
                    sb.Append(UpperHalfBlock);
                }
                else if (bottomDark)
                {
                    sb.Append(LowerHalfBlock);
                }
                else
                {
                    sb.Append(' ');
                }
            }

            // Trim trailing spaces from the line
            while (sb.Length > lineStart && sb[sb.Length - 1] == ' ')
            {
                sb.Length--;
            }

            if (row + 2 < totalHeight)
            {
                sb.AppendLine();
            }
        }

        return sb.ToString();
    }

    private static bool IsDark(QRCode qrCode, int row, int col, bool invert)
    {
        var isDark = row >= 0 && row < qrCode.Height && col >= 0 && col < qrCode.Width && qrCode[row, col];

        return invert ? !isDark : isDark;
    }
}
