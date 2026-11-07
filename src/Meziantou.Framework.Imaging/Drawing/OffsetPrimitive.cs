using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that moves its input: <c>feOffset</c> of SVG.</summary>
/// <remarks>
/// A pixel of the result is the pixel of the input at its position minus <see cref="Offset"/> on the canvas (through
/// the linear part of the transformation); between pixels, the four around are interpolated, in the colors of the
/// input. What comes from beyond the subregion of the input is transparent.
/// </remarks>
public sealed class OffsetPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="OffsetPrimitive"/> class.</summary>
    /// <param name="offset">The distance the input is moved by, in units of the drawing: <c>dx</c> and <c>dy</c>.</param>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public OffsetPrimitive(PointD offset)
    {
        DrawingGuard.Point(offset, nameof(offset));
        Offset = offset;
    }

    /// <summary>Gets the distance the input is moved by.</summary>
    public PointD Offset { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (Math.Abs(Offset.X), Math.Abs(Offset.Y));

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        var input = context.GetInput(0);
        var (dx, dy) = context.Processor.MapVector(Offset);
        var values = result.Values;
        Span<double> color = stackalloc double[4];
        for (var y = subregion.Top; y < subregion.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                context.Processor.CancellationToken.ThrowIfCancellationRequested();
            }

            for (var x = subregion.Left; x < subregion.Right; x++)
            {
                FilterProcessor.Interpolate(input, x - dx, y - dy, color);
                var offset = result.IndexOf(x, y);
                values[offset] = (float)color[0];
                values[offset + 1] = (float)color[1];
                values[offset + 2] = (float)color[2];
                values[offset + 3] = (float)color[3];
            }
        }
    }
}
