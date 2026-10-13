namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects how the frames of a JPEG XL image are coded.</summary>
public enum JpegXlCompression
{
    /// <summary>
    /// The lossless coding (Modular): every sample is preserved exactly, 16-bit samples and the color of fully transparent
    /// pixels included. This is the default.
    /// </summary>
    Lossless = 0,

    /// <summary>
    /// The lossy coding (VarDCT in the XYB color space), controlled by <see cref="JpegXlEncoder.Quality"/>. Alpha is stored
    /// losslessly.
    /// </summary>
    Lossy = 1,
}
