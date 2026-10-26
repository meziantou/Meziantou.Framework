namespace Meziantou.Framework.Barcodes.InteropTests;

/// <summary>A barcode found by ZXingReader.</summary>
/// <param name="FilePath">The file the barcode was found in.</param>
/// <param name="Text">The decoded text.</param>
/// <param name="Bytes">The decoded bytes.</param>
/// <param name="Format">The format, such as <c>QR Code</c> or <c>EAN-13</c>.</param>
/// <param name="Version">The version of a QR code (<c>1</c>, <c>M2</c>, <c>R7x43</c>), or <see langword="null"/>.</param>
/// <param name="ErrorCorrectionLevel">The error correction level of a QR code (<c>L</c>, <c>M</c>, <c>Q</c>, <c>H</c>), or <see langword="null"/>.</param>
/// <param name="DataMask">The mask of a QR code, or <see langword="null"/>.</param>
/// <param name="Error">The error ZXingReader reports, or <see langword="null"/>.</param>
internal sealed record ZXingResult(string FilePath, string Text, byte[] Bytes, string Format, string? Version, string? ErrorCorrectionLevel, int? DataMask, string? Error);
