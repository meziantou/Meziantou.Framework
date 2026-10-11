namespace Meziantou.Framework.Imaging.TestHarness.Gif;

/// <summary>One image of a GIF file read by <see cref="ReferenceGif"/>.</summary>
/// <param name="Left">The left position on the logical screen.</param>
/// <param name="Top">The top position on the logical screen.</param>
/// <param name="Width">The image width.</param>
/// <param name="Height">The image height.</param>
/// <param name="Interlaced">Whether the rows are stored in interlaced order.</param>
/// <param name="LocalColorTable">The local color table (RGB triplets), or <see langword="null"/>.</param>
/// <param name="LzwMinimumCodeSize">The LZW minimum code size.</param>
/// <param name="Control">The preceding Graphic Control Extension, or <see langword="null"/>.</param>
/// <param name="Indices">The color indices in display order (deinterlaced).</param>
public sealed record ReferenceGifImage(int Left, int Top, int Width, int Height, bool Interlaced, ReadOnlyMemory<byte>? LocalColorTable, int LzwMinimumCodeSize, ReferenceGifControl? Control, ReadOnlyMemory<byte> Indices)
{
    /// <summary>Gets a value indicating whether the image covers the logical screen of the given size at offset 0.</summary>
    /// <param name="width">The screen width.</param>
    /// <param name="height">The screen height.</param>
    /// <returns><see langword="true"/> for a full-canvas image.</returns>
    public bool CoversCanvas(int width, int height) => Left == 0 && Top == 0 && Width == width && Height == height;
}
