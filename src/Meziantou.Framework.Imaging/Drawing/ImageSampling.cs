namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>How the color of an image is read at a position that is not the center of one of its pixels.</summary>
public enum ImageSampling
{
    /// <summary>
    /// The four pixels around the position are interpolated, in proportion to their distances to it. This is the
    /// default. At the center of a pixel, the color is the color of that pixel, exactly.
    /// </summary>
    Bilinear = 0,

    /// <summary>The pixel that contains the position: an enlarged image keeps its pixels as squares.</summary>
    Nearest = 1,
}
