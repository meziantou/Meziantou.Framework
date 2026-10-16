using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>Computes the primitives of a <see cref="FilterGraph"/>, one after the other, and keeps their results as long as a primitive reads them.</summary>
/// <remarks>
/// <para>
/// Every result is a surface of the bounds of the filtered surface, transparent outside the subregion of its
/// primitive. A result is kept in the colors its primitive computed with; an input in other colors is converted, once,
/// to the colors of the primitive that reads it: each color is divided by the alpha, decoded or encoded by the transfer
/// function of sRGB (<see cref="SrgbTransfer"/>), and multiplied by the alpha again. The alpha of a pixel is never
/// converted, and the drawing (<see cref="FilterInputKind.SourceGraphic"/>) is in sRGB.
/// </para>
/// <para>
/// The subregion of a primitive is in pixels of the canvas: those whose center is in the box of the image of the
/// rectangle (<see cref="FilterProcessor.ToPixels"/>), within the region of the filter, which is by default the
/// rectangle of the edges of the processor (the canvas for a layer, the rectangle of a backdrop). It may go beyond the
/// filtered surface: the edge modes and the tiles use it, and only the part on the surface is computed.
/// </para>
/// </remarks>
internal sealed class FilterGraphContext : IDisposable
{
    private readonly FilterProcessor _processor;
    private readonly FilterSurface _source;
    private readonly IReadOnlyList<FilterPrimitive> _primitives;
    private readonly Entry[] _results;
    private readonly int[] _lastUses;
    private Entry _sourceGraphic;
    private Entry _sourceAlpha;
    private FilterPrimitive? _current;
    private int _currentIndex;

    public FilterGraphContext(FilterProcessor processor, FilterSurface source, IReadOnlyList<FilterPrimitive> primitives, RectangleD? region)
    {
        _processor = processor;
        _source = source;
        _primitives = primitives;
        Region = region is { } rectangle ? Rectangle.Intersect(processor.ToPixels(rectangle), processor.Edges) : processor.Edges;
        _results = new Entry[primitives.Count];
        _lastUses = new int[primitives.Count];
        _sourceGraphic = new Entry { Subregion = Region, Space = FilterColorSpace.Srgb, Kind = FilterInputKind.SourceGraphic };
        _sourceAlpha = new Entry { Subregion = Region, Space = FilterColorSpace.Srgb, Kind = FilterInputKind.SourceAlpha };

        // The last primitive that reads each result: the result of the last primitive is the output of the graph
        for (var index = 0; index < primitives.Count; index++)
        {
            var primitive = primitives[index];
            for (var input = 0; input < primitive.InputCount; input++)
            {
                var resultIndex = GetResultIndex(primitive.GetInput(input), index);
                if (resultIndex >= 0)
                {
                    _lastUses[resultIndex] = index;
                }
            }
        }

        _lastUses[^1] = primitives.Count;
    }

    /// <summary>Gets the processor of the filters, for the lengths of the primitives and the operations they share.</summary>
    public FilterProcessor Processor => _processor;

    /// <summary>Gets the pixels of the filter region, which may go beyond the filtered surface.</summary>
    public Rectangle Region { get; }

    /// <summary>Computes every primitive, and writes the result of the last one to the filtered surface, in sRGB.</summary>
    public void Run()
    {
        for (var index = 0; index < _primitives.Count; index++)
        {
            _processor.CancellationToken.ThrowIfCancellationRequested();
            var primitive = _primitives[index];
            _current = primitive;
            _currentIndex = index;
            var subregion = GetSubregion(primitive, index);
            var space = primitive.IsColorSpaceNeutral
                ? (primitive.InputCount > 0 ? GetEntry(primitive.GetInput(0), index).Space : FilterColorSpace.Srgb)
                : primitive.ColorSpace;
            var result = new FilterSurface(_processor.Scope, _source.Bounds);
            try
            {
                result.Values.Clear();
                var area = Rectangle.Intersect(subregion, _source.Bounds);
                if (!area.IsEmpty)
                {
                    primitive.Evaluate(this, result, area);
                }
            }
            catch
            {
                result.Dispose();
                throw;
            }

            _results[index] = new Entry { Subregion = subregion, Space = space, Kind = FilterInputKind.Result };
            SetSurface(ref _results[index], space, result);

            // The results that no primitive reads any more
            for (var i = 0; i < index; i++)
            {
                if (_lastUses[i] == index)
                {
                    _results[i].Dispose();
                }
            }
        }

        _current = null;
        var output = GetSurface(ref _results[^1], FilterColorSpace.Srgb);
        output.Values.CopyTo(_source.Values);
    }

    /// <summary>Gets an input of the primitive being computed, in the colors it computes with (in its own colors for a primitive whose colors make no difference).</summary>
    /// <param name="index">The index of the input.</param>
    public FilterSurface GetInput(int index)
    {
        var primitive = _current!;
        ref var entry = ref GetEntry(primitive.GetInput(index), _currentIndex);
        return GetSurface(ref entry, primitive.IsColorSpaceNeutral ? entry.Space : primitive.ColorSpace);
    }

    /// <summary>Gets an input of the primitive being computed in some colors, whatever the colors of the primitive.</summary>
    /// <param name="index">The index of the input.</param>
    /// <param name="space">The colors.</param>
    public FilterSurface GetInput(int index, FilterColorSpace space)
    {
        ref var entry = ref GetEntry(_current!.GetInput(index), _currentIndex);
        return GetSurface(ref entry, space);
    }

    /// <summary>Gets the subregion of an input of the primitive being computed: the pixels where it may not be transparent, which may go beyond the surface.</summary>
    /// <param name="index">The index of the input.</param>
    public Rectangle GetInputSubregion(int index) => GetEntry(_current!.GetInput(index), _currentIndex).Subregion;

    /// <summary>Gets a color in the colors of the primitive being computed, as fractions of one, with straight alpha.</summary>
    public (double Red, double Green, double Blue, double Alpha) GetColor(Rgba64 color)
    {
        var red = color.R / 65535.0;
        var green = color.G / 65535.0;
        var blue = color.B / 65535.0;
        if (!_current!.IsColorSpaceNeutral && _current.ColorSpace == FilterColorSpace.LinearRgb)
        {
            red = SrgbTransfer.Decode(red);
            green = SrgbTransfer.Decode(green);
            blue = SrgbTransfer.Decode(blue);
        }

        return (red, green, blue, color.A / 65535.0);
    }

    /// <summary>Makes the pixels of a surface outside a rectangle transparent.</summary>
    public static void ClearOutside(FilterSurface surface, Rectangle area)
    {
        var bounds = surface.Bounds;
        area = Rectangle.Intersect(area, bounds);
        for (var y = bounds.Top; y < bounds.Bottom; y++)
        {
            var row = surface.GetRow(y);
            if (area.IsEmpty || y < area.Top || y >= area.Bottom)
            {
                row.Clear();
                continue;
            }

            row[..(4 * (area.Left - bounds.Left))].Clear();
            row[(4 * (area.Right - bounds.Left))..].Clear();
        }
    }

    /// <summary>Copies the pixels of a rectangle of a surface to another surface of the same bounds.</summary>
    public static void Copy(FilterSurface source, FilterSurface target, Rectangle area)
    {
        for (var y = area.Top; y < area.Bottom; y++)
        {
            var offset = source.IndexOf(area.Left, y);
            source.Values.Slice(offset, 4 * area.Width).CopyTo(target.Values[offset..]);
        }
    }

    public void Dispose()
    {
        foreach (ref var entry in _results.AsSpan())
        {
            entry.Dispose();
        }

        _sourceGraphic.Dispose();
        _sourceAlpha.Dispose();
    }

    /// <summary>Gets the index of the result an input is, or -1 for the drawing or its alpha.</summary>
    private static int GetResultIndex(FilterInput input, int index) => input.Kind switch
    {
        FilterInputKind.Previous => index - 1,
        FilterInputKind.Result => input.ResultIndex,
        _ => -1,
    };

    private ref Entry GetEntry(FilterInput input, int index)
    {
        if (input.Kind == FilterInputKind.SourceAlpha)
            return ref _sourceAlpha;

        var resultIndex = GetResultIndex(input, index);
        if (resultIndex < 0)
            return ref _sourceGraphic;

        return ref _results[resultIndex];
    }

    /// <summary>
    /// Gets the subregion of a primitive: its own rectangle, or the union of the subregions of its inputs, or the
    /// region of the filter when it reads the drawing or its alpha, or reads nothing; within the region of the filter.
    /// </summary>
    private Rectangle GetSubregion(FilterPrimitive primitive, int index)
    {
        if (primitive.Subregion is { } rectangle)
            return Rectangle.Intersect(_processor.ToPixels(rectangle), Region);

        if (primitive.InputCount == 0)
            return Region;

        var union = Rectangle.Empty;
        for (var input = 0; input < primitive.InputCount; input++)
        {
            var inputIndex = GetResultIndex(primitive.GetInput(input), index);
            if (inputIndex < 0)
                return Region;

            var subregion = _results[inputIndex].Subregion;
            if (subregion.IsEmpty)
                continue;

            union = union.IsEmpty ? subregion : Rectangle.FromLTRB(Math.Min(union.Left, subregion.Left), Math.Min(union.Top, subregion.Top), Math.Max(union.Right, subregion.Right), Math.Max(union.Bottom, subregion.Bottom));
        }

        return union;
    }

    /// <summary>Gets the surface of an entry in some colors, converted from the other colors the first time.</summary>
    private FilterSurface GetSurface(ref Entry entry, FilterColorSpace space)
    {
        if (entry.Kind == FilterInputKind.SourceGraphic)
        {
            if (space == FilterColorSpace.Srgb)
                return _source;

            if (entry.Linear is null)
            {
                entry.Linear = new FilterSurface(_processor.Scope, _source.Bounds);
                Convert(_source, entry.Linear, decode: true);
            }

            return entry.Linear;
        }

        if (entry.Kind == FilterInputKind.SourceAlpha)
        {
            // Black has the same colors in both spaces
            if (entry.Srgb is null)
            {
                entry.Srgb = new FilterSurface(_processor.Scope, _source.Bounds);
                var source = _source.Values;
                var target = entry.Srgb.Values;
                target.Clear();
                for (var i = 3; i < source.Length; i += 4)
                {
                    target[i] = source[i];
                }
            }

            return entry.Srgb;
        }

        var surface = space == FilterColorSpace.Srgb ? entry.Srgb : entry.Linear;
        if (surface is not null)
            return surface;

        var other = space == FilterColorSpace.Srgb ? entry.Linear! : entry.Srgb!;
        surface = new FilterSurface(_processor.Scope, _source.Bounds);
        Convert(other, surface, decode: space == FilterColorSpace.LinearRgb);
        SetSurface(ref entry, space, surface);
        return surface;
    }

    private static void SetSurface(ref Entry entry, FilterColorSpace space, FilterSurface surface)
    {
        if (space == FilterColorSpace.Srgb)
        {
            entry.Srgb = surface;
        }
        else
        {
            entry.Linear = surface;
        }
    }

    /// <summary>Converts the colors of a surface between sRGB and linear light, the alpha unchanged.</summary>
    private void Convert(FilterSurface source, FilterSurface target, bool decode)
    {
        var from = source.Values;
        var to = target.Values;
        for (var i = 0; i < from.Length; i += 4)
        {
            if ((i & 0xFFFF) == 0)
            {
                _processor.CancellationToken.ThrowIfCancellationRequested();
            }

            double alpha = from[i + 3];
            if (!(alpha > 0))
            {
                to[i] = to[i + 1] = to[i + 2] = to[i + 3] = 0;
                continue;
            }

            for (var channel = 0; channel < 3; channel++)
            {
                var color = Math.Clamp(from[i + channel] / alpha, 0, 1);
                to[i + channel] = (float)((decode ? SrgbTransfer.Decode(color) : SrgbTransfer.Encode(color)) * alpha);
            }

            to[i + 3] = (float)alpha;
        }
    }

    /// <summary>
    /// The result of a primitive, or the drawing or its alpha: its subregion, the colors it was computed in, and its
    /// surface in each colors, once needed. The surface of the drawing in sRGB is the filtered surface, which is not owned.
    /// </summary>
    private struct Entry
    {
        public Rectangle Subregion;
        public FilterColorSpace Space;
        public FilterInputKind Kind;
        public FilterSurface? Srgb;
        public FilterSurface? Linear;

        public void Dispose()
        {
            Srgb?.Dispose();
            Linear?.Dispose();
            Srgb = null;
            Linear = null;
        }
    }
}
