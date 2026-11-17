namespace Meziantou.Framework.Imaging.Formats;

/// <summary>The encoding of the scanlines of a Radiance HDR file written by <see cref="RadianceEncoder"/>.</summary>
public enum RadianceCompression
{
    /// <summary>
    /// Flat scanlines: four bytes per pixel (red, green and blue mantissas, then the shared exponent), with no run-length
    /// encoding.
    /// </summary>
    None = 0,

    /// <summary>
    /// The adaptive run-length encoding of the four components of a scanline apart (the "new" encoding of Radiance). This
    /// is the default. A scanline shorter than 8 pixels or longer than 32,767 pixels cannot use it and is written flat.
    /// </summary>
    RunLength = 1,
}
