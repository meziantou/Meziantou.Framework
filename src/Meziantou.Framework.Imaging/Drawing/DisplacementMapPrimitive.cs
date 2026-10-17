using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that moves the pixels of its input by the colors of a map: <c>feDisplacementMap</c> of SVG.</summary>
/// <remarks>
/// A pixel <c>(x, y)</c> of the result is the pixel of the input nearest to
/// <c>(x + scale (X - 1/2), y + scale (Y - 1/2))</c>, with <c>X</c> and <c>Y</c> the components of the pixel of the map
/// that <see cref="XChannel"/> and <see cref="YChannel"/> select, the colors without their alpha, in the colors of the
/// primitive; the scale goes through the scale of each axis on the canvas. The input is read in its own colors, which
/// the result keeps, as in SVG. What comes from beyond the subregion of the input is transparent.
/// </remarks>
public sealed class DisplacementMapPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="DisplacementMapPrimitive"/> class.</summary>
    /// <param name="scale">How far a component of 0 or 1 moves a pixel, twice, in units of the drawing: <c>scale</c>.</param>
    /// <param name="map">The map: <c>in2</c>.</param>
    /// <exception cref="ArgumentException"><paramref name="scale"/> is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of <paramref name="scale"/> exceeds 16777216.</exception>
    public DisplacementMapPrimitive(double scale, FilterInput map)
    {
        DrawingGuard.Coordinate(scale, nameof(scale));
        Scale = scale;
        Map = map;
        XChannel = FilterChannel.Alpha;
        YChannel = FilterChannel.Alpha;
    }

    /// <summary>Gets how far a component of 0 or 1 moves a pixel, twice.</summary>
    public double Scale { get; }

    /// <summary>Gets the map: <c>in2</c>.</summary>
    public FilterInput Map { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    /// <summary>Gets the component of the map that moves the pixels along x: <c>xChannelSelector</c>. Defaults to <see cref="FilterChannel.Alpha"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FilterChannel"/>.</exception>
    public FilterChannel XChannel { get; init => field = Validate(value); }

    /// <summary>Gets the component of the map that moves the pixels along y: <c>yChannelSelector</c>. Defaults to <see cref="FilterChannel.Alpha"/>.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not a defined <see cref="FilterChannel"/>.</exception>
    public FilterChannel YChannel { get; init => field = Validate(value); }

    internal override int InputCount => 2;

    // The result is in the colors of the input, which is not converted; the map is
    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => index switch
    {
        0 => Input,
        1 => Map,
        _ => throw new ArgumentOutOfRangeException(nameof(index)),
    };

    internal override (double X, double Y) GetReach() => (Math.Abs(Scale) / 2, Math.Abs(Scale) / 2);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var input = context.GetInput(0);
        var map = context.GetInput(1, ColorSpace);
        var edges = Rectangle.Intersect(context.GetInputSubregion(0), input.Bounds);
        var scaleX = Scale * context.Processor.ScaleX;
        var scaleY = Scale * context.Processor.ScaleY;
        var source = input.Values;
        var colors = map.Values;
        var values = result.Values;
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                var offset = map.IndexOf(x, y);
                var dx = scaleX * (GetComponent(colors, offset, XChannel) - 0.5);
                var dy = scaleY * (GetComponent(colors, offset, YChannel) - 0.5);
                var sourceX = Math.Floor(x + dx + 0.5);
                var sourceY = Math.Floor(y + dy + 0.5);
                if (sourceX < edges.Left || sourceX >= edges.Right || sourceY < edges.Top || sourceY >= edges.Bottom)
                    continue;

                source.Slice(input.IndexOf((int)sourceX, (int)sourceY), 4).CopyTo(values.Slice(result.IndexOf(x, y), 4));
            }
        }

        static double GetComponent(Span<float> colors, int offset, FilterChannel channel)
        {
            double alpha = colors[offset + 3];
            if (channel == FilterChannel.Alpha)
                return alpha;

            return alpha > 0 ? Math.Clamp(colors[offset + (int)channel] / alpha, 0, 1) : 0;
        }
    }

    private static FilterChannel Validate(FilterChannel value)
    {
        if (!Enum.IsDefined(value))
            throw new ArgumentOutOfRangeException(nameof(value), value, "The channel is not valid.");

        return value;
    }
}
