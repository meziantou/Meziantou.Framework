namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A brush painting a transition between colors: a <see cref="LinearGradientBrush"/> or a <see cref="RadialGradientBrush"/>.</summary>
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
/// upward. A gradient whose stops all have the same color paints exactly what a <see cref="SolidBrush"/> of that color
/// paints.
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

    /// <summary>Gets the stops without copying them.</summary>
    internal ReadOnlySpan<GradientStop> StopSpan => _stops;
}
