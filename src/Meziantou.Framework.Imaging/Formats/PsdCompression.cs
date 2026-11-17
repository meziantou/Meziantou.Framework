namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The compression of the image data of an Adobe Photoshop file written by <see cref="PsdEncoder"/>.</summary>
public enum PsdCompression
{
    /// <summary>The samples as they are, row by row (compression 0 of the file).</summary>
    None = 0,

    /// <summary>
    /// Each row compressed with PackBits, after a table of the compressed length of every row (compression 1 of the file,
    /// the compression of the composite image that Photoshop writes). This is the default.
    /// </summary>
    RunLength = 1,
}
