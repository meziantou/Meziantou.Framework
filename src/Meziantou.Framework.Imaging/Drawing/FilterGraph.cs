using Meziantou.Framework.Imaging.Internals;

namespace Meziantou.Framework.Imaging.Drawing;

/// <summary>A filter made of primitives that read the drawing, its alpha, and each other's results: an SVG <c>filter</c> element.</summary>
/// <remarks>
/// <para>
/// The primitives are computed in order. Each one reads its inputs (<see cref="FilterInput"/>: by default the result
/// of the primitive before it, or the drawing for the first one) and computes its result in its subregion
/// (<see cref="FilterPrimitive.Subregion"/>), transparent outside it. The result of the last primitive is the result of
/// the filter, and nothing is drawn outside <see cref="Region"/>.
/// </para>
/// <para>
/// Each primitive computes with the colors of its <see cref="FilterPrimitive.ColorSpace"/> (<c>color-interpolation-filters</c>):
/// by default, as in SVG, in linear light, the inputs decoded by the transfer function of sRGB and the result encoded
/// again, which uses <see cref="Math.Pow(double, double)"/> (a value may differ by one unit in the last place between
/// platforms). A primitive that only moves or copies pixels (<see cref="OffsetPrimitive"/>, <see cref="TilePrimitive"/>)
/// or makes a color (<see cref="FloodPrimitive"/>) gives the same pixels in both, and converts nothing.
/// </para>
/// <para>
/// The rectangles of the graph are in the coordinates of the drawing: a subregion is the set of pixels whose center is
/// in the box of the image of its rectangle on the canvas. The inputs <c>BackgroundImage</c>, <c>BackgroundAlpha</c>,
/// <c>FillPaint</c> and <c>StrokePaint</c> of SVG are not supported.
/// </para>
/// </remarks>
public sealed class FilterGraph : Filter
{
    private readonly FilterPrimitive[] _primitives;

    /// <summary>Initializes a new instance of the <see cref="FilterGraph"/> class.</summary>
    /// <param name="primitives">The primitives, in the order they are computed. They are copied.</param>
    /// <exception cref="ArgumentNullException">A primitive is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentException">There is no primitive, or a primitive reads the result of a primitive that is not before it.</exception>
    public FilterGraph(params ReadOnlySpan<FilterPrimitive> primitives)
    {
        if (primitives.IsEmpty)
            throw new ArgumentException("A filter graph needs at least one primitive.", nameof(primitives));

        for (var index = 0; index < primitives.Length; index++)
        {
            var primitive = primitives[index];
            ArgumentNullException.ThrowIfNull(primitive, nameof(primitives));
            for (var input = 0; input < primitive.InputCount; input++)
            {
                var value = primitive.GetInput(input);
                if (value.Kind == FilterInputKind.Result && value.ResultIndex >= index)
                    throw new ArgumentException($"The primitive at index {index} reads the result of the primitive at index {value.ResultIndex}, which is not before it.", nameof(primitives));
            }
        }

        _primitives = primitives.ToArray();
    }

    /// <summary>Gets the primitives, in the order they are computed.</summary>
    public IReadOnlyList<FilterPrimitive> Primitives => _primitives;

    /// <summary>
    /// Gets the rectangle of the drawing the filter draws in (the filter region of SVG), or <see langword="null"/> (the
    /// default) for the whole canvas. The rectangle can go beyond the canvas: what is drawn there, within the reach of
    /// the filter, is read by the primitives.
    /// </summary>
    /// <exception cref="ArgumentException">A coordinate is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216.</exception>
    public RectangleD? Region
    {
        get;
        init
        {
            if (value is { } rectangle)
            {
                DrawingGuard.Rectangle(rectangle, nameof(value));
            }

            field = value;
        }
    }

    internal override void Apply(FilterProcessor processor, FilterSurface surface)
    {
        using var context = new FilterGraphContext(processor, surface.Bounds, _primitives, Region);
        context.Run(surface);
    }

    internal override void ApplyToLayer(FilterProcessor processor, ImageFrame frame, PixelStorage storage, Rectangle bounds, Span<Rgba64> row)
    {
        using var context = new FilterGraphContext(processor, bounds, _primitives, Region);
        if (context.ReadsSource)
        {
            using var surface = new FilterSurface(processor.Scope, bounds);
            FilterProcessor.Read(frame, storage, surface, row);
            context.Run(surface);
            FilterProcessor.Write(surface, storage, bounds);
            return;
        }

        // The drawing is not read, and the result is transparent beyond the pixels the subregions cover: only those are
        // computed, which keeps a small subregion of a large layer in the buffer pool
        using (var surface = new FilterSurface(processor.Scope, context.Bounds))
        {
            context.Run(surface);
            FilterProcessor.Write(surface, storage, context.Bounds);
        }

        FilterProcessor.Clear(storage, bounds, context.Bounds);
    }

    internal override (double X, double Y) GetReach()
    {
        // Each primitive may read what the ones before it moved
        double x = 0, y = 0;
        foreach (var primitive in _primitives)
        {
            var (px, py) = primitive.GetReach();
            x = Math.Min(x + px, DrawingGuard.MaxCoordinate);
            y = Math.Min(y + py, DrawingGuard.MaxCoordinate);
        }

        return (x, y);
    }
}
