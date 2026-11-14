using Meziantou.Framework.Barcodes.Internal;

namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Provides methods to render a QR code using Unicode block characters.
/// </summary>
public static class QRCodeConsoleRenderer
{
    private const int DefaultQuietZoneModules = 2;

    /// <summary>Renders the QR code to <see cref="Console.Out"/>.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteToConsole(this QRCode qrCode, ConsoleRenderOptions? options = null)
    {
        WriteTo(qrCode, Console.Out, options);
    }

    /// <summary>Renders the QR code to a <see cref="TextWriter"/> using Unicode block characters.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="writer">The destination.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> or <paramref name="writer"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static void WriteTo(this QRCode qrCode, TextWriter writer, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        ArgumentNullException.ThrowIfNull(writer);
        writer.Write(ToConsoleString(qrCode, options));
    }

    /// <summary>Renders the QR code as a string using Unicode block characters, two rows of modules per line.</summary>
    /// <param name="qrCode">The QR code to render.</param>
    /// <param name="options">The rendering options, or <see langword="null"/> for the defaults.</param>
    /// <returns>The lines of the QR code, without trailing spaces and without a final new line.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="qrCode"/> is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">An option is out of range, or the text is too large for a string.</exception>
    public static string ToConsoleString(this QRCode qrCode, ConsoleRenderOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(qrCode);
        options ??= new ConsoleRenderOptions();
        ConsoleBlockRenderer.Validate(options);

        var quietZone = options.QuietZoneModules ?? DefaultQuietZoneModules;
        return ConsoleBlockRenderer.Render(qrCode.Modules, quietZone, quietZone, options.ModuleWidth, options.ModuleHeight, options.InvertColors, text: null, nameof(options));
    }
}
