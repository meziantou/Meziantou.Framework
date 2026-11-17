using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a two-dimensional barcode (Data Matrix, PDF417, Aztec) using Unicode block characters.
/// </summary>
public static class Barcode2DConsoleRenderer
{
    private const int DefaultQuietZoneModules = 2;

    /// <summary>Renders the barcode to <see cref="Console.Out"/>.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteToConsole(this Barcode2D barcode, ConsoleRenderOptions? options = null)
    {
        WriteTo(barcode, Console.Out, options);
    }

    /// <summary>Renders the barcode to a <see cref="TextWriter"/> using Unicode block characters.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteTo(this Barcode2D barcode, TextWriter writer, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToConsoleString(barcode, options));
    }

    /// <summary>Renders the barcode as a string using Unicode block characters, two rows of modules per line.</summary>
    /// <param name="barcode">The barcode to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The lines of the barcode, without trailing spaces and without a final new line.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="barcode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static string ToConsoleString(this Barcode2D barcode, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(barcode);
        options ??= new ConsoleRenderOptions();
        ConsoleBlockRenderer.Validate(options);

        var quietZone = options.QuietZoneModules ?? DefaultQuietZoneModules;
        return ConsoleBlockRenderer.Render(barcode.Modules, quietZone, quietZone, options.ModuleWidth, options.ModuleHeight, options.InvertColors, text: null, nameof(options));
    }
}
