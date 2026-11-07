namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the AV1 coding of every image of an AVIF file.</summary>
public enum AvifCompression
{
    /// <summary>
    /// The lossless coding: every sample is preserved exactly, including the color of fully transparent pixels (unless
    /// <see cref="AvifEncoder.ClearTransparentColors"/> is set). The R'G'B' samples are coded without color conversion
    /// (identity matrix, 4:4:4, full range), gray samples as a monochrome image. This is the default.
    /// </summary>
    Lossless = 0,

    /// <summary>
    /// The lossy coding of Y'CbCr samples, controlled by <see cref="AvifEncoder.Quality"/> and
    /// <see cref="AvifEncoder.ChromaSubsampling"/>. Alpha is stored losslessly.
    /// </summary>
    Lossy = 1,
}
