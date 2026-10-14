namespace Meziantou.Framework.Imaging.Formats;

/// <summary>How a JPEG 2000 image is compressed.</summary>
public enum Jpeg2000Compression
{
    /// <summary>
    /// The reversible wavelet transformation (the 5-3 filter) without quantization: the decoded samples are the samples of
    /// the image, 16-bit samples and the color of fully transparent pixels included.
    /// </summary>
    Lossless = 0,

    /// <summary>
    /// The irreversible wavelet transformation (the 9-7 filter) with a quantization that
    /// <see cref="Jpeg2000Encoder.Quality"/> sets. The opacity channel is still stored without loss.
    /// </summary>
    Lossy = 1,
}
