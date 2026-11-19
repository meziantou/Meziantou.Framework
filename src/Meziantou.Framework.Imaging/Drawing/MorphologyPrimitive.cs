using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
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
    /// comparisons per value, whatever the radius). The four components of a pixel are compared at once.
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
        var all = unsafe(MemoryMarshal.Cast<byte, Vector128<float>>(buffer.Span));
        var line = all[..padded];
        var prefix = all.Slice(padded, padded);
        var suffix = all.Slice(2 * padded, padded);
        var values = surface.Values;
        var stride = horizontal ? 4 : 4 * bounds.Width;
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                processor.CancellationToken.ThrowIfCancellationRequested();
            }

            var first = horizontal ? 4 * index * bounds.Width : 4 * index;
            line[..radius].Clear();
            line[(radius + length)..].Clear();
            for (var i = 0; i < length; i++)
            {
                line[i + radius] = Vector128.Create(values.Slice(first + (i * stride), 4));
            }

            // The value of each block of the window's size from its start, and to its end
            for (var start = 0; start < padded; start += window)
            {
                var end = Math.Min(start + window, padded);
                prefix[start] = line[start];
                for (var k = start + 1; k < end; k++)
                {
                    prefix[k] = Pick(prefix[k - 1], line[k], isDilate);
                }

                suffix[end - 1] = line[end - 1];
                for (var k = end - 2; k >= start; k--)
                {
                    suffix[k] = Pick(suffix[k + 1], line[k], isDilate);
                }
            }

            // The window of a pixel starts at its index in the padded line, and spans two blocks at most
            for (var i = 0; i < length; i++)
            {
                Pick(suffix[i], prefix[i + window - 1], isDilate).CopyTo(values.Slice(first + (i * stride), 4));
            }
        }

        // The same results as Math.Max and Math.Min on each component
        static Vector128<float> Pick(Vector128<float> a, Vector128<float> b, bool isDilate) => isDilate ? Vector128.Max(a, b) : Vector128.Min(a, b);
    }
}
