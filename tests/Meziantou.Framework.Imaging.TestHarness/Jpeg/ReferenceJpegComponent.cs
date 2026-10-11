namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>A frame component of a <see cref="ReferenceJpeg"/> file, with the tables its scan selects.</summary>
/// <param name="Id">The component identifier.</param>
/// <param name="H">The horizontal sampling factor.</param>
/// <param name="V">The vertical sampling factor.</param>
/// <param name="QuantizationTable">The quantization table selector.</param>
/// <param name="DcTable">The DC Huffman table selector of the scan.</param>
/// <param name="AcTable">The AC Huffman table selector of the scan.</param>
public sealed record ReferenceJpegComponent(int Id, int H, int V, int QuantizationTable, int DcTable, int AcTable);
