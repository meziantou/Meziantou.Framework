namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Options for rendering a QR code, a two-dimensional barcode or a linear barcode as text, with Unicode block characters.
/// </summary>
public sealed class ConsoleRenderOptions
{
    /// <summary>Gets or sets whether to invert colors (light-on-dark), for terminals with a dark background. Default is false.</summary>
    public bool InvertColors { get; set; }

    /// <summary>
    /// Gets or sets the number of quiet zone modules. Default is <see langword="null"/>: 2 around QR codes and
    /// two-dimensional barcodes, 10 on the left and right sides of linear barcodes.
    /// </summary>
    public int? QuietZoneModules { get; set; }

    /// <summary>Gets or sets the horizontal size of each rendered module in text cells. Default is 1.</summary>
    public int ModuleWidth { get; set; } = 1;

    /// <summary>Gets or sets the vertical size of each rendered module, in half text cells. Default is 1.</summary>
    /// <remarks>A line of text holds two rows of modules. Linear barcodes that are not stacked use <see cref="BarHeight"/> instead.</remarks>
    public int ModuleHeight { get; set; } = 1;

    /// <summary>Gets or sets the height of the bars of a linear barcode that is not stacked, in lines of text. Default is 4.</summary>
    public int BarHeight { get; set; } = 4;

    /// <summary>Gets or sets whether the human-readable text (<see cref="Barcode.Text"/>) is written under a linear barcode. Default is false.</summary>
    /// <remarks>
    /// The text is centered under the bars, on its own line, in the font of the terminal. Its control characters (such as
    /// the new lines and escape characters a Code 128 barcode can encode) are written as <c>?</c>, so the text cannot
    /// break the layout or send commands to the terminal.
    /// </remarks>
    public bool ShowText { get; set; }
}
