namespace Meziantou.Framework.Imaging.TestHarness.WebP;

/// <summary>A RIFF chunk read by <see cref="ReferenceWebP"/>.</summary>
/// <param name="FourCC">The chunk identifier (for example <c>VP8X</c>, <c>VP8L</c>, <c>XMP </c>).</param>
/// <param name="Offset">The file offset of the chunk header.</param>
/// <param name="Data">The chunk payload (without padding).</param>
public sealed record ReferenceWebPChunk(string FourCC, int Offset, ReadOnlyMemory<byte> Data);
