namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The compression of the raw image of a DNG file written by <see cref="DngEncoder"/>.</summary>
public enum DngCompression
{
    /// <summary>The samples as they are (compression 1).</summary>
    None = 0,

    /// <summary>
    /// Lossless JPEG (ITU-T T.81 process 14, compression 7), the compression of the raw images of most DNG files: each tile is
    /// a lossless JPEG datastream of one component per sample. Integer samples only.
    /// </summary>
    LosslessJpeg = 1,

    /// <summary>Deflate (compression 8), which DNG 1.4 added for floating-point samples. Floating-point samples only.</summary>
    Deflate = 2,
}
