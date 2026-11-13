namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents a linear barcode read by <see cref="BarcodeDecoder"/>: its type, its text, and where it is on the scan lines.
/// </summary>
public sealed class BarcodeDecodeResult
{
    internal BarcodeDecodeResult(BarcodeType type, string text, int firstScanLine, int lastScanLine, long start, long end)
    {
        Type = type;
        Text = text;
        FirstScanLine = firstScanLine;
        LastScanLine = lastScanLine;
        Start = start;
        End = end;
    }

    /// <summary>Gets the type of the barcode.</summary>
    /// <remarks>
    /// An EAN-13 symbol whose first digit is 0 is reported as <see cref="BarcodeType.UpcA"/>: the two are the same bars. The
    /// type of a GS1 DataBar Stacked or Stacked Omnidirectional symbol comes from the heights of its rows.
    /// </remarks>
    public BarcodeType Type { get; }

    /// <summary>
    /// Gets the text of the barcode, as <see cref="Barcode.Text"/> gives it for a generated barcode: the check characters
    /// of Code 39 are part of the text, the start and stop characters of Codabar too, the add-on of an EAN or UPC symbol
    /// follows a space, and GS1 data is its human-readable interpretation (each application identifier in parentheses).
    /// </summary>
    public string Text { get; }

    /// <summary>Gets the index of the first scan line that crosses the barcode.</summary>
    public int FirstScanLine { get; }

    /// <summary>Gets the index of the last scan line that crosses the barcode.</summary>
    public int LastScanLine { get; }

    /// <summary>Gets the position of the first bar of the barcode on its scan lines, in the unit of the widths of the runs.</summary>
    public long Start { get; }

    /// <summary>Gets the position of the end of the last bar of the barcode on its scan lines, in the unit of the widths of the runs.</summary>
    public long End { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
