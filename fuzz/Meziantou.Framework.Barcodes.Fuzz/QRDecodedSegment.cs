namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>A segment of a standard QR code.</summary>
/// <param name="Mode">The mode.</param>
/// <param name="Data">The bytes: the ASCII characters of a numeric or alphanumeric segment, the Shift JIS codes of a Kanji segment.</param>
/// <param name="EciAssignmentNumber">The assignment number of an ECI designator.</param>
internal sealed record QRDecodedSegment(QRCodeSegmentMode Mode, byte[] Data, int EciAssignmentNumber);
