namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>The exact (unrounded) quantized coefficients of one component computed by <see cref="JpegEncoderModel"/>.</summary>
/// <param name="BlocksX">The number of block columns.</param>
/// <param name="BlocksY">The number of block rows.</param>
/// <param name="Values">64 values per block (natural order), blocks in row-major order.</param>
public sealed record ReferenceJpegModelBlocks(int BlocksX, int BlocksY, ReadOnlyMemory<double> Values);
