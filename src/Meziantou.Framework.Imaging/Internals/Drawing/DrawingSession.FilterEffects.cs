using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Filters: a <see cref="Filter"/> applied to a layer before it is drawn, or to what is behind a layer, which it starts with.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> A filtered layer holds the pixels the clip can show and, around them, as far as the filter moves
/// what it filters (<see cref="Filter.GetReach"/>). When it ends, those pixels are read into a surface
/// (<see cref="FilterProcessor"/>), filtered with the edges of the canvas, written back to the layer, and the layer is
/// drawn. Under a transformation that scales the axes, the lengths of the filter are multiplied by the scale of each
/// axis; under one that turns, skews or is projective, the layer is drawn on an image of the plane of the drawing
/// (<see cref="BeginProjectedScope"/>), where the filter applies.
/// </para>
/// <para>
/// A backdrop layer starts with what is drawn behind it: the pixels of the rectangle (the pixels whose center is in
/// the rectangle of the drawing, or in the box of its image on the canvas, and in what is drawn on), filtered with
/// mirrored edges, written to the layer in the bounds of the clip; then the drawings of the callback are made on it,
/// and it is drawn as any layer is. When several frames are drawn on, the layer has a frame for each, which starts with
/// the backdrop of its frame. The lengths of the filter go through the linear part of the transformation of the
/// canvas (divided by its <c>M33</c> when it is projective): a deviation is multiplied by the length of the image of
/// the unit vector of its axis.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private FilterProcessor? _filterProcessor;
    private PooledBuffer? _filterRow;

    /// <summary>Starts a layer whose pixels go through a filter when it ends, before it is drawn.</summary>
    /// <returns>The session the drawings of the layer are made by.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="filter"/> is <see langword="null"/>.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    public DrawingSession PushFilter(Filter filter)
    {
        ArgumentNullException.ThrowIfNull(filter);
        _cancellationToken.ThrowIfCancellationRequested();
        var (reachX, reachY) = filter.GetReach();
        if (_isProjective || (_hasTransform && (_transform.M12 != 0 || _transform.M21 != 0)))
        {
            // Filtered in the plane of the drawing, then drawn there
            var session = PushProjectedLayer(Math.Max(reachX, reachY) + 1);
            try
            {
                return session.PushFilter(filter);
            }
            catch
            {
                DiscardProjectedLayer();
                throw;
            }
        }

        var scaleX = _hasTransform ? Math.Abs(_transform.M11) : 1;
        var scaleY = _hasTransform ? Math.Abs(_transform.M22) : 1;
        var margin = Math.Ceiling(Math.Max(reachX * scaleX, reachY * scaleY)) + 1;
        var level = PushLayerCore((int)Math.Min(margin, Math.Max(Width, Height)));
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.Effect = LayerEffect.Filter;
        level.IsLuminanceMask = false;
        level.Filter = filter;
        level.FilterScale = (scaleX, scaleY);
        return this;
    }

    /// <summary>Starts a layer that starts with the filtered backdrop of a rectangle, and is drawn as a layer when it ends.</summary>
    /// <exception cref="ArgumentException">A coordinate of the rectangle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The magnitude of a coordinate exceeds 16777216, the opacity is not between 0 and 1, or the operator or the blend mode is not defined.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer or the working storage exceeds the allocation limit.</exception>
    public void PushBackdropLayer(Filter? filter, RectangleD bounds, double opacity, CompositeMode mode, BlendMode blend)
    {
        DrawingGuard.Rectangle(bounds, nameof(bounds));
        var coverage = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));
        if ((uint)mode > (uint)CompositeMode.Plus)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The composite mode is not valid.");

        if ((uint)blend > (uint)BlendMode.Luminosity)
            throw new ArgumentOutOfRangeException(nameof(blend), blend, "The blend mode is not valid.");

        _cancellationToken.ThrowIfCancellationRequested();

        // The pixels of the backdrop: those whose center is in the rectangle on the canvas, in what is drawn on
        var box = _isProjective ? GetProjectedBox(bounds) : ToCanvasBounds(bounds);
        var cropLeft = (int)Math.Clamp(Math.Floor(box.Left + 0.5), -1, Width + 1);
        var cropTop = (int)Math.Clamp(Math.Floor(box.Top + 0.5), -1, Height + 1);
        var cropRight = (int)Math.Clamp(Math.Floor(box.Right + 0.5), cropLeft, Width + 1);
        var cropBottom = (int)Math.Clamp(Math.Floor(box.Bottom + 0.5), cropTop, Height + 1);
        var crop = Rectangle.FromLTRB(cropLeft, cropTop, cropRight, cropBottom);
        var drawnOn = _layerDepth > 0 ? GetBounds(_layers[_layerDepth - 1]) : new Rectangle(0, 0, Width, Height);
        crop = Rectangle.Intersect(crop, drawnOn);

        // The backdrop of each frame is read and filtered before the layer changes what is drawn on
        var surfaces = new FilterSurface?[_frames.Length];
        try
        {
            if (!crop.IsEmpty)
            {
                var processor = GetFilterProcessor(crop.Width);
                var (m11, m12, m21, m22) = GetLinearPart();
                var (offsetX, offsetY) = GetTranslation();
                processor.Configure(crop, FilterEdgeMode.Mirror, Math.Sqrt((m11 * m11) + (m12 * m12)), Math.Sqrt((m21 * m21) + (m22 * m22)), m11, m12, m21, m22, offsetX, offsetY, _cancellationToken);
                var row = unsafe(MemoryMarshal.Cast<byte, Rgba64>(_filterRow!.Span));
                for (var i = 0; i < _frames.Length; i++)
                {
                    var surface = new FilterSurface(_image.Owner.Scope, crop);
                    surfaces[i] = surface;
                    FilterProcessor.Read(_frames[i], _storages[i], surface, row);
                    filter?.Apply(processor, surface);
                }
            }

            var level = PushLayerCore(margin: 0, _frames.Length);
            level.Opacity = coverage;
            level.Mode = mode;
            level.Blend = blend;
            level.Effect = LayerEffect.None;
            level.IsLuminanceMask = false;
            var area = GetBounds(level);
            for (var i = 0; i < surfaces.Length; i++)
            {
                if (surfaces[i] is { } surface)
                {
                    FilterProcessor.Write(surface, level.Storages![i], area);
                }
            }
        }
        finally
        {
            foreach (var surface in surfaces)
            {
                surface?.Dispose();
            }
        }
    }

    /// <summary>Applies the filter of a layer to the pixels it holds. The pixels of the layer are still held by the session.</summary>
    private void ApplyFilterLayer(LayerLevel level)
    {
        var bounds = GetBounds(level);
        if (bounds.IsEmpty)
            return;

        var processor = GetFilterProcessor(bounds.Width);
        var (scaleX, scaleY) = level.FilterScale;
        var m11 = _hasTransform ? _transform.M11 : 1;
        var m22 = _hasTransform ? _transform.M22 : 1;
        var (offsetX, offsetY) = GetTranslation();
        processor.Configure(new Rectangle(0, 0, Width, Height), edgeOverride: null, scaleX, scaleY, m11, 0, 0, m22, offsetX, offsetY, _cancellationToken);
        using var surface = new FilterSurface(_image.Owner.Scope, bounds);
        FilterProcessor.Read(level.Frames![0], level.Storages![0], surface, unsafe(MemoryMarshal.Cast<byte, Rgba64>(_filterRow!.Span)));
        level.Filter!.Apply(processor, surface);
        FilterProcessor.Write(surface, level.Storages[0], bounds);
    }

    /// <summary>Gets the processor of the filters and the row its pixels are read through.</summary>
    private FilterProcessor GetFilterProcessor(int width)
    {
        if (_filterRow is null || _filterRow.Length < width * 8)
        {
            _filterRow?.Dispose();
            _filterRow = null;
            _filterRow = ResampleWeights.Rent<Rgba64>(_image.Owner.Scope, Math.Max(width, Width));
        }

        return _filterProcessor ??= new FilterProcessor(_image.Owner.Scope, _image.Configuration);
    }

    /// <summary>Gets the linear part of the transformation of the canvas, divided by <c>M33</c> when it is projective.</summary>
    private (double M11, double M12, double M21, double M22) GetLinearPart()
    {
        if (_isProjective)
        {
            var w = Math.Abs(_projection.M33) > 0 ? _projection.M33 : 1;
            return (_projection.M11 / w, _projection.M12 / w, _projection.M21 / w, _projection.M22 / w);
        }

        return _hasTransform ? (_transform.M11, _transform.M12, _transform.M21, _transform.M22) : (1, 0, 0, 1);
    }

    /// <summary>Gets the translation of the transformation of the canvas, divided by <c>M33</c> when it is projective: the image of the origin of the drawing when it is affine.</summary>
    private (double X, double Y) GetTranslation()
    {
        if (_isProjective)
        {
            var w = Math.Abs(_projection.M33) > 0 ? _projection.M33 : 1;
            return (_projection.M31 / w, _projection.M32 / w);
        }

        return _hasTransform ? (_transform.OffsetX, _transform.OffsetY) : (0, 0);
    }

    /// <summary>Gets the box of the image of a rectangle under a projective transformation: of its corners when they all have one, the canvas otherwise.</summary>
    private RectangleD GetProjectedBox(RectangleD bounds)
    {
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var x = (corner & 1) == 0 ? bounds.Left : bounds.Right;
            var y = (corner & 2) == 0 ? bounds.Top : bounds.Bottom;
            if (!_projection.TryTransform(new PointD(x, y), out var point))
                return new RectangleD(0, 0, Width, Height);

            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        minX = Math.Clamp(minX, -1, Width + 1);
        minY = Math.Clamp(minY, -1, Height + 1);
        return RectangleD.FromLTRB(minX, minY, Math.Clamp(maxX, minX, Width + 1), Math.Clamp(maxY, minY, Height + 1));
    }

    /// <summary>Gets the pixels a layer holds; empty when the clip it started under shows nothing.</summary>
    private static Rectangle GetBounds(LayerLevel level)
        => level.Right > level.Left && level.Bottom > level.Top ? Rectangle.FromLTRB(level.Left, level.Top, level.Right, level.Bottom) : Rectangle.Empty;

    private void DisposeFilterEffects()
    {
        _filterProcessor?.Dispose();
        _filterProcessor = null;
        _filterRow?.Dispose();
        _filterRow = null;
    }
}
