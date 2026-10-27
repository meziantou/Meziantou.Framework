namespace Meziantou.Framework.Imaging;

/// <summary>
/// Immutable options of the transformations of an image:
/// <see cref="ImageProcessingExtensions.Rotate(Image, double, TransformOptions?, CancellationToken)"/>,
/// <see cref="ImageProcessingExtensions.Skew(Image, double, double, TransformOptions?, CancellationToken)"/> and
/// <see cref="ImageProcessingExtensions.Transform(Image, AffineTransform, TransformOptions?, CancellationToken)"/>.
/// </summary>
public sealed class TransformOptions
{
    /// <summary>Gets the default options: bicubic filter, transparent background, expanded canvas, encoded samples.</summary>
    public static TransformOptions Default { get; } = new();

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

    /// <summary>
    /// Gets the color of the canvas where the transformed image does not reach. Defaults to transparent black. A pixel
    /// format without alpha needs an opaque color, unless no pixel of the result reads beyond the image.
    /// </summary>
    public Rgba64 BackgroundColor { get; init; }

    /// <summary>Gets how the size of the canvas is chosen. Defaults to <see cref="TransformCanvasMode.Expand"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="TransformCanvasMode"/>.</exception>
    public TransformCanvasMode CanvasMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The canvas mode is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the size of the canvas of <see cref="TransformCanvasMode.Explicit"/>, which requires a size that is not empty.
    /// It is not used by the other modes. Defaults to <see cref="Size.Empty"/>.
    /// </summary>
    public Size CanvasSize { get; init; }

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
