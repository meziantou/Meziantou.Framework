namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the HEVC coding of every image of an HEIC file.</summary>
public enum HeicCompression
{
    /// <summary>
    /// The lossless coding: every sample is preserved exactly, including the color of fully transparent pixels (unless
    /// <see cref="HeicEncoder.ClearTransparentColors"/> is set). The R'G'B' samples are coded without color conversion
    /// (identity matrix, 4:4:4, full range, without transform nor quantization), gray samples as a monochrome image. This
    /// is the default.
    /// </summary>
    Lossless = 0,

    /// <summary>
    /// The lossy coding of Y'CbCr samples, controlled by <see cref="HeicEncoder.Quality"/> and
    /// <see cref="HeicEncoder.ChromaSubsampling"/>. Alpha is stored losslessly.
    /// </summary>
    Lossy = 1,
}
