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

    /// <summary>
    /// <see cref="Bilinear"/> for an image that is not reduced, bit for bit; for an image that is reduced, every pixel
    /// of the image a pixel of the canvas covers counts, with the weights of the interpolation stretched over them:
    /// no pixel is skipped, so a photograph drawn as a thumbnail has no aliasing.
    /// </summary>
    /// <remarks>
    /// The time to paint a pixel of the canvas grows with the number of pixels of the image it covers: about four times
    /// the pixels of the image for an image drawn once. Reduce an image that is drawn many times with <c>Resize</c>,
    /// which also has better filters.
    /// </remarks>
    Smooth = 2,
}
