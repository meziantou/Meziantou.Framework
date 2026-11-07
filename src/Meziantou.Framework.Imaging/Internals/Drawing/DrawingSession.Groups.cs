using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Groups: layers whose result depends on their backdrop (non-isolated groups) or whose elements knock out one another (knockout groups).</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> A group is a bounded layer of 16-bit samples drawn on by a session of its own, as
/// <see cref="PushBoundedLayer"/>. Its image has one plane per frame the group is drawn on, which starts with the
/// pixels under the group when it is not isolated (and transparent otherwise), and, when it is not isolated, one more
/// plane that starts transparent: every draw call composites onto every plane, so that plane holds the group alone, and
/// its alpha is the alpha of the group (the alpha of source-over does not depend on the blend mode). When the group
/// ends, the backdrop is removed from each plane (ISO 32000-2, 11.4.8, the compositing of a non-isolated group:
/// <c>C = Cn + (Cn - C0)(a0 / ag - a0)</c>, with the alpha <c>ag</c> of the group), the mask and the opacity multiply
/// the alpha, and each plane is drawn on its frame with the blend mode, as a layer.
/// </para>
/// <para>
/// <b>Knockout.</b> An element of a knockout group (<see cref="PushKnockoutElement"/>) is a bounded layer of its own,
/// with a plane per plane of the group, each starting with the initial backdrop of that plane. The callback draws the
/// element with an opacity of 1; the element ends as <c>(1 - f) C + f K0 + q (X - K0)</c>, premultiplied, where
/// <c>C</c> is what the group had, <c>K0</c> its initial backdrop, <c>X</c> the element drawn on it, <c>f</c> the alpha
/// of the element on a transparent backdrop (its shape) and <c>q</c> its opacity times its mask: the element is
/// composited with the initial backdrop of the group, and replaces what is under it in proportion to its shape (11.4.8,
/// knockout groups). The alpha of the paint of the element counts as shape.
/// </para>
/// <para>The arithmetic is made on the stored samples, in double precision, and rounded once per plane.</para>
/// </remarks>
internal sealed partial class DrawingSession
{
    // The group this session draws the content of, when it is the session of a group
    private GroupState? _group;

    /// <summary>Gets a value indicating whether the session draws under a projective transformation: groups are then drawn as layers.</summary>
    public bool IsProjective => _isProjective;

    /// <summary>Starts a group: a layer whose drawings are made by the session it returns, until <see cref="PopLayer"/>.</summary>
    /// <param name="bounds">The rectangle the group can change, in the coordinates of the drawing, or <see langword="null"/> for the bounds of the clip.</param>
    /// <param name="opacity">The opacity of the group, which multiplies its alpha.</param>
    /// <param name="blend">The blend mode that mixes the colors of the group with those under it.</param>
    /// <param name="isIsolated"><see langword="false"/> for a group whose drawings mix with what is under it.</param>
    /// <param name="isKnockout"><see langword="true"/> for a group whose elements knock out one another.</param>
    /// <param name="maskMode">How the values of the mask of the group are read (<see cref="PushGroupMask"/>).</param>
    /// <returns>The session of the content of the group.</returns>
    /// <exception cref="ArgumentException">A coordinate of the rectangle is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The opacity, the blend mode or the mask mode is not valid.</exception>
    /// <exception cref="ImageResourceLimitException">The images of the group exceed the limits of the image that is drawn on.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session and the images are owned by the level of the layer, released by PopLayer.")]
    public DrawingSession PushGroup(RectangleD? bounds, double opacity, BlendMode blend, bool isIsolated, bool isKnockout, MaskMode maskMode)
    {
        _ = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));
        if ((uint)blend > (uint)BlendMode.Luminosity)
            throw new ArgumentOutOfRangeException(nameof(blend), blend, "The blend mode is not valid.");

        ValidateMaskMode(maskMode, nameof(maskMode));
        if (bounds is { } rectangle)
        {
            DrawingGuard.Rectangle(rectangle, nameof(bounds));
        }

        _cancellationToken.ThrowIfCancellationRequested();

        // The pixels of the frames the clip can show, within the rectangle
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, Width);
        bottom = Math.Min(bottom, Height);
        if (bounds is { } limit)
        {
            var box = ToCanvasBounds(limit);
            left = Math.Max(left, (int)Math.Clamp(Math.Floor(box.Left), 0, Width));
            top = Math.Max(top, (int)Math.Clamp(Math.Floor(box.Top), 0, Height));
            right = Math.Min(right, (int)Math.Clamp(Math.Ceiling(box.Right), 0, Width));
            bottom = Math.Min(bottom, (int)Math.Clamp(Math.Ceiling(box.Bottom), 0, Height));
        }

        var contentCount = isIsolated ? 1 : _frames.Length;
        var planeCount = isIsolated ? 1 : contentCount + 1;
        var state = new GroupState
        {
            IsIsolated = isIsolated,
            IsKnockout = isKnockout,
            Opacity = opacity,
            Blend = blend,
            MaskMode = maskMode,
            ContentCount = contentCount,
        };

        // A non-isolated group starts with the pixels under it, which it keeps to remove them when it ends
        BoundedLayer bounded;
        try
        {
            bounded = CreateGroupImage(left, top, right, bottom, planeCount, (frame, isEmpty) =>
        {
            if (isIsolated || isEmpty)
                return;

            var backdrop = CreatePlanes(right - left, bottom - top, contentCount);
            state.Backdrop = backdrop;
            for (var i = 0; i < contentCount; i++)
            {
                CopyRows(_frames[i], _storages[i], left, top, frame(i), right - left, bottom - top);
                CopyRows(_frames[i], _storages[i], left, top, backdrop.Frames[i].GetStorage(), right - left, bottom - top);
            }
            });
        }
        catch
        {
            state.Backdrop?.Dispose();
            throw;
        }

        bounded.Session._group = state;
        PushGroupLevel(bounded, state);
        return bounded.Session;
    }

    /// <summary>
    /// Starts an element of a knockout group when this session draws one (and no layer of its own is open), or a
    /// non-isolated group of the element otherwise, which draws it as it would be drawn with that opacity and mask.
    /// </summary>
    /// <param name="opacity">The opacity of the element; the callback draws it with an opacity of 1.</param>
    /// <param name="maskMode">How the values of the mask of the element are read (<see cref="PushGroupMask"/>).</param>
    /// <returns>The session the element is drawn by.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The opacity or the mask mode is not valid.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the element exceeds the limits of the image that is drawn on.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session and the images are owned by the level of the layer, released by PopLayer.")]
    public DrawingSession PushKnockoutElement(double opacity, MaskMode maskMode)
    {
        if (_group is not { IsKnockout: true } target || _layerDepth != 0)
            return PushGroup(bounds: null, opacity, BlendMode.Normal, isIsolated: false, isKnockout: false, maskMode);

        _ = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));
        ValidateMaskMode(maskMode, nameof(maskMode));
        _cancellationToken.ThrowIfCancellationRequested();
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        left = Math.Max(left, 0);
        top = Math.Max(top, 0);
        right = Math.Min(right, Width);
        bottom = Math.Min(bottom, Height);

        // Each plane of the element starts with the initial backdrop of the plane of the group
        var state = new GroupState { IsElement = true, Target = target, Opacity = opacity, MaskMode = maskMode };
        var bounded = CreateGroupImage(left, top, right, bottom, _frames.Length, (frame, isEmpty) =>
        {
            if (isEmpty || target.Backdrop is not { } backdrop)
                return;

            for (var i = 0; i < target.ContentCount; i++)
            {
                var source = backdrop.Frames[i];
                CopyRows(source, source.GetStorage(), left, top, frame(i), right - left, bottom - top);
            }
        });
        PushGroupLevel(bounded, state);
        return bounded.Session;
    }

    /// <summary>Starts the mask of the group or of the element that was started last: its alpha, or its luminance, multiplies the alpha of the group.</summary>
    /// <returns>The session the mask is drawn by.</returns>
    /// <exception cref="InvalidOperationException">The layer that was started last is not a group, or it has a mask already.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the mask exceeds the limits of the image that is drawn on.</exception>
    public DrawingSession PushGroupMask()
    {
        if (_layerDepth == 0 || _layers[_layerDepth - 1] is not { Group: { Mask: null } state, Bounded: { } bounded })
            throw new InvalidOperationException("The layer that was started last is not a group without mask.");

        var image = bounded.Image;
        state.Mask = CreateBoundedLayer(bounded.Left, bounded.Top, bounded.IsEmpty ? bounded.Left : bounded.Left + image.Width, bounded.IsEmpty ? bounded.Top : bounded.Top + image.Height);
        return state.Mask.Session;
    }

    /// <summary>Creates the image of a group or of an element, of 16-bit planes, and the session that draws on all of them.</summary>
    /// <param name="left">The left of the pixels of the frames the image holds.</param>
    /// <param name="top">The top of the pixels of the frames the image holds.</param>
    /// <param name="right">The right of the pixels of the frames the image holds.</param>
    /// <param name="bottom">The bottom of the pixels of the frames the image holds.</param>
    /// <param name="planeCount">The number of planes.</param>
    /// <param name="initialize">Writes the initial pixels of the planes, given the storage of each one and whether the image holds no pixel.</param>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session and the image are owned by the level of the layer, released by PopLayer.")]
    private BoundedLayer CreateGroupImage(int left, int top, int right, int bottom, int planeCount, Action<Func<int, PixelStorage>, bool> initialize)
    {
        var isEmpty = !(right > left && bottom > top);
        var width = isEmpty ? 1 : right - left;
        var height = isEmpty ? 1 : bottom - top;
        _image.Configuration.Limits.EnsureCanvasWithinLimits(width, height);
        var image = CreatePlanes(width, height, planeCount);
        DrawingSession? session = null;
        try
        {
            var frames = new ImageFrame[planeCount];
            var storages = new PixelStorage[planeCount];
            for (var i = 0; i < planeCount; i++)
            {
                frames[i] = image.Frames[i];
                storages[i] = frames[i].GetStorage();
            }

            initialize(i => storages[i], isEmpty);
            session = Begin(image, frames, storages, _cancellationToken);
            InheritRendering(session);
            session._originX = _originX + left;
            session._originY = _originY + top;
            session._clipsGeometry = _clipsGeometry;
            session._isLayerFrame = true;
            session._tracksDrawn = true;
            session._canvasBounds = new Rectangle(_canvasBounds.X - left, _canvasBounds.Y - top, _canvasBounds.Width, _canvasBounds.Height);
            var toImage = AffineTransform.CreateTranslation(-left, -top);
            session.PushTransform(_hasTransform ? _transform.Then(toImage) : toImage);
            return new BoundedLayer(image, session, left, top, isEmpty);
        }
        catch
        {
            session?.Dispose();
            image.Dispose();
            throw;
        }
    }

    /// <summary>Creates an image of transparent 16-bit planes.</summary>
    private Image<Rgba64> CreatePlanes(int width, int height, int planeCount)
    {
        var image = new Image<Rgba64>(_image.Configuration, new Size(width, height), _image.Owner.Scope, layoutOptions: null);
        try
        {
            for (var i = 1; i < planeCount; i++)
            {
                image.AppendFrameUnchecked();
            }

            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private void PushGroupLevel(BoundedLayer bounded, GroupState state)
    {
        if (_layerDepth == _layers.Count)
        {
            _layers.Add(new LayerLevel());
        }

        var level = _layers[_layerDepth];
        level.Bounded = bounded;
        level.Group = state;
        level.AlphaFactor = 1;
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = state.Blend;
        level.Effect = LayerEffect.None;
        level.IsLuminanceMask = false;
        _layerDepth++;
    }

    /// <summary>Copies the pixels of a rectangle of a frame to the first pixels of a plane of 16-bit samples.</summary>
    private void CopyRows(ImageFrame source, PixelStorage sourceStorage, int left, int top, PixelStorage destination, int width, int height)
    {
        using var row = ResampleWeights.Rent<Rgba64>(_image.Owner.Scope, width);
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(row.Span))[..width];
        for (var y = 0; y < height; y++)
        {
            source.ReadRgba64(sourceStorage, top + y, left, pixels);
            var bytes = unsafe(MemoryMarshal.AsBytes(pixels));
            bytes.CopyTo(destination.GetRowSpanCore(y));
        }
    }

    /// <summary>Ends a group or an element: computes its pixels, then draws them on what is under it.</summary>
    private void PopGroup(LayerLevel level, BoundedLayer bounded, GroupState state, bool draw)
    {
        var mask = state.Mask;
        try
        {
            var area = bounded.Session.GetDrawnBounds();
            var maskArea = mask?.Session.GetDrawnBounds() ?? default;
            bounded.Session.Dispose();
            mask?.Session.Dispose();
            if (mask is not null)
            {
                // The mask is transparent where it was not drawn on: the group shows nothing there
                area = Rectangle.Intersect(area, maskArea);
            }

            if (!draw || bounded.IsEmpty || area.IsEmpty)
                return;

            if (state.IsElement)
            {
                EndKnockoutElement(level, bounded, state, area);
            }
            else
            {
                EndGroup(level, bounded, state, area);
            }
        }
        finally
        {
            bounded.Image.Dispose();
            mask?.Image.Dispose();
            if (!state.IsElement)
            {
                state.Backdrop?.Dispose();
            }
        }
    }

    /// <summary>Removes the backdrop of a non-isolated group, applies its mask and its opacity, and draws each of its planes on its frame.</summary>
    private void EndGroup(LayerLevel level, BoundedLayer bounded, GroupState state, Rectangle area)
    {
        var planes = bounded.Image.Frames;
        var maskRow = state.Mask is null ? null : ResampleWeights.Rent<double>(_image.Owner.Scope, area.Width);
        try
        {
            for (var y = area.Top; y < area.Bottom; y++)
            {
                if ((y & 63) == 0)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                var maskValues = maskRow is null ? default : unsafe(MemoryMarshal.Cast<byte, double>(maskRow.Span))[..area.Width];
                if (maskRow is not null)
                {
                    ReadMask(state.Mask!, state.MaskMode, y, area.Left, maskValues);
                }

                var groupAlpha = state.IsIsolated ? default : Row(planes[state.ContentCount].GetStorage(), y).Slice(area.Left, area.Width);
                for (var i = 0; i < state.ContentCount; i++)
                {
                    var pixels = Row(planes[i].GetStorage(), y).Slice(area.Left, area.Width);
                    var backdrop = state.Backdrop is null ? default : Row(state.Backdrop.Frames[i].GetStorage(), y).Slice(area.Left, area.Width);
                    for (var x = 0; x < pixels.Length; x++)
                    {
                        ref var pixel = ref pixels[x];
                        var alpha = pixel.A / 65535.0;
                        if (!state.IsIsolated)
                        {
                            // 11.4.8: C = Cn + (Cn - C0)(a0 / ag - a0), with the alpha of the group alone
                            alpha = groupAlpha[x].A / 65535.0;
                            if (alpha == 0)
                            {
                                pixel = default;
                                continue;
                            }

                            var initial = backdrop[x];
                            var a0 = initial.A / 65535.0;
                            var factor = (a0 / alpha) - a0;
                            pixel.R = RemoveBackdrop(pixel.R, initial.R, factor);
                            pixel.G = RemoveBackdrop(pixel.G, initial.G, factor);
                            pixel.B = RemoveBackdrop(pixel.B, initial.B, factor);
                        }

                        alpha *= state.Opacity * (maskRow is null ? 1 : maskValues[x]);
                        pixel.A = ToSample(alpha);
                    }
                }
            }
        }
        finally
        {
            maskRow?.Dispose();
        }

        // Each plane is drawn on its frame (an isolated group, on every frame) as a layer of its opacity and blend mode
        level.Mode = CompositeMode.SourceOver;
        level.Blend = state.Blend;
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.AlphaFactor = 1;
        DrawPlanes(bounded, level, area, state.ContentCount);
    }

    /// <summary>Ends an element of a knockout group: replaces the pixels of the group by the element composited with the initial backdrop, in proportion to its shape.</summary>
    private void EndKnockoutElement(LayerLevel level, BoundedLayer bounded, GroupState state, Rectangle area)
    {
        var target = state.Target!;
        var planes = bounded.Image.Frames;
        var shapePlane = target.IsIsolated ? 0 : _frames.Length - 1;
        var maskRow = state.Mask is null ? null : ResampleWeights.Rent<double>(_image.Owner.Scope, area.Width);
        try
        {
            for (var y = area.Top; y < area.Bottom; y++)
            {
                if ((y & 63) == 0)
                {
                    _cancellationToken.ThrowIfCancellationRequested();
                }

                var maskValues = maskRow is null ? default : unsafe(MemoryMarshal.Cast<byte, double>(maskRow.Span))[..area.Width];
                if (maskRow is not null)
                {
                    ReadMask(state.Mask!, state.MaskMode, y, area.Left, maskValues);
                }

                var shapes = Row(planes[shapePlane].GetStorage(), y).Slice(area.Left, area.Width);
                var groupY = bounded.Top + y;
                for (var i = 0; i < planes.Count; i++)
                {
                    var pixels = Row(planes[i].GetStorage(), y).Slice(area.Left, area.Width);
                    var current = Row(_storages[i], groupY).Slice(bounded.Left + area.Left, area.Width);
                    var initial = target.Backdrop is { } backdrop && i < target.ContentCount ? Row(backdrop.Frames[i].GetStorage(), groupY).Slice(bounded.Left + area.Left, area.Width) : default;
                    for (var x = 0; x < pixels.Length; x++)
                    {
                        var shape = shapes[x].A / 65535.0;
                        var weight = state.Opacity * (maskRow is null ? 1 : maskValues[x]);
                        var k0 = initial.IsEmpty ? default : initial[x];
                        pixels[x] = Knockout(current[x], k0, pixels[x], shape, weight);
                    }
                }
            }
        }
        finally
        {
            maskRow?.Dispose();
        }

        // The pixels replace those of the group, through the clip the element started under
        level.Mode = CompositeMode.Source;
        level.Blend = BlendMode.Normal;
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.AlphaFactor = 1;
        DrawPlanes(bounded, level, area, planes.Count);
    }

    /// <summary>Draws the first planes of the image of a group on the frames: one plane on every frame, or each plane on its frame.</summary>
    private void DrawPlanes(BoundedLayer bounded, LayerLevel level, Rectangle area, int planeCount)
    {
        var planes = bounded.Image.Frames;
        var placement = AffineTransform.CreateTranslation(bounded.Left, bounded.Top);
        var bounds = new Rectangle(bounded.Left + area.Left, bounded.Top + area.Top, area.Width, area.Height);
        if (planeCount == 1)
        {
            DrawLayerFrame(planes[0], bounds, level, placement);
            return;
        }

        var frames = _frames;
        var storages = _storages;
        try
        {
            for (var i = 0; i < frames.Length; i++)
            {
                _frames = [frames[i]];
                _storages = [storages[i]];
                DrawLayerFrame(planes[i], bounds, level, placement);
            }
        }
        finally
        {
            _frames = frames;
            _storages = storages;
        }
    }

    /// <summary>Reads the values of a row of a mask: its alpha, or its Rec. 709 luma times its alpha.</summary>
    private void ReadMask(BoundedLayer mask, MaskMode mode, int y, int left, Span<double> values)
    {
        using var row = ResampleWeights.Rent<Rgba64>(_image.Owner.Scope, values.Length);
        var pixels = unsafe(MemoryMarshal.Cast<byte, Rgba64>(row.Span))[..values.Length];
        var frame = mask.Image.Frames[0];
        frame.ReadRgba64(frame.GetStorage(), y, left, pixels);
        for (var x = 0; x < values.Length; x++)
        {
            var pixel = pixels[x];
            var alpha = pixel.A / 65535.0;
            values[x] = mode == MaskMode.Luminance ? alpha * PixelConverter.Luma(pixel.R, pixel.G, pixel.B, 65535) / 65535.0 : alpha;
        }
    }

    /// <summary>
    /// Composites an element of a knockout group (ISO 32000-2, 11.4.8): <c>(1 - f) C + f K0 + q (X - K0)</c>, with
    /// premultiplied colors, where <c>X</c> is the element drawn with an opacity of 1 on the initial backdrop
    /// <c>K0</c>, <c>f</c> its shape and <c>q</c> its opacity.
    /// </summary>
    private static Rgba64 Knockout(Rgba64 current, Rgba64 initial, Rgba64 element, double shape, double weight)
    {
        var currentAlpha = current.A / 65535.0;
        var initialAlpha = initial.A / 65535.0;
        var elementAlpha = element.A / 65535.0;
        var alpha = Math.Clamp(((1 - shape) * currentAlpha) + (shape * initialAlpha) + (weight * (elementAlpha - initialAlpha)), 0, 1);
        if (alpha == 0)
            return default;

        ushort Channel(ushort c, ushort k, ushort e)
        {
            var premultiplied = ((1 - shape) * c / 65535.0 * currentAlpha) + (shape * k / 65535.0 * initialAlpha) + (weight * ((e / 65535.0 * elementAlpha) - (k / 65535.0 * initialAlpha)));
            return ToSample(premultiplied / alpha);
        }

        return new Rgba64(Channel(current.R, initial.R, element.R), Channel(current.G, initial.G, element.G), Channel(current.B, initial.B, element.B), ToSample(alpha));
    }

    private static ushort RemoveBackdrop(ushort color, ushort initial, double factor)
    {
        var value = color / 65535.0;
        return ToSample(value + ((value - (initial / 65535.0)) * factor));
    }

    private static ushort ToSample(double value) => (ushort)Math.Round(Math.Clamp(value, 0, 1) * 65535, MidpointRounding.AwayFromZero);

    private static Span<Rgba64> Row(PixelStorage storage, int y) => unsafe(MemoryMarshal.Cast<byte, Rgba64>(storage.GetRowSpanCore(y)));

    /// <summary>The state of a group, or of an element of a knockout group.</summary>
    private sealed class GroupState
    {
        public bool IsIsolated { get; init; }

        public bool IsKnockout { get; init; }

        public double Opacity { get; init; }

        public BlendMode Blend { get; init; }

        public MaskMode MaskMode { get; init; }

        /// <summary>Gets the number of planes of the group that are drawn on a frame; a non-isolated group has one more, the group alone.</summary>
        public int ContentCount { get; init; }

        /// <summary>Gets or sets the initial backdrop of each content plane of a non-isolated group, or <see langword="null"/> when it is transparent.</summary>
        public Image<Rgba64>? Backdrop { get; set; }

        /// <summary>Gets or sets the mask of the group, once it is started.</summary>
        public BoundedLayer? Mask { get; set; }

        /// <summary>Gets a value indicating whether the state is the one of an element of the knockout group <see cref="Target"/>.</summary>
        public bool IsElement { get; init; }

        public GroupState? Target { get; init; }
    }
}
