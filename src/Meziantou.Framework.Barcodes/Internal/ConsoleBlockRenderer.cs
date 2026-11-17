namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>Writes a grid of modules as lines of Unicode block characters, two rows of modules per line.</summary>
internal static class ConsoleBlockRenderer
{
    private const char FullBlock = '█';     // Both dark
    private const char UpperHalfBlock = '▀'; // Top dark, bottom light
    private const char LowerHalfBlock = '▄'; // Top light, bottom dark

    // Maximum length of a string in .NET
    private const long MaxStringLength = 0x3FFFFFDF;

    /// <summary>Renders the modules as text.</summary>
    /// <param name="modules">The modules, by row then column; <see langword="true"/> for dark.</param>
    /// <param name="horizontalQuietZone">The number of light modules on the left and right sides.</param>
    /// <param name="verticalQuietZone">The number of light modules on the top and bottom sides.</param>
    /// <param name="moduleWidth">The number of characters of a module.</param>
    /// <param name="moduleHeight">The number of half lines of a module.</param>
    /// <param name="invertColors">Whether dark and light are swapped.</param>
    /// <param name="text">A line of text centered under the modules, or <see langword="null"/>.</param>
    /// <exception cref="ArgumentOutOfRangeException">The text would be too large for a string.</exception>
    /// <param name="paramName">The parameter to blame when the text is too large.</param>
    public static string Render(bool[,] modules, int horizontalQuietZone, int verticalQuietZone, int moduleWidth, int moduleHeight, bool invertColors, string? text, string paramName)
    {
        var rows = modules.GetLength(0);
        var columns = modules.GetLength(1);

        // Compute the output size with 64-bit arithmetic so large options cannot wrap around.
        // Each factor is bounded before it is multiplied, so the products below cannot overflow a long.
        var unscaledWidth = columns + (2L * horizontalQuietZone);
        var unscaledHeight = rows + (2L * verticalQuietZone);
        if (unscaledWidth > MaxStringLength || unscaledHeight > int.MaxValue)
            throw CreateTooLargeException(paramName);

        var lineLength = unscaledWidth * moduleWidth;
        var totalHeightLong = unscaledHeight * moduleHeight;
        var textLength = text is null ? 0L : text.Length + Environment.NewLine.Length + Math.Max(0, (lineLength - text.Length) / 2);
        if (lineLength > MaxStringLength || totalHeightLong > int.MaxValue || ((lineLength + Environment.NewLine.Length) * ((totalHeightLong + 1) / 2)) + textLength > MaxStringLength)
            throw CreateTooLargeException(paramName);

        var totalWidth = (int)unscaledWidth;
        var totalHeight = (int)totalHeightLong;

        var sb = new StringBuilder();
        for (var row = 0; row < totalHeight; row += 2)
        {
            var lineStart = sb.Length;
            var topSourceRow = (row / moduleHeight) - verticalQuietZone;
            var hasBottomRow = row + 1 < totalHeight;
            var bottomSourceRow = hasBottomRow ? ((row + 1) / moduleHeight) - verticalQuietZone : 0;
            for (var col = 0; col < totalWidth; col++)
            {
                var sourceCol = col - horizontalQuietZone;
                var topDark = IsDark(modules, topSourceRow, sourceCol, invertColors);
                var bottomDark = hasBottomRow && IsDark(modules, bottomSourceRow, sourceCol, invertColors);

                var block = (topDark, bottomDark) switch
                {
                    (true, true) => FullBlock,
                    (true, false) => UpperHalfBlock,
                    (false, true) => LowerHalfBlock,
                    _ => ' ',
                };

                sb.Append(block, moduleWidth);
            }

            TrimTrailingSpaces(sb, lineStart);
            if (row + 2 < totalHeight)
            {
                sb.AppendLine();
            }
        }

        if (text is not null)
        {
            if (totalHeight > 0)
            {
                sb.AppendLine();
            }

            var lineStart = sb.Length;
            sb.Append(' ', (int)Math.Max(0, (lineLength - text.Length) / 2));
            PrintableText.Append(sb, text);
            TrimTrailingSpaces(sb, lineStart);
        }

        return sb.ToString();
    }

    private static void TrimTrailingSpaces(StringBuilder sb, int lineStart)
    {
        while (sb.Length > lineStart && sb[sb.Length - 1] == ' ')
        {
            sb.Length--;
        }
    }

    /// <summary>Validates the options shared by every symbol.</summary>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range.</exception>
    public static void Validate(ConsoleRenderOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleWidth, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.ModuleHeight, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(options.BarHeight, 1);
        if (options.QuietZoneModules is < 0)
            throw new ArgumentOutOfRangeException(nameof(options), options.QuietZoneModules, "The quiet zone cannot be negative.");
    }

    public static ArgumentOutOfRangeException CreateTooLargeException(string paramName)
    {
        return new ArgumentOutOfRangeException(paramName, "The rendered symbol is too large to be represented as a string. Reduce the module size or the quiet zone.");
    }

    private static bool IsDark(bool[,] modules, int row, int col, bool invert)
    {
        var isDark = row >= 0 && row < modules.GetLength(0) && col >= 0 && col < modules.GetLength(1) && modules[row, col];
        return invert ? !isDark : isDark;
    }
}
