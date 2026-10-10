namespace Meziantou.Framework.Imaging.Formats;

/// <summary>Selects the chroma subsampling of color JPEG output.</summary>
public enum JpegChromaSubsampling
{
    /// <summary>Chooses automatically: 4:2:0 for color images; grayscale images are written with a single component. This is the default.</summary>
    Auto = 0,

    /// <summary>4:4:4, no chroma subsampling.</summary>
    Ratio444 = 1,

    /// <summary>4:2:2, chroma halved horizontally.</summary>
    Ratio422 = 2,

    /// <summary>4:2:0, chroma halved horizontally and vertically.</summary>
    Ratio420 = 3,
}
