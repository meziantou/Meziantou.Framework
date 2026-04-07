namespace Meziantou.Framework;

/// <summary>
/// Options for rendering a QR code to the console.
/// </summary>
public sealed class QRCodeConsoleOptions
{
    /// <summary>Gets or sets whether to invert colors (light-on-dark). Default is false.</summary>
    public bool InvertColors { get; set; }

    /// <summary>Gets or sets the number of quiet zone modules. Default is 2.</summary>
    public int QuietZoneModules { get; set; } = 2;
}
