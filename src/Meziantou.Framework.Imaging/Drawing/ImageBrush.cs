using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A brush painting the pixels of an image.</summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> The image is in its own coordinates: its pixel <c>(i, j)</c> covers the square from <c>(i, j)</c> to
/// <c>(i + 1, j + 1)</c>, the pixel at the top-left corner of the frame being at the origin, and <see cref="Transform"/>
/// places it on the canvas. Without transformation, the pixels of the image are on the pixels of the canvas.
/// <see cref="SourceRegion"/> limits the image to some of its pixels, which stay where they are;
/// <see cref="WrapMode"/> gives the colors around them.
/// </para>
/// <para>
/// <b>Colors.</b> The color of a pixel of the canvas is the color of the image at the position its center comes from. With
/// <see cref="ImageSampling.Bilinear"/>, the four pixels of the image around that position are interpolated at 16 bits,
/// on alpha-premultiplied values, and the result is rounded to nearest; where the centers of the pixels of the image are
/// on the centers of the pixels of the canvas (no transformation, or a translation by whole pixels), the colors are
/// those of the image, exactly. The colors are then converted to the pixel format of the canvas like any brush color:
/// they are sample values, and no color profile is applied.
/// </para>
/// <para>
/// An image drawn smaller than it is skips pixels: each pixel of the canvas reads four pixels of the image at most.
/// Reduce the image first (<c>Resize</c>) for a result without aliasing.
/// </para>
/// <para>
/// <b>Lifetime.</b> The brush refers to the frame and does not own it. The pixels are read when a shape is drawn: the
/// frame must still belong to a live image, and must not be one of the frames being drawn on.
/// </para>
/// </remarks>
public sealed class ImageBrush : Brush
{
    /// <summary>Initializes a new instance of the <see cref="ImageBrush"/> class.</summary>
    /// <param name="source">The frame whose pixels are painted. It is not copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is <see langword="null"/>.</exception>
    public ImageBrush(ImageFrame source)
    {
        ArgumentNullException.ThrowIfNull(source);
        Source = source;
    }

    /// <summary>Gets the frame whose pixels are painted.</summary>
    public ImageFrame Source { get; }

    /// <summary>
    /// Gets the pixels of the frame the brush paints, or <see langword="null"/> (the default) for all of them. It is
    /// checked when a shape is drawn: it must then be inside the frame.
    /// </summary>
    public Rectangle? SourceRegion { get; init; }

    /// <summary>Gets the transformation that places the image on the canvas. Defaults to the identity.</summary>
    /// <remarks>With a transformation that flattens the plane to a line or a point, the brush paints nothing.</remarks>
    public AffineTransform Transform { get; init; }

    /// <summary>Gets what the brush paints outside the pixels of the image. Defaults to <see cref="ImageWrapMode.Transparent"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The wrap mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets how the image is read between the centers of its pixels. Defaults to <see cref="ImageSampling.Bilinear"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageSampling"/>.</exception>
    public ImageSampling Sampling
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The sampling is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the opacity of the image, from 0 (nothing is painted) to 1 (the default: the image as it is).</summary>
    /// <remarks>
    /// The opacity multiplies the coverage of the shape, rounded to the nearest 1/65536: an image at half its opacity is
    /// painted as on half of each pixel.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public double Opacity
    {
        get;
        init
        {
            OpacityCoverage = ImageEvaluator.GetOpacityCoverage(value, nameof(value));
            field = value;
        }
    } = 1;

    /// <summary>Gets the opacity as a coverage, from 0 to <see cref="CoverageRasterizer.FullCoverage"/>.</summary>
    internal uint OpacityCoverage { get; private init; } = CoverageRasterizer.FullCoverage;
}
