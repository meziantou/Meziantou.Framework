namespace Meziantou.Framework.Internal;

/// <summary>
/// Fills one packed scanline of a two-colour indexed PNG.
/// </summary>
/// <param name="row">
/// The packed scanline. It is cleared before each call and holds one bit per pixel, most
/// significant bit leftmost; set a bit to make that pixel dark.
/// </param>
/// <param name="rowIndex">The zero-based row being rendered.</param>
internal delegate void PngRowRenderer(Span<byte> row, int rowIndex);
