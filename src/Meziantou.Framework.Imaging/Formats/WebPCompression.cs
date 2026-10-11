namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the WebP bitstream used for every frame.</summary>
public enum WebPCompression
{
    /// <summary>
    /// The lossless format (VP8L): every sample is preserved exactly, including the color of fully transparent pixels (unless
    /// <see cref="WebPEncoder.ClearTransparentColors"/> is set). This is the default.
    /// </summary>
    Lossless = 0,

    /// <summary>
    /// The lossy format (VP8, Y'CbCr 4:2:0 with BT.601 conversion), controlled by <see cref="WebPEncoder.Quality"/>. Alpha is
    /// stored losslessly in an <c>ALPH</c> chunk.
    /// </summary>
    Lossy = 1,
}
