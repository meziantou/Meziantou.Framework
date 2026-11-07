using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A primitive that makes the shapes of its input thinner or thicker: <c>feMorphology</c> of SVG.</summary>
/// <remarks>
/// Each component of a pixel of the result, the colors multiplied by their alpha, is the smallest (erode) or the
/// largest (dilate) value of that component on the rectangle of <c>2 rx + 1</c> by <c>2 ry + 1</c> pixels around the
/// pixel, where <c>rx</c> and <c>ry</c> are the radii on the canvas rounded to the nearest pixel. What is beyond the
/// subregion of the input is transparent. A radius that is zero on the canvas along an axis leaves that axis as it is.
/// </remarks>
public sealed class MorphologyPrimitive : FilterPrimitive
{
    /// <summary>Initializes a new instance of the <see cref="MorphologyPrimitive"/> class.</summary>
    /// <param name="operator">Whether the shapes get thinner or thicker.</param>
    /// <param name="radiusX">The radius along x, in units of the drawing: <c>radius</c>.</param>
    /// <param name="radiusY">The radius along y, in units of the drawing.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operator"/> is not defined, or a radius is negative or exceeds 16777216.</exception>
    /// <exception cref="ArgumentException">A radius is not a finite number.</exception>
    public MorphologyPrimitive(FilterMorphologyOperator @operator, double radiusX, double radiusY)
    {
        if (!Enum.IsDefined(@operator))
            throw new ArgumentOutOfRangeException(nameof(@operator), @operator, "The operator is not valid.");

        DrawingGuard.Length(radiusX, nameof(radiusX));
        DrawingGuard.Length(radiusY, nameof(radiusY));
        Operator = @operator;
        RadiusX = radiusX;
        RadiusY = radiusY;
    }

    /// <summary>Gets whether the shapes get thinner or thicker.</summary>
    public FilterMorphologyOperator Operator { get; }

    /// <summary>Gets the radius along x.</summary>
    public double RadiusX { get; }

    /// <summary>Gets the radius along y.</summary>
    public double RadiusY { get; }

    /// <summary>Gets the input: <c>in</c>.</summary>
    public FilterInput Input { get; init; }

    internal override int InputCount => 1;

    internal override bool IsColorSpaceNeutral => true;

    internal override FilterInput GetInput(int index) => index == 0 ? Input : throw new ArgumentOutOfRangeException(nameof(index));

    internal override (double X, double Y) GetReach() => (RadiusX + 1, RadiusY + 1);

    internal override void Evaluate(FilterGraphContext context, FilterSurface result, Rectangle subregion)
    {
        context.GetInput(0).Values.CopyTo(result.Values);
        var processor = context.Processor;
        var bounds = result.Bounds;
        var radiusX = (int)Math.Min(Math.Floor((RadiusX * processor.ScaleX) + 0.5), bounds.Width);
        var radiusY = (int)Math.Min(Math.Floor((RadiusY * processor.ScaleY) + 0.5), bounds.Height);
        var isDilate = Operator == FilterMorphologyOperator.Dilate;
        Apply(processor, result, radiusX, isDilate, horizontal: true);
        Apply(processor, result, radiusY, isDilate, horizontal: false);
        FilterGraphContext.ClearOutside(result, subregion);
    }

    /// <summary>
    /// Keeps the smallest or the largest value of each component over a window along one axis, in place: the window of
    /// <c>2 r + 1</c> pixels, transparent beyond the surface, by the blocks of van Herk and Gil and Werman (three
    /// comparisons per value, whatever the radius).
    /// </summary>
    private static void Apply(FilterProcessor processor, FilterSurface surface, int radius, bool isDilate, bool horizontal)
    {
        if (radius <= 0)
            return;

        var bounds = surface.Bounds;
        var length = horizontal ? bounds.Width : bounds.Height;
        var lines = horizontal ? bounds.Height : bounds.Width;
        var window = (2 * radius) + 1;
        var padded = length + (2 * radius);
        using var buffer = ResampleWeights.Rent<float>(processor.Scope, 12L * padded);
        var all = unsafe(MemoryMarshal.Cast<byte, float>(buffer.Span));
        var line = all[..(4 * padded)];
        var prefix = all.Slice(4 * padded, 4 * padded);
        var suffix = all.Slice(8 * padded, 4 * padded);
        var values = surface.Values;
        var stride = horizontal ? 4 : 4 * bounds.Width;
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                processor.CancellationToken.ThrowIfCancellationRequested();
            }

            var first = horizontal ? 4 * index * bounds.Width : 4 * index;
            line.Clear();
            for (var i = 0; i < length; i++)
            {
                values.Slice(first + (i * stride), 4).CopyTo(line.Slice(4 * (i + radius), 4));
            }

            // The value of each block of the window's size from its start, and to its end
            for (var start = 0; start < padded; start += window)
            {
                var end = Math.Min(start + window, padded);
                for (var k = start; k < end; k++)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var value = line[(4 * k) + channel];
                        prefix[(4 * k) + channel] = k == start ? value : Pick(prefix[(4 * (k - 1)) + channel], value, isDilate);
                    }
                }

                for (var k = end - 1; k >= start; k--)
                {
                    for (var channel = 0; channel < 4; channel++)
                    {
                        var value = line[(4 * k) + channel];
                        suffix[(4 * k) + channel] = k == end - 1 ? value : Pick(suffix[(4 * (k + 1)) + channel], value, isDilate);
                    }
                }
            }

            // The window of a pixel starts at its index in the padded line, and spans two blocks at most
            for (var i = 0; i < length; i++)
            {
                var output = values.Slice(first + (i * stride), 4);
                for (var channel = 0; channel < 4; channel++)
                {
                    output[channel] = Pick(suffix[(4 * i) + channel], prefix[(4 * (i + window - 1)) + channel], isDilate);
                }
            }
        }

        static float Pick(float a, float b, bool isDilate) => isDilate ? Math.Max(a, b) : Math.Min(a, b);
    }
}
