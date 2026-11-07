using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A brush painting the pixels of an image.</summary>
/// <remarks>
/// <para>
/// <b>Placement.</b> The image is in its own coordinates: its pixel <c>(i, j)</c> covers the square from <c>(i, j)</c> to
/// <c>(i + 1, j + 1)</c>, the pixel at the top-left corner of the frame being at the origin, and <see cref="Transform"/>
/// places it on the canvas. Without transformation, the pixels of the image are on the pixels of the canvas.
/// <see cref="SourceRegion"/> limits the image to some of its pixels, which stay where they are;
/// <see cref="WrapModeX"/> and <see cref="WrapModeY"/> give the colors around them.
/// </para>
/// <para>
/// <b>Colors.</b> The color of a pixel of the canvas is the color of the image at the position its center comes from. With
/// <see cref="ImageSampling.Bilinear"/>, the four pixels of the image around that position are interpolated at 16 bits,
/// on alpha-premultiplied values, and the result is rounded to nearest; where the centers of the pixels of the image are
/// on the centers of the pixels of the canvas (no transformation, or a translation by whole pixels), the colors are
/// those of the image, exactly. The colors are then converted to the pixel format of the canvas like any brush color:
/// they are sample values, and no color profile is applied. Floating-point images retain their range and precision on
/// floating-point canvases. On integer canvases their samples are read as 16-bit colors: a sample outside [0, 1] or that
/// is not a number has none, and the drawing that reads it throws an <see cref="UnsupportedImageFeatureException"/> instead of clipping it.
/// </para>
/// <para>
/// An image drawn smaller than it is skips pixels: each pixel of the canvas reads four pixels of the image at most.
/// <see cref="ImageSampling.Smooth"/> reads every pixel a pixel of the canvas covers instead, for a result without
/// aliasing, and <see cref="ImageSampling.Box"/> averages them; reduce an image that is drawn many times first
/// (<c>Resize</c>).
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

    /// <summary>
    /// Gets what the brush paints outside the pixels of the image, in both directions: setting it sets
    /// <see cref="WrapModeX"/> and <see cref="WrapModeY"/>, and getting it gets <see cref="WrapModeX"/>. Defaults to
    /// <see cref="ImageWrapMode.Transparent"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapMode
    {
        get => WrapModeX;
        init
        {
            WrapModeX = value;
            WrapModeY = value;
        }
    }

    /// <summary>
    /// Gets what the brush paints on the left and on the right of the pixels of the image: with
    /// <see cref="ImageWrapMode.Repeat"/> along x and <see cref="ImageWrapMode.Transparent"/> along y, a row of tiles
    /// (<c>background-repeat: repeat-x</c> of CSS). Defaults to <see cref="ImageWrapMode.Transparent"/>.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapModeX
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The wrap mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets what the brush paints above and below the pixels of the image. Defaults to <see cref="ImageWrapMode.Transparent"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ImageWrapMode"/>.</exception>
    public ImageWrapMode WrapModeY
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
    /// <remarks>The opacity of every brush (<see cref="Brush.Opacity"/>): it multiplies the alpha of the paint.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 0 and 1.</exception>
    public new double Opacity
    {
        get => base.Opacity;
        init => base.Opacity = value;
    }
}
