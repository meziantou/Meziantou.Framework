namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The compression used by <see cref="BmpEncoder"/>.</summary>
public enum BmpCompression
{
    /// <summary>Uncompressed rows padded to four bytes. This is the default and the most widely readable output.</summary>
    None = 0,

    /// <summary>
    /// Run-length encoded rows: <c>BI_RLE8</c> for <see cref="BmpPixelLayout.Indexed8"/> and <c>BI_RLE4</c> for
    /// <see cref="BmpPixelLayout.Indexed4"/>, the only layouts the BMP run-length schemes store. The decoded pixels are
    /// identical to <see cref="None"/>: the encoding is lossless. Runs never cross a row, every pixel is stored (no
    /// delta escape is written), and the output can be larger than <see cref="None"/> for images without repeated pixels.
    /// </summary>
    RunLength = 1,
}
