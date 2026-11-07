namespace Meziantou.Framework.Barcodes.Internal;

/// <summary>
/// Fills one packed scanline of a two-colour indexed PNG.
/// </summary>
/// <param name="row">
/// The packed scanline. It is cleared before each call and holds one bit per pixel, most
/// significant bit leftmost; set a bit to make that pixel dark.
/// </param>
/// <param name="rowIndex">The zero-based row being rendered.</param>
/// <returns>
/// The number of consecutive rows, starting at <paramref name="rowIndex"/>, that are identical to
/// the scanline just filled. It must be at least 1. The writer reuses the scanline for those rows
/// and asks for the next one after them.
/// </returns>
internal delegate int PngRowRenderer(Span<byte> row, int rowIndex);
