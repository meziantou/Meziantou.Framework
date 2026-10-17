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

    /// <summary>
    /// The sixteen pixels around the position, with the weights of the cubic filter of Mitchell and Netravali
    /// (<c>B = C = 1/3</c>), as browsers draw a scaled image with high quality: smoother than <see cref="Bilinear"/>
    /// when the image is enlarged, without its blur.
    /// </summary>
    /// <remarks>
    /// The filter has negative weights: the alpha is clamped to the range from 0 to 1, and each color, multiplied by the
    /// alpha, to the range from 0 to the alpha. At the center of a pixel, the color mixes it with its neighbors (the
    /// filter does not interpolate). An image that is reduced is read at the position, as with <see cref="Bilinear"/>.
    /// </remarks>
    Bicubic = 3,

    /// <summary>
    /// <see cref="Bilinear"/> for an image that is not reduced, bit for bit; for an image that is reduced, the average of
    /// what a pixel of the canvas covers of the image: each pixel of the image weighs the length of its part inside the
    /// columns and the rows the pixel of the canvas covers, as the box-filtered mipmaps of browsers approximate.
    /// </summary>
    /// <remarks>
    /// Stripes of whole pixels that a reduction by a whole factor keeps aligned stay sharp: <see cref="Smooth"/>, whose
    /// filter is twice as wide, blends them at their edges. The cost is that of <see cref="Smooth"/>, about half as many
    /// pixels of the image for each pixel of the canvas.
    /// </remarks>
    Box = 4,
}
