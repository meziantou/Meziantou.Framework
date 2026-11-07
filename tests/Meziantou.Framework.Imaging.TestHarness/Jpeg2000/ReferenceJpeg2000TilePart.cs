namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>A tile-part of a JPEG 2000 codestream (SOT marker segment).</summary>
/// <param name="Tile">The index of the tile.</param>
/// <param name="Part">The index of the tile-part in its tile.</param>
/// <param name="PartCount">The number of tile-parts of the tile, or 0 when the tile-part does not give it.</param>
/// <param name="Offset">The offset of the SOT marker in the codestream.</param>
/// <param name="Length">The length of the tile-part, from the SOT marker.</param>
/// <param name="DataLength">The number of bytes that follow the SOD marker.</param>
/// <param name="Markers">The marker segments of the tile-part header, in order (the second byte of each marker).</param>
public sealed record ReferenceJpeg2000TilePart(int Tile, int Part, int PartCount, int Offset, int Length, int DataLength, IReadOnlyList<int> Markers);
