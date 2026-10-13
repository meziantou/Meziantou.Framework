namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>A component of a <see cref="ReferenceJpegScan"/>.</summary>
/// <param name="Index">The index of the component in the frame.</param>
/// <param name="DcTable">The DC Huffman table selector.</param>
/// <param name="AcTable">The AC Huffman table selector.</param>
public sealed record ReferenceJpegScanComponent(int Index, int DcTable, int AcTable);
