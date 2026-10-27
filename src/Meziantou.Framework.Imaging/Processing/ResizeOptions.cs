namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options for <see cref="ImageProcessingExtensions.Resize(Image, ResizeOptions, CancellationToken)"/>.</summary>
/// <remarks>
/// <para>
/// Output dimensions are computed with checked integer/rational arithmetic and nearest rounding, and are never smaller than
/// one pixel. Sampling uses pixel centers and clamps at image edges.
/// </para>
/// <para>
/// Filtering is alpha-aware: colors are premultiplied by alpha while filtering and unpremultiplied afterward, so transparent
/// pixels do not bleed their hidden color; pixels whose resulting alpha is zero become transparent black. 16-bit pixel
/// formats are filtered with 16-bit precision.
/// </para>
/// </remarks>
public sealed class ResizeOptions
{
    /// <summary>Initializes a new instance of the <see cref="ResizeOptions"/> class.</summary>
    /// <param name="width">The target width. Must be positive.</param>
    /// <param name="height">The target height. Must be positive.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="width"/> or <paramref name="height"/> is not positive.</exception>
    public ResizeOptions(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(width);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(height);
        Size = new Size(width, height);
    }

    /// <summary>Initializes a new instance of the <see cref="ResizeOptions"/> class.</summary>
    /// <param name="size">The target size. Must not be empty.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="size"/> is empty.</exception>
    public ResizeOptions(Size size)
        : this(size.Width, size.Height)
    {
    }

    /// <summary>Gets the target size. Its interpretation depends on <see cref="Mode"/>.</summary>
    public Size Size { get; }

    /// <summary>Gets how the target size is interpreted. Defaults to <see cref="ResizeMode.Contain"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ResizeMode"/>.</exception>
    public ResizeMode Mode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The resize mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the part of the image kept by <see cref="ResizeMode.Cover"/>, or where <see cref="ResizeMode.Pad"/> and
    /// <see cref="ResizeMode.BoxPad"/> place the image on the canvas. Defaults to <see cref="ResizeAnchor.Center"/>. A
    /// centered image gets the extra pixel of an odd padding on its right or bottom side.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ResizeAnchor"/>.</exception>
    public ResizeAnchor Anchor
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The anchor is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets a value indicating whether the image may be enlarged. Defaults to <see langword="true"/>. When
    /// <see langword="false"/>, <see cref="ResizeMode.Contain"/>, <see cref="ResizeMode.Pad"/> and
    /// <see cref="ResizeMode.BoxPad"/> never exceed the original size of the image (the padding modes still produce the
    /// target size), while <see cref="ResizeMode.Stretch"/> and <see cref="ResizeMode.Cover"/> throw an
    /// <see cref="ArgumentException"/> if the request requires enlargement, instead of returning an unexpected size.
    /// </summary>
    /// <remarks>
    /// <see cref="ResizeMode.Contain"/> with <see langword="false"/> shrinks an image to fit the target size but never
    /// enlarges it (a "max" resize).
    /// </remarks>
    public bool AllowUpscaling { get; init; } = true;

    /// <summary>
    /// Gets a value indicating whether the image may be reduced. Defaults to <see langword="true"/>. When
    /// <see langword="false"/>, <see cref="ResizeMode.Contain"/> never goes below the original size (the image may then be
    /// larger than the target size), while <see cref="ResizeMode.Stretch"/>, <see cref="ResizeMode.Cover"/>,
    /// <see cref="ResizeMode.Pad"/> and <see cref="ResizeMode.BoxPad"/> throw an <see cref="ArgumentException"/> if the
    /// request requires a reduction, instead of returning an unexpected size.
    /// </summary>
    /// <remarks>
    /// <see cref="ResizeMode.Contain"/> with <see langword="false"/> enlarges an image to fit the target size but never
    /// shrinks it (a "min" resize).
    /// </remarks>
    public bool AllowDownscaling { get; init; } = true;

    /// <summary>
    /// Gets the color of the padding added by <see cref="ResizeMode.Pad"/> and <see cref="ResizeMode.BoxPad"/>, or
    /// <see langword="null"/> (the default) for transparent black. It is converted to the pixel format of the image, and is
    /// not used by the other modes.
    /// </summary>
    /// <remarks>
    /// A pixel format without alpha (<see cref="PixelFormat.Rgb24"/>, <see cref="PixelFormat.Gray8"/>,
    /// <see cref="PixelFormat.Gray16"/>) cannot store a transparent padding: padding such an image requires an opaque color.
    /// </remarks>
    public Rgba64? BackgroundColor { get; init; }

    /// <summary>Gets the resampling kernel. Defaults to <see cref="ResamplingFilter.Bicubic"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ResamplingFilter"/>.</exception>
    public ResamplingFilter Filter
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The filter is not valid.");

            field = value;
        }
    } = ResamplingFilter.Bicubic;

    /// <summary>Gets the numeric space used for filtering. Defaults to <see cref="ResizeWorkingSpace.Encoded"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="ResizeWorkingSpace"/>.</exception>
    public ResizeWorkingSpace WorkingSpace
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The working space is not valid.");

            field = value;
        }
    }
}
