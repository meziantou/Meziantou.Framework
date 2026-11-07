namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a QR code using Unicode block characters.
/// </summary>
public static class QRCodeConsoleRenderer
{
    private const char FullBlock = '\u2588';     // Both dark
    private const char UpperHalfBlock = '\u2580'; // Top dark, bottom light
    private const char LowerHalfBlock = '\u2584'; // Top light, bottom dark

    // Maximum length of a string in .NET
    private const long MaxStringLength = 0x3FFFFFDF;

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
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfNegative(options.QuietZoneModules);

        var quietZone = options.QuietZoneModules;
        var moduleWidth = options.ModuleWidth;
        var moduleHeight = options.ModuleHeight;

        // Compute the output size with 64-bit arithmetic so large options cannot wrap around.
        // Each factor is bounded before it is multiplied, so the products below cannot overflow a long.
        var unscaledWidth = qrCode.Width + (2L * quietZone);
        var unscaledHeight = qrCode.Height + (2L * quietZone);
        if (unscaledWidth > MaxStringLength || unscaledHeight > int.MaxValue)
        {
            throw CreateTooLargeException(nameof(options));
        }

        var lineLength = unscaledWidth * moduleWidth;
        var totalHeightLong = unscaledHeight * moduleHeight;
        if (lineLength > MaxStringLength || totalHeightLong > int.MaxValue || (lineLength + Environment.NewLine.Length) * ((totalHeightLong + 1) / 2) > MaxStringLength)
        {
            throw CreateTooLargeException(nameof(options));
        }

        var totalWidth = (int)unscaledWidth;
        var totalHeight = (int)totalHeightLong;

        var sb = new StringBuilder();

        // Process two terminal rows at a time using half-block characters
        for (var row = 0; row < totalHeight; row += 2)
        {
            var lineStart = sb.Length;
            var topSourceRow = (row / moduleHeight) - quietZone;
            var hasBottomRow = row + 1 < totalHeight;
            var bottomSourceRow = hasBottomRow ? ((row + 1) / moduleHeight) - quietZone : 0;
            for (var col = 0; col < totalWidth; col++)
            {
                var sourceCol = col - quietZone;
                var topDark = IsDark(qrCode, topSourceRow, sourceCol, options.InvertColors);
                var bottomDark = hasBottomRow && IsDark(qrCode, bottomSourceRow, sourceCol, options.InvertColors);

                var block = (topDark, bottomDark) switch
                {
                    (true, true) => FullBlock,
                    (true, false) => UpperHalfBlock,
                    (false, true) => LowerHalfBlock,
                    _ => ' ',
                };

                sb.Append(block, moduleWidth);
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

    private static ArgumentOutOfRangeException CreateTooLargeException(string paramName)
    {
        return new ArgumentOutOfRangeException(paramName, "The rendered QR code is too large to be represented as a string. Reduce the module size or the quiet zone.");
    }

    private static bool IsDark(QRCode qrCode, int row, int col, bool invert)
    {
        var isDark = row >= 0 && row < qrCode.Height && col >= 0 && col < qrCode.Width && qrCode[row, col];

        return invert ? !isDark : isDark;
    }
}
