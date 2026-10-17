namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// The Adam7 pass geometry of the W3C PNG specification, section 8.2, shared by the decoder and the encoder: pass
/// <c>p</c> holds the pixels at <c>(x0 + i * dx, y0 + j * dy)</c>. A pass whose width or height is zero has no scanline at
/// all (not even a filter byte).
/// </summary>
internal static class PngInterlace
{
    /// <summary>The number of Adam7 passes.</summary>
    public const int PassCount = 7;

    // x0, y0, dx, dy of each pass
    private static ReadOnlySpan<byte> Adam7 => [0, 0, 8, 8, 4, 0, 8, 8, 0, 4, 4, 8, 2, 0, 4, 4, 0, 2, 2, 4, 1, 0, 2, 2, 0, 1, 1, 2];

    // The pass of each pixel of a block of 8x8 pixels, row by row
    private static ReadOnlySpan<byte> PixelPasses =>
    [
        0, 5, 3, 5, 1, 5, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        4, 5, 4, 5, 4, 5, 4, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        2, 5, 3, 5, 2, 5, 3, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
        4, 5, 4, 5, 4, 5, 4, 5,
        6, 6, 6, 6, 6, 6, 6, 6,
    ];

    // The width and the height of the blocks of pixels that the passes up to each pass hold one pixel of
    private static ReadOnlySpan<byte> BlockSizes => [8, 8, 4, 8, 4, 4, 2, 4, 2, 2, 1, 2, 1, 1];

    /// <summary>Gets the origin and the steps of an Adam7 pass.</summary>
    /// <param name="pass">The pass index, 0 to 6.</param>
    /// <returns>The first column and row and the column and row steps.</returns>
    public static (int X0, int Y0, int Dx, int Dy) GetPass(int pass)
    {
        var adam7 = Adam7.Slice(pass * 4, 4);
        return (adam7[0], adam7[1], adam7[2], adam7[3]);
    }

    /// <summary>Gets the size of an Adam7 pass of an image.</summary>
    /// <param name="width">The image width.</param>
    /// <param name="height">The image height.</param>
    /// <param name="pass">The pass index, 0 to 6.</param>
    /// <returns>The pass size; <c>(0, 0)</c> for an empty pass.</returns>
    public static (int Width, int Height) GetPassSize(int width, int height, int pass)
    {
        var (x0, y0, dx, dy) = GetPass(pass);
        var passWidth = width > x0 ? ((width - x0 - 1) / dx) + 1 : 0;
        var passHeight = height > y0 ? ((height - y0 - 1) / dy) + 1 : 0;
        return passWidth == 0 || passHeight == 0 ? (0, 0) : (passWidth, passHeight);
    }

    /// <summary>Gets the pass that holds a pixel.</summary>
    /// <param name="x">The column of the pixel.</param>
    /// <param name="y">The row of the pixel.</param>
    /// <returns>The pass index, 0 to 6.</returns>
    public static int GetPassOfPixel(int x, int y) => PixelPasses[((y & 7) << 3) | (x & 7)];

    /// <summary>
    /// Gets the size of the blocks of pixels that the first passes hold one pixel of: the pixel at the top left of each
    /// block.
    /// </summary>
    /// <param name="passCount">The number of passes, 1 to 7.</param>
    /// <returns>The width and the height of a block.</returns>
    public static (int Width, int Height) GetBlockSize(int passCount)
    {
        var size = BlockSizes.Slice((passCount - 1) * 2, 2);
        return (size[0], size[1]);
    }
}
