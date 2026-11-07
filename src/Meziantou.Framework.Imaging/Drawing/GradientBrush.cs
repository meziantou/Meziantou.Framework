namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>
/// A brush painting a transition between colors: a <see cref="LinearGradientBrush"/>, a <see cref="RadialGradientBrush"/>
/// or a <see cref="SweepGradientBrush"/>.
/// </summary>
/// <remarks>
/// <para>
/// The color of a pixel is the color of the gradient at the center of that pixel. Each position of the canvas has a
/// parameter (0 at the start of the gradient, 1 at its end), brought back into that range by <see cref="SpreadMode"/>. The
/// color is the color of the last stop whose offset is not greater than the parameter, interpolated with the next
/// stop; before the first stop it is the color of the first stop. Two stops with the same offset make a hard edge.
/// </para>
/// <para>
/// The stops are converted to the pixel format of the frame first, then interpolated on alpha-premultiplied values (a
/// transparent stop does not darken its neighbors), on the encoded sample values, and rounded to nearest with ties
/// upward. <see cref="ColorSpace"/> selects another space to mix the colors in: linear light, Oklab or Oklch. A gradient whose
/// stops all have the same color paints exactly what a <see cref="SolidBrush"/> of that color paints.
/// </para>
/// </remarks>
public abstract class GradientBrush : Brush
{
    private readonly GradientStop[] _stops;

    private protected GradientBrush(ReadOnlySpan<GradientStop> stops)
    {
        if (stops.IsEmpty)
            throw new ArgumentException("A gradient needs at least one stop.", nameof(stops));

        for (var i = 1; i < stops.Length; i++)
        {
            if (stops[i].Offset < stops[i - 1].Offset)
                throw new ArgumentException("The offsets of the stops must not decrease.", nameof(stops));
        }

        _stops = stops.ToArray();
    }

    /// <summary>Gets the stops, in increasing order of offset.</summary>
    public ReadOnlyMemory<GradientStop> Stops => _stops;

    /// <summary>Gets the colors outside the range of the stops. Defaults to <see cref="GradientSpreadMode.Pad"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="GradientSpreadMode"/>.</exception>
    public GradientSpreadMode SpreadMode
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The spread mode is not valid.");

            field = value;
        }
    }

    /// <summary>Gets the space in which the colors of two stops are mixed. Defaults to <see cref="GradientColorSpace.Encoded"/>.</summary>
    /// <remarks>
    /// In a space other than <see cref="GradientColorSpace.Encoded"/>, the stops are interpolated as 16-bit sRGB colors,
    /// on alpha-premultiplied values of that space, and the result is converted to the pixel format of the frame like
    /// any brush color; a color the interpolation takes out of sRGB is brought back to its nearest sample values.
    /// The colors come from the power and the cube root functions of the platform, as those of
    /// <see cref="ResizeWorkingSpace.LinearSrgb"/> do: a sample whose exact value is within about 1e-12 of a rounding
    /// boundary can differ by one from a platform to another.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="GradientColorSpace"/>.</exception>
    public GradientColorSpace ColorSpace
    {
        get;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "The color space is not valid.");

            field = value;
        }
    }

    /// <summary>
    /// Gets the transformation that places the gradient on the canvas: the points and the lengths of the gradient are
    /// given before it is applied. Defaults to the identity.
    /// </summary>
    /// <remarks>
    /// The color of a pixel is the color the gradient has, before the transformation, at the position the center of the
    /// pixel comes from. A scaling that is not the same on both axes turns the circles of a radial gradient into
    /// ellipses. With a transformation that flattens the plane to a line or a point, the whole canvas has the color of
    /// the last stop.
    /// </remarks>
    public AffineTransform Transform { get; init; }

    /// <summary>
    /// Gets a value indicating whether the colors of the gradient are dithered on a canvas of 8 bits per component, so
    /// that a slow transition does not show as bands. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A dithered pixel is rounded up or down to one of the two nearest samples, by an ordered pattern (the 8x8 Bayer
    /// matrix, placed on the pixels of the frame from its top-left corner) instead of to the nearest one: over a few
    /// pixels, the samples average to the color between two steps. The same threshold is used for the color and the
    /// alpha of a pixel. The pattern is fixed: the same drawing gives the same pixels.
    /// </para>
    /// <para>
    /// A color that a frame of 8 bits per component holds exactly, such as the color of a stop, is not changed. A frame
    /// of 16 bits per component or of floating-point components, whose steps do not show, is not dithered, and neither
    /// are the colors of a gradient that are not drawn on a frame (the paints of color glyphs).
    /// </para>
    /// </remarks>
    public bool Dither { get; init; }

    /// <summary>Gets the stops without copying them.</summary>
    internal ReadOnlySpan<GradientStop> StopSpan => _stops;
}
