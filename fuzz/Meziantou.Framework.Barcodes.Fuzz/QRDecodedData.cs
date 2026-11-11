namespace Meziantou.Framework.Barcodes.Fuzz;

/// <summary>The data of a standard QR code.</summary>
/// <param name="StructuredAppend">The structured append header, or <see langword="null"/>.</param>
/// <param name="Segments">The ECI designators and data segments, in order.</param>
internal sealed record QRDecodedData((int Index, int Count, int Parity)? StructuredAppend, List<QRDecodedSegment> Segments);
