namespace Meziantou.Framework.Imaging.TestHarness.Jpeg;

/// <summary>The quantized coefficients of one component decoded by <see cref="ReferenceJpeg.DecodeCoefficients"/>.</summary>
/// <param name="BlocksX">The number of block columns.</param>
/// <param name="BlocksY">The number of block rows.</param>
/// <param name="Coefficients">64 coefficients per block (natural order), blocks in row-major order.</param>
public sealed record ReferenceJpegBlocks(int BlocksX, int BlocksY, ReadOnlyMemory<int> Coefficients)
{
    /// <summary>
    /// Gets the number of block columns whose AC coefficients are coded: every column, except in a progressive file, whose
    /// AC scans only code the blocks that hold samples of the component (the others have a DC coefficient only).
    /// </summary>
    public int AcBlocksX { get; init; } = BlocksX;

    /// <summary>Gets the number of block rows whose AC coefficients are coded (see <see cref="AcBlocksX"/>).</summary>
    public int AcBlocksY { get; init; } = BlocksY;

    /// <summary>Gets the coefficients of a block.</summary>
    /// <param name="x">The block column.</param>
    /// <param name="y">The block row.</param>
    /// <returns>64 coefficients, natural order.</returns>
    public ReadOnlySpan<int> GetBlock(int x, int y) => Coefficients.Span.Slice(((y * BlocksX) + x) * 64, 64);
}
