using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a linear barcode using Unicode block characters.
/// </summary>
public static class BarcodeConsoleRenderer
{
    private const int DefaultQuietZoneModules = 10;

    /// <summary>Renders the barcode to <see cref="Console.Out"/>.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteToConsole(this Barcode barcode, ConsoleRenderOptions? options = null)
    {
        WriteTo(barcode, Console.Out, options);
    }

    /// <summary>Renders the barcode to a <see cref="TextWriter"/> using Unicode block characters.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteTo(this Barcode barcode, TextWriter writer, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToConsoleString(barcode, options));
    }

    /// <summary>Renders the barcode as a string using Unicode block characters.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The lines of the barcode, without trailing spaces and without a final new line.</returns>
    /// <remarks>
    /// Each module is <see cref="ConsoleRenderOptions.ModuleWidth"/> characters wide. The bars of a barcode that is not
    /// stacked are <see cref="ConsoleRenderOptions.BarHeight"/> lines high; the rows of a stacked barcode are drawn as the
    /// rows of a two-dimensional symbol, two per line. The quiet zone is on the left and right sides only.
    /// </remarks>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static string ToConsoleString(this Barcode barcode, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        options ??= new ConsoleRenderOptions();
        ConsoleBlockRenderer.Validate(options);

        var quietZone = options.QuietZoneModules ?? DefaultQuietZoneModules;
        var moduleHeight = barcode.IsStacked ? options.ModuleHeight : options.BarHeight * 2L;
        if (moduleHeight > int.MaxValue)
            throw ConsoleBlockRenderer.CreateTooLargeException(nameof(options));

        return ConsoleBlockRenderer.Render(barcode.Modules, quietZone, verticalQuietZone: 0, options.ModuleWidth, (int)moduleHeight, options.InvertColors, options.ShowText ? barcode.Text : null, nameof(options));
    }
}
