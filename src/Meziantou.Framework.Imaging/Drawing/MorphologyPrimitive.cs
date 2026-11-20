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
    /// comparisons per value, whatever the radius).
    /// </summary>
    private static void Apply(FilterProcessor processor, FilterSurface surface, int radius, bool isDilate, bool horizontal)
    {
        if (radius <= 0)
            return;

        var bounds = surface.Bounds;
        var padded = (horizontal ? bounds.Width : bounds.Height) + (2 * radius);
        using var buffer = ResampleWeights.Rent<float>(processor.Scope, 12L * padded);
        ApplyAxis(surface.Values, bounds.Width, bounds.Height, radius, isDilate, horizontal, unsafe(MemoryMarshal.Cast<byte, Vector128<float>>(buffer.Span)), processor.CancellationToken);
    }

    /// <summary>
    /// The morphology of <see cref="Apply"/> on the colors multiplied by their alpha of an image of <paramref name="width"/> by
    /// <paramref name="height"/> pixels: the four components of a pixel are compared at once. <paramref name="buffer"/> holds
    /// 3 times the length of a line plus <c>2 r</c> vectors. <see cref="ApplyAxisScalar"/> is its reference.
    /// </summary>
    internal static void ApplyAxis(Span<float> values, int width, int height, int radius, bool isDilate, bool horizontal, Span<Vector128<float>> buffer, CancellationToken cancellationToken)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var window = (2 * radius) + 1;
        var padded = length + (2 * radius);
        var line = buffer[..padded];
        var prefix = buffer.Slice(padded, padded);
        var suffix = buffer.Slice(2 * padded, padded);
        var stride = horizontal ? 4 : 4 * width;
        for (var index = 0; index < lines; index++)
        {
            if ((index & 15) == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            var first = horizontal ? 4 * index * width : 4 * index;
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

    /// <summary>The scalar reference of <see cref="ApplyAxis"/>: <paramref name="buffer"/> holds 12 times the length of a line plus <c>2 r</c> values.</summary>
    internal static void ApplyAxisScalar(Span<float> values, int width, int height, int radius, bool isDilate, bool horizontal, Span<float> buffer)
    {
        var length = horizontal ? width : height;
        var lines = horizontal ? height : width;
        var window = (2 * radius) + 1;
        var padded = length + (2 * radius);
        var line = buffer[..(4 * padded)];
        var prefix = buffer.Slice(4 * padded, 4 * padded);
        var suffix = buffer.Slice(8 * padded, 4 * padded);
        var stride = horizontal ? 4 : 4 * width;
        for (var index = 0; index < lines; index++)
        {
            var first = horizontal ? 4 * index * width : 4 * index;
            line.Clear();
            for (var i = 0; i < length; i++)
            {
                values.Slice(first + (i * stride), 4).CopyTo(line.Slice(4 * (i + radius), 4));
            }

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
