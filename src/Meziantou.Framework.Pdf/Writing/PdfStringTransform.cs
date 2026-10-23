namespace Meziantou.Framework.Pdf.Writing;

/// <summary>Transforms the bytes of a string when it is written: the encryption of the strings of an object (ISO 32000-2, 7.6.2).</summary>
/// <param name="value">The bytes of the string.</param>
/// <returns>The bytes to write.</returns>
internal delegate byte[] PdfStringTransform(ReadOnlySpan<byte> value);
