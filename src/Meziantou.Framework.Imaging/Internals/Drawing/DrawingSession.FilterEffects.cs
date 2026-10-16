using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Filters: a <see cref="Filter"/> applied to a layer before it is drawn, or to what is behind a layer, which it starts with.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> A filtered layer holds the pixels the clip can show and, around them, as far as the filter moves
/// what it filters (<see cref="Filter.GetReach"/>). When that margin goes beyond the canvas, the layer is an image of
/// its own (<see cref="PushBoundedLevel"/>) of the bounds of the clip and the margin (at most the largest side of the
/// canvas, four times the pixels of the bounds and 65,536, and the limits of the image: <see cref="TryGetFilterExtent"/>),
/// whose session fills only the canvas when it fills the whole canvas: what is drawn beyond the canvas is filtered
/// into it. When it ends, those pixels are read into a surface (<see cref="FilterProcessor"/>), filtered with the
/// edges of the canvas (the transparent edge mode reads what the layer holds beyond them,
/// <see cref="FilterProcessor.Extent"/>), written back to the layer, and the layer is drawn. Under a transformation that scales the axes, the lengths of the filter are multiplied by the scale of each
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
        var reach = Math.Ceiling(Math.Max(reachX * scaleX, reachY * scaleY));
        var margin = reach + 1;
        LayerLevel level;
        if (reach > 0 && TryGetFilterExtent(margin, out var extent))
        {
            // What is drawn beyond the canvas, within the reach of the filter, is moved or blurred into it: the layer
            // is an image of its own, which holds those pixels too
            level = PushBoundedLevel(extent.Left, extent.Top, extent.Right, extent.Bottom);
        }
        else
        {
            level = PushLayerCore((int)Math.Min(margin, Math.Max(Width, Height)));
        }

        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.Effect = LayerEffect.Filter;
        level.IsLuminanceMask = false;
        level.Filter = filter;
        level.FilterScale = (scaleX, scaleY);
        return level.Bounded?.Session ?? this;
    }

    /// <summary>
    /// Gets the pixels a filtered layer holds when they go beyond the canvas: the bounds of the clip with a margin
    /// around them, of at most the largest side of the canvas, and as long as the layer has at most four times the
    /// pixels of the bounds of the clip (and at least 65,536) and fits in the limits of the image.
    /// </summary>
    /// <returns><see langword="false"/> when the layer of the canvas holds them all: the margin stays on the canvas, or the clip shows nothing.</returns>
    private bool TryGetFilterExtent(double margin, out Rectangle extent)
    {
        extent = default;
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        if (!(right > left && bottom > top))
            return false;

        // The largest margin e with (w + 2e) (h + 2e) <= the most pixels: 4e^2 + 2(w + h)e + wh - pixels <= 0
        var limits = _image.Configuration.Limits;
        long width = right - left;
        long height = bottom - top;
        var pixels = Math.Min(limits.MaxFramePixels, (4 * width * height) + 65536);
        var extension = (long)Math.Min(Math.Min(margin, Math.Max(Width, Height)), Math.Min((limits.MaxWidth - width) / 2, (limits.MaxHeight - height) / 2));
        if (extension > 0 && (width + (2 * extension)) * (height + (2 * extension)) > pixels)
        {
            double sum = width + height;
            extension = (long)Math.Floor((Math.Sqrt((sum * sum) - (4 * ((width * (double)height) - pixels))) - sum) / 4);
            while (extension > 0 && (width + (2 * extension)) * (height + (2 * extension)) > pixels)
            {
                extension--;
            }
        }

        if (extension <= 0 || (left - extension >= 0 && top - extension >= 0 && right + extension <= Width && bottom + extension <= Height))
            return false;

        extent = Rectangle.FromLTRB(left - (int)extension, top - (int)extension, right + (int)extension, bottom + (int)extension);
        return true;
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
        processor.Configure(_canvasBounds, edgeOverride: null, scaleX, scaleY, m11, 0, 0, m22, offsetX, offsetY, _cancellationToken);
        using var surface = new FilterSurface(_image.Owner.Scope, bounds);
        FilterProcessor.Read(level.Frames![0], level.Storages![0], surface, unsafe(MemoryMarshal.Cast<byte, Rgba64>(_filterRow!.Span)));
        level.Filter!.Apply(processor, surface);
        FilterProcessor.Write(surface, level.Storages[0], bounds);
    }

    /// <summary>
    /// Applies the filter of a layer that holds pixels beyond the canvas to its image: the edge modes apply at the edges
    /// of the canvas, and what is drawn beyond them is read where a filter reads what is transparent there.
    /// </summary>
    private void ApplyFilterLayer(BoundedLayer bounded, LayerLevel level)
    {
        var frame = bounded.Image.Frames[0];
        var storage = frame.GetStorage();
        var area = new Rectangle(0, 0, bounded.Image.Width, bounded.Image.Height);
        var processor = GetFilterProcessor(area.Width);
        var (scaleX, scaleY) = level.FilterScale;
        var m11 = _hasTransform ? _transform.M11 : 1;
        var m22 = _hasTransform ? _transform.M22 : 1;
        var (offsetX, offsetY) = GetTranslation();
        var canvas = new Rectangle(_canvasBounds.X - bounded.Left, _canvasBounds.Y - bounded.Top, _canvasBounds.Width, _canvasBounds.Height);
        processor.Configure(canvas, edgeOverride: null, scaleX, scaleY, m11, 0, 0, m22, offsetX - bounded.Left, offsetY - bounded.Top, _cancellationToken);
        processor.Extent = area;
        using var surface = new FilterSurface(_image.Owner.Scope, area);
        FilterProcessor.Read(frame, storage, surface, unsafe(MemoryMarshal.Cast<byte, Rgba64>(_filterRow!.Span)));
        level.Filter!.Apply(processor, surface);
        FilterProcessor.Write(surface, storage, area);
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
