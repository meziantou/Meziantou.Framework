namespace Meziantou.Framework.Barcodes;

/// <summary>
/// Represents the content of a QR code read by <see cref="QRCodeDecoder"/>: its data and the parameters of the symbol.
/// </summary>
public sealed class QRCodeDecodeResult
{
    internal QRCodeDecodeResult(QRCodeType type, int version, ErrorCorrectionLevel errorCorrectionLevel, int? maskPattern, QRCodeStructuredAppend? structuredAppend, IReadOnlyList<QRCodeSegment> segments, string text, int correctedErrorCount, bool isMirrored)
    {
        Type = type;
        Version = version;
        ErrorCorrectionLevel = errorCorrectionLevel;
        MaskPattern = maskPattern;
        StructuredAppend = structuredAppend;
        Segments = segments;
        Text = text;
        CorrectedErrorCount = correctedErrorCount;
        IsMirrored = isMirrored;
    }

    /// <summary>Gets the type of the QR code.</summary>
    public QRCodeType Type { get; }

    /// <summary>Gets the version of the QR code: 1 to 40 for a standard QR code, 1 to 4 for a Micro QR code (M1 to M4), 1 to 32 for an rMQR code.</summary>
    public int Version { get; }

    /// <summary>Gets the error correction level of the symbol. Micro QR version M1, which detects errors only, reports <see cref="ErrorCorrectionLevel.L"/>.</summary>
    public ErrorCorrectionLevel ErrorCorrectionLevel { get; }

    /// <summary>Gets the data mask pattern of the symbol: 0 to 7 in a standard QR code, 0 to 3 in a Micro QR code, or <see langword="null"/> for an rMQR code, which has a single mask.</summary>
    public int? MaskPattern { get; }

    /// <summary>
    /// Gets the place of the symbol in a structured append sequence, or <see langword="null"/> when the symbol holds all
    /// of its data. Use <see cref="QRCodeDecoder.MergeStructuredAppend(IEnumerable{QRCodeDecodeResult})"/> to put the
    /// data of a sequence back together.
    /// </summary>
    public QRCodeStructuredAppend? StructuredAppend { get; }

    /// <summary>Gets the segments of the data, in order, with their ECI designators.</summary>
    public IReadOnlyList<QRCodeSegment> Segments { get; }

    /// <summary>
    /// Gets the text of the data. The byte segments are decoded with the character set of the ECI designator that
    /// precedes them. Without one, they are decoded as UTF-8 when they are valid UTF-8, and as ISO/IEC 8859-1 otherwise.
    /// </summary>
    public string Text { get; }

    /// <summary>Gets the number of codewords that the error correction fixed.</summary>
    public int CorrectedErrorCount { get; }

    /// <summary>Gets whether the symbol was read mirrored: its rows and columns were swapped. Only square symbols (standard and Micro QR codes) are read mirrored.</summary>
    public bool IsMirrored { get; }

    /// <inheritdoc/>
    public override string ToString() => Text;
}
