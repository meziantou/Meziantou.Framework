namespace Meziantou.Framework.Imaging;

/// <summary>Immutable options of <see cref="ImageProcessingExtensions.Inpaint(Image, PixelMask, InpaintingOptions?, CancellationToken)"/>.</summary>
public sealed class InpaintingOptions
{
    /// <summary>The largest radius.</summary>
    public const int MaxRadius = 64;

    /// <summary>Gets the default options: a radius of 5 pixels.</summary>
    public static InpaintingOptions Default { get; } = new();

    /// <summary>
    /// Gets the radius, in pixels, of the neighborhood a pixel of the mask is filled from, from 1 to
    /// <see cref="MaxRadius"/>. Defaults to 5. A larger radius gives smoother fills and costs time proportional to its
    /// square; a radius of about the half width of the strokes of the mask is usually enough.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not from 1 to <see cref="MaxRadius"/>.</exception>
    public int Radius
    {
        get;
        init
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(value, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(value, MaxRadius);
            field = value;
        }
    } = 5;
}
