namespace Meziantou.Framework.Imaging.Internals;

/// <summary>A Huffman table of a JPEG file.</summary>
/// <param name="IsAc">Whether the table codes AC coefficients.</param>
/// <param name="Slot">The destination of the table (0 to 3).</param>
/// <param name="IsLast">Whether the table ends its segment.</param>
/// <param name="Counts">The number of codes of each length, 1 to 16.</param>
/// <param name="Values">The symbols, in the order of their codes.</param>
internal sealed record JpegBitstreamHuffmanTable(bool IsAc, int Slot, bool IsLast, byte[] Counts, byte[] Values);
