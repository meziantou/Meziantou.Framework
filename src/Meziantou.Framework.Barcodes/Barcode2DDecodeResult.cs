namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents the content of a Data Matrix, PDF417 or Aztec symbol read by <see cref="Barcode2DDecoder"/>.
/// </summary>
public sealed class Barcode2DDecodeResult
{
    private readonly byte[] _bytes;

    internal Barcode2DDecodeResult(Barcode2DType type, string text, byte[] bytes, int? eciAssignmentNumber, bool isGs1, int correctedErrorCount)
    {
        Type = type;
        Text = text;
        _bytes = bytes;
        EciAssignmentNumber = eciAssignmentNumber;
        IsGs1 = isGs1;
        CorrectedErrorCount = correctedErrorCount;
    }

    /// <summary>Gets the type of the symbol.</summary>
    public Barcode2DType Type { get; }

    /// <summary>
    /// Gets the text of the data. The bytes are decoded with the character set of the ECI designator that precedes them;
    /// without one, as UTF-8 when they are valid UTF-8, and as ISO/IEC 8859-1 otherwise. The FNC1 characters that separate
    /// the elements of GS1 data are GS characters (U+001D). For GS1 data (<see cref="IsGs1"/>), the text is the
    /// human-readable interpretation: each application identifier in parentheses, followed by its data.
    /// </summary>
    public string Text { get; }

    /// <summary>
    /// Gets the bytes of the data, without the ECI designators. The FNC1 characters that separate the elements of GS1 data
    /// are GS bytes (0x1D); the FNC1 that marks GS1 data at the start of the symbol is not included.
    /// </summary>
    public ReadOnlyMemory<byte> Bytes => _bytes;

    /// <summary>Gets the assignment number of the first ECI designator of the data, or <see langword="null"/>.</summary>
    public int? EciAssignmentNumber { get; }

    /// <summary>Gets whether the data is GS1 element strings: the symbol starts with a FNC1 character.</summary>
    public bool IsGs1 { get; }

    /// <summary>Gets the number of codewords that the error correction fixed.</summary>
    public int CorrectedErrorCount { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
