namespace Meziantou.Framework.Imaging.FuzzTests;

/// <summary>A raw AV1 stream of the corpus that a fuzz seed wraps, with what <see cref="FuzzAvifContainer.Wrap"/> needs to wrap it again.</summary>
/// <param name="Data">The open bitstream units, with their size fields.</param>
/// <param name="Width">The width of the frames.</param>
/// <param name="Height">The height of the frames.</param>
/// <param name="BitDepth">The bit depth: 8, 10 or 12.</param>
/// <param name="Subsampling">The chroma format.</param>
internal sealed record FuzzAv1Stream(byte[] Data, int Width, int Height, int BitDepth, string Subsampling);
