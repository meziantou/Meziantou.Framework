namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>The data of each symbol of a structured append sequence.</summary>
/// <param name="Parts">The segments of each symbol, in order.</param>
/// <param name="Version">The version of every symbol.</param>
/// <param name="Parity">The XOR of the bytes of the data.</param>
internal sealed record QRStructuredAppendPlan(QRCodeSegment[][] Parts, int Version, byte Parity);
