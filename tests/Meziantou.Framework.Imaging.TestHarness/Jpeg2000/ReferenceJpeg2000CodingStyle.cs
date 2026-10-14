namespace Meziantou.Framework.Imaging.TestHarness.Jpeg2000;

/// <summary>The coding style of a COD or COC marker segment of a JPEG 2000 codestream.</summary>
/// <param name="Component">The component of a COC marker segment, or -1 for a COD marker segment.</param>
/// <param name="DecompositionLevels">The number of decomposition levels.</param>
/// <param name="CodeBlockWidth">The width of a code-block.</param>
/// <param name="CodeBlockHeight">The height of a code-block.</param>
/// <param name="CodeBlockStyle">The style of the coding passes: 1 selective bypass, 2 reset, 4 termination on each pass, 8 vertically causal, 16 predictable termination, 32 segmentation symbols, 64 high-throughput, 128 mixed.</param>
/// <param name="IsReversible">Whether the wavelet transformation is the reversible 5-3 one.</param>
/// <param name="HasPrecincts">Whether the marker segment gives the sizes of the precincts.</param>
/// <param name="IsInTilePartHeader">Whether the marker segment is in a tile-part header.</param>
public sealed record ReferenceJpeg2000CodingStyle(int Component, int DecompositionLevels, int CodeBlockWidth, int CodeBlockHeight, int CodeBlockStyle, bool IsReversible, bool HasPrecincts, bool IsInTilePartHeader);
