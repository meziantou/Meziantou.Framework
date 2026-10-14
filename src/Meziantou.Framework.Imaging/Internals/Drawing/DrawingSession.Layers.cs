using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Layers: drawings made on a transparent image of their own, which is then drawn on the canvas as a whole.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> A layer is an image of the size of the canvas, with colors and alpha: <see cref="Rgba32"/> for the
/// 8-bit color formats, whose paint colors it holds exactly, and <see cref="Rgba64"/> for the others (a gray level
/// comes from 16-bit colors, so an 8-bit layer would not give a grayscale frame the gray a paint has on it). It is
/// transparent black when it starts. While it lasts, every draw call paints it instead of the canvas, with the rules of any frame of that
/// pixel format. When it ends, it is drawn on what was drawn on before it started (the frames, or the layer under
/// it) as an image is drawn at a whole position: its pixels converted to the pixel format like brush colors, combined
/// with the blend mode and the operator of the layer, the opacity of the layer multiplying the coverage as a clip does.
/// </para>
/// <para>
/// The clip that is active when a layer starts applies to the layer as a whole, when it is drawn; only its bounds are
/// used while the layer lasts, to leave out what could not be seen. The clips started inside a layer end inside it.
/// </para>
/// <para>
/// The image of a layer is created when a layer first starts at its depth, from the allocation scope of the image that
/// is drawn on, and kept until the session ends: a layer that starts again at that depth allocates nothing.
/// </para>
/// <para>
/// <b>Masks.</b> A mask is a layer drawn with <see cref="CompositeMode.DestinationIn"/> on the layer of what it masks:
/// what is under it is kept in proportion to its alpha. A luminance mask first replaces the alpha <c>a</c> of each of
/// its pixels by <c>round(a Y / M)</c> (to nearest, ties upward), where <c>Y</c> is the Rec. 709 luma of the pixel
/// (<see cref="PixelConverter.Luma"/>, on the stored samples) and <c>M</c> the largest sample value: white keeps,
/// black removes.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    private readonly List<LayerLevel> _layers = [];
    private int _layerDepth;

    /// <summary>Starts a layer: the draw calls that follow paint it, until <see cref="PopLayer"/>.</summary>
    /// <param name="opacity">The opacity the layer is drawn with, from 0 to 1.</param>
    /// <param name="mode">The operator that combines the layer with what is under it.</param>
    /// <param name="blend">The blend mode that mixes the colors of the layer with those of what is under it.</param>
    /// <exception cref="ArgumentOutOfRangeException">The opacity is not between 0 and 1, or the operator or the blend mode is not defined.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    public void PushLayer(double opacity, CompositeMode mode, BlendMode blend)
    {
        var coverage = ImageEvaluator.GetOpacityCoverage(opacity, nameof(opacity));
        if ((uint)mode > (uint)CompositeMode.Plus)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The composite mode is not valid.");

        if ((uint)blend > (uint)BlendMode.Luminosity)
            throw new ArgumentOutOfRangeException(nameof(blend), blend, "The blend mode is not valid.");

        var level = PushLayerCore(margin: 0);
        level.Opacity = coverage;
        level.Mode = mode;
        level.Blend = blend;
        level.Effect = LayerEffect.None;
        level.IsLuminanceMask = false;
    }

    /// <summary>Validates the mode of a mask.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The mode is not defined.</exception>
    public static void ValidateMaskMode(MaskMode mode, string paramName)
    {
        if (mode is not (MaskMode.Alpha or MaskMode.Luminance))
            throw new ArgumentOutOfRangeException(paramName, mode, "The mask mode is not valid.");
    }

    /// <summary>Starts the layer of a mask: what it is drawn on (the layer of what it masks) is kept where its drawings are, when it ends.</summary>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    public void PushMask(MaskMode mode)
    {
        var level = PushLayerCore(margin: 0);
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.DestinationIn;
        level.Blend = BlendMode.Normal;
        level.Effect = LayerEffect.None;
        level.IsLuminanceMask = mode == MaskMode.Luminance;
    }

    /// <summary>Starts a layer that is blurred when it ends, before it is drawn.</summary>
    /// <param name="sigma">The standard deviation of the blur.</param>
    /// <returns>The session the drawings of the layer are made by: this one, or the one of an image of the plane of the drawing under a projective transformation.</returns>
    /// <exception cref="ArgumentException">The standard deviation is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The standard deviation is negative or too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    public DrawingSession PushBlur(double sigma)
    {
        GaussianKernel.Validate(sigma, nameof(sigma));
        if (_isProjective)
        {
            // Blurred in the plane of the drawing, then seen in perspective
            var session = PushProjectedLayer(GaussianKernel.GetRadius(sigma) + 1);
            try
            {
                return session.PushBlur(sigma);
            }
            catch
            {
                DiscardProjectedLayer();
                throw;
            }
        }

        sigma = ToCanvasBlur(sigma);

        // What is drawn around the pixels the clip can show spreads over them: the layer keeps that much more
        var level = PushLayerCore(Math.Min(GaussianKernel.GetRadius(sigma), Math.Max(Width, Height)));
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.Effect = sigma > 0 ? LayerEffect.Blur : LayerEffect.None;
        level.IsLuminanceMask = false;
        level.BlurSigma = sigma;
        return this;
    }

    /// <summary>
    /// Gets the standard deviation a blur of the drawing has on the canvas: a transformation that enlarges the drawing
    /// blurs it more, by the square root of what it does to an area, or by the factor of a projective transformation
    /// where W is the one of the origin.
    /// </summary>
    private double ToCanvasBlur(double sigma)
        => _isProjective ? Math.Min(sigma * _transformScale, GaussianKernel.MaxSigma)
        : _hasTransform ? Math.Min(sigma * Math.Sqrt(Math.Abs(_transform.Determinant)), GaussianKernel.MaxSigma)
        : sigma;

    /// <summary>Starts a layer whose drawings are drawn with their shadow when it ends.</summary>
    /// <param name="color">The color of the shadow; its alpha is the opacity of the shadow of an opaque drawing.</param>
    /// <param name="offset">The distance from the drawing to its shadow.</param>
    /// <param name="sigma">The standard deviation of the blur of the shadow.</param>
    /// <exception cref="ArgumentException">A coordinate of the offset, or the standard deviation, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The offset is beyond the largest coordinate, or the standard deviation is negative or too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    /// <param name="isInner"><see langword="true"/> for a shadow cast on the drawings by what is around them, instead of the one they cast.</param>
    /// <returns>The session the drawings of the layer are made by: this one, or the one of an image of the plane of the drawing under a projective transformation.</returns>
    public DrawingSession PushShadow(Rgba64 color, PointD offset, double sigma, bool isInner = false)
    {
        DrawingGuard.Point(offset, nameof(offset));
        GaussianKernel.Validate(sigma, nameof(sigma));
        if (_isProjective)
        {
            // The shadow is cast in the plane of the drawing, then seen in perspective with it
            var session = PushProjectedLayer(GaussianKernel.GetRadius(sigma) + Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y)) + 1);
            try
            {
                return session.PushShadow(color, offset, sigma, isInner);
            }
            catch
            {
                DiscardProjectedLayer();
                throw;
            }
        }

        if (_hasTransform)
        {
            // The offset is a distance of the drawing, and so is the blur: a transformation that enlarges the drawing
            // moves its shadow further and blurs it more, by the square root of what it does to an area
            offset = _transform.TransformVector(offset);
            sigma = ToCanvasBlur(sigma);
            if (!(Math.Abs(offset.X) <= DrawingGuard.MaxCoordinate && Math.Abs(offset.Y) <= DrawingGuard.MaxCoordinate))
                TransformSink.ThrowOutOfRange(Math.Abs(offset.X) <= DrawingGuard.MaxCoordinate ? offset.Y : offset.X);
        }

        // What is drawn around the pixels the clip can show casts a shadow on them: the layer keeps that much more
        var reach = GaussianKernel.GetRadius(sigma) + Math.Ceiling(Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y))) + 1;
        var level = PushLayerCore((int)Math.Min(reach, Math.Max(Width, Height)));
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.Effect = isInner ? LayerEffect.InnerShadow : LayerEffect.Shadow;
        level.IsLuminanceMask = false;
        level.ShadowColor = ToImageColor(color);
        level.ShadowOffsetX = offset.X;
        level.ShadowOffsetY = offset.Y;
        level.BlurSigma = sigma;
        return this;
    }

    /// <summary>Starts a layer on the pixels the clip can show, and on a margin around them.</summary>
    private LayerLevel PushLayerCore(int margin) => PushLayerCore(margin, frameCount: 1);

    /// <summary>Starts a layer of one or several frames on the pixels the clip can show, and on a margin around them.</summary>
    /// <param name="margin">The pixels around the bounds of the clip that the layer holds too.</param>
    /// <param name="frameCount">The number of frames of the layer: one, or one per frame that is drawn on, when each starts with what is under it.</param>
    private LayerLevel PushLayerCore(int margin, int frameCount)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        if (_layerDepth == _layers.Count)
        {
            _layers.Add(new LayerLevel());
        }

        var level = _layers[_layerDepth];
        level.VisibleLeft = left;
        level.VisibleTop = top;
        level.VisibleRight = right;
        level.VisibleBottom = bottom;
        if (right > left && bottom > top)
        {
            left = Math.Max(0, left - margin);
            top = Math.Max(0, top - margin);
            right = Math.Min(Width, right + margin);
            bottom = Math.Min(Height, bottom + margin);
        }

        level.Begin(_image, _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24), Width, Height, left, top, right, bottom, frameCount);
        level.ParentFrames = _frames;
        level.ParentStorages = _storages;
        _frames = level.Frames!;
        _storages = level.Storages!;
        _layerDepth++;

        // What the clip under the layer cannot show is not drawn: a rectangle of whole pixels, which costs nothing.
        // The mask of that clip is applied when the layer is drawn.
        var clip = GetNextLevel();
        clip.Left = left;
        clip.Top = top;
        clip.Right = right;
        clip.Bottom = bottom;
        clip.MaskLevel = -1;
        _clipDepth++;
        return level;
    }

    /// <summary>Ends the layer that was started last, and draws it on what is under it.</summary>
    /// <param name="draw"><see langword="false"/> to discard the layer: the drawing that was made on it failed.</param>
    public void PopLayer(bool draw)
    {
        var level = _layers[--_layerDepth];
        if (level.Projected is { } scope)
        {
            // A layer of an image of the plane of the drawing: it ends in its own session, then the image is drawn in perspective
            level.Projected = null;
            try
            {
                scope.Session.PopLayer(draw);
            }
            catch
            {
                scope.Release();
                throw;
            }

            EndProjectedScope(scope, draw, CompositeMode.SourceOver, BlendMode.Normal);
            return;
        }

        _clipDepth--;
        _frames = level.ParentFrames!;
        _storages = level.ParentStorages!;
        level.ParentFrames = null;
        level.ParentStorages = null;
        var isEmpty = level.Opacity == 0 || level.Right <= level.Left || level.Bottom <= level.Top;
        try
        {
            if (draw && !isEmpty)
            {
                // The pixels of the layer are still held: what changes them comes before they are drawn
                switch (level.Effect)
                {
                    case LayerEffect.Shadow:
                        DrawShadow(level, isInner: false);
                        break;

                    case LayerEffect.InnerShadow:
                        DrawShadow(level, isInner: true);
                        break;

                    case LayerEffect.Blur:
                        BlurLayer(level);
                        break;

                    case LayerEffect.ColorMatrix:
                        FilterLayer(level);
                        break;

                    case LayerEffect.Filter:
                        ApplyFilterLayer(level);
                        break;
                }

                if (level.IsLuminanceMask)
                {
                    ApplyLuminance(level);
                }
            }
        }
        finally
        {
            level.End();
        }

        if (!draw || isEmpty)
            return;

        // The layer is an image at the origin of the canvas: its pixels are those of the canvas, read as they are. A
        // layer of several frames has one for each frame it is drawn on.
        var bounds = Rectangle.FromLTRB(level.Left, level.Top, level.Right, level.Bottom);
        var layerFrames = level.Frames!;
        if (layerFrames.Length == 1)
        {
            DrawLayerFrame(layerFrames[0], bounds, level);
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
                DrawLayerFrame(layerFrames[i], bounds, level);
            }
        }
        finally
        {
            _frames = frames;
            _storages = storages;
        }
    }

    /// <summary>Draws a frame of a layer on what is drawn on.</summary>
    private void DrawLayerFrame(ImageFrame frame, Rectangle bounds, LayerLevel level)
    {
        var sink = BeginDeviceFill();
        AddRectangle(sink, bounds);
        sink.End();
        var evaluator = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
        evaluator.Prepare(frame, bounds, AffineTransform.Identity, ImageWrapMode.Transparent, ImageSampling.Nearest, _frames[0], Width, "draw");
        if (level.Opacity != CoverageRasterizer.FullCoverage)
        {
            EnsureCoverageRow();
        }

        if (_rasterizer!.Begin(_edges!, FillRule.NonZero, _cancellationToken))
        {
            Paint(default, evaluator, level.Mode, level.Blend, level.Opacity);
        }
    }

    /// <summary>Replaces the alpha of the pixels of a layer by their alpha multiplied by their luma.</summary>
    private void ApplyLuminance(LayerLevel level)
    {
        var storage = level.Storages![0];
        var count = level.Right - level.Left;
        for (var y = level.Top; y < level.Bottom; y++)
        {
            if ((y & 63) == 0)
            {
                _cancellationToken.ThrowIfCancellationRequested();
            }

            var row = storage.GetRowSpanCore(y);
            if (storage.BytesPerPixel == 8)
            {
                foreach (ref var pixel in unsafe(MemoryMarshal.Cast<byte, Rgba64>(row)).Slice(level.Left, count))
                {
                    if (pixel.A != 0)
                    {
                        pixel.A = (ushort)((((ulong)pixel.A * PixelConverter.Luma(pixel.R, pixel.G, pixel.B, ushort.MaxValue)) + (ushort.MaxValue / 2)) / ushort.MaxValue);
                    }
                }
            }
            else
            {
                foreach (ref var pixel in unsafe(MemoryMarshal.Cast<byte, Rgba32>(row)).Slice(level.Left, count))
                {
                    if (pixel.A != 0)
                    {
                        pixel.A = (byte)(((pixel.A * PixelConverter.Luma(pixel.R, pixel.G, pixel.B, byte.MaxValue)) + (byte.MaxValue / 2)) / byte.MaxValue);
                    }
                }
            }
        }
    }

    private void DisposeLayers()
    {
        // A session that ends inside a layer leaves what it was drawing on as the frames of the image
        while (_layerDepth > 0)
        {
            var level = _layers[--_layerDepth];
            if (level.Projected is { } scope)
            {
                level.Projected = null;
                scope.Release();
                continue;
            }

            _frames = level.ParentFrames!;
            _storages = level.ParentStorages!;
            level.End();
        }

        foreach (var level in _layers)
        {
            level.Dispose();
        }

        _layers.Clear();
        DisposeShadows();
        DisposeBlur();
        DisposeFilters();
        DisposeFilterEffects();
    }

    /// <summary>What changes the pixels of a layer, or is drawn with it, when it ends.</summary>
    private enum LayerEffect : byte
    {
        None,
        Shadow,
        InnerShadow,
        Blur,
        ColorMatrix,
        Filter,
    }

    /// <summary>A layer of the stack: its image, what was drawn on before it started, and how it is drawn when it ends.</summary>
    private sealed class LayerLevel : IDisposable
    {
        private Image? _image;
        private long[]? _tokens;

        public ImageFrame[]? Frames { get; private set; }

        public PixelStorage[]? Storages { get; private set; }

        public ImageFrame[]? ParentFrames { get; set; }

        public PixelStorage[]? ParentStorages { get; set; }

        public uint Opacity { get; set; }

        /// <summary>Gets or sets the session on an image of the plane of the drawing the layer is drawn by, under a projective transformation; the other properties are then not used.</summary>
        public ProjectedScope? Projected { get; set; }

        public CompositeMode Mode { get; set; }

        public BlendMode Blend { get; set; }

        /// <summary>Gets or sets what changes the pixels of the layer, or is drawn with it, when it ends.</summary>
        public LayerEffect Effect { get; set; }

        /// <summary>Gets or sets a value indicating whether the alpha of the layer is multiplied by its luma when it ends.</summary>
        public bool IsLuminanceMask { get; set; }

        public Rgba64 ShadowColor { get; set; }

        public double ShadowOffsetX { get; set; }

        public double ShadowOffsetY { get; set; }

        /// <summary>Gets or sets the color matrix the pixels of the layer go through when it ends.</summary>
        public ColorMatrix? Matrix { get; set; }

        /// <summary>Gets or sets the filter the pixels of the layer go through when it ends.</summary>
        public Filter? Filter { get; set; }

        /// <summary>Gets or sets the scales of the lengths of <see cref="Filter"/> along x and along y.</summary>
        public (double X, double Y) FilterScale { get; set; }

        /// <summary>Gets or sets the standard deviation of the blur of the shadow of the layer, or of the layer itself.</summary>
        public double BlurSigma { get; set; }

        /// <summary>Gets or sets the left of the pixels the clip the layer started under can show: the layer is drawn on them only.</summary>
        public int VisibleLeft { get; set; }

        public int VisibleTop { get; set; }

        public int VisibleRight { get; set; }

        public int VisibleBottom { get; set; }

        /// <summary>Gets the pixels of the layer that can be drawn on: the bounds of the clip it started under, and for a shadow what is around them and casts a shadow on them.</summary>
        public int Left { get; private set; }

        public int Top { get; private set; }

        public int Right { get; private set; }

        public int Bottom { get; private set; }

        /// <summary>Creates the image of the layer if it has none, makes the pixels it can draw on transparent, and holds them until <see cref="End"/>.</summary>
        public void Begin(Image owner, bool isWide, int width, int height, int left, int top, int right, int bottom, int frameCount)
        {
            if (_image is null)
            {
                Image image = isWide
                    ? new Image<Rgba64>(owner.Configuration, new Size(width, height), owner.Owner.Scope, layoutOptions: null)
                    : new Image<Rgba32>(owner.Configuration, new Size(width, height), owner.Owner.Scope, layoutOptions: null);
                _image = image;
            }

            // A frame per frame that is drawn on when each one starts with what is under it: added once, and kept
            while (_image.Frames.Count < frameCount)
            {
                _image.AppendFrame();
            }

            if (Frames is null || Frames.Length != frameCount)
            {
                Frames = new ImageFrame[frameCount];
                Storages = new PixelStorage[frameCount];
                _tokens = new long[frameCount];
                for (var i = 0; i < frameCount; i++)
                {
                    Frames[i] = _image.Frames[i];
                    Storages[i] = Frames[i].GetStorage();
                }
            }

            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            for (var i = 0; i < frameCount; i++)
            {
                var storage = Storages![i];
                _tokens![i] = storage.AcquireLease().Token;
                if (right > left)
                {
                    var bytesPerPixel = storage.BytesPerPixel;
                    for (var y = top; y < bottom; y++)
                    {
                        storage.GetRowSpanCore(y).Slice(left * bytesPerPixel, (right - left) * bytesPerPixel).Clear();
                    }
                }
            }
        }

        /// <summary>Releases the pixels of the layer: they can be read to draw it.</summary>
        public void End()
        {
            if (_tokens is null)
                return;

            for (var i = 0; i < _tokens.Length; i++)
            {
                if (_tokens[i] != 0)
                {
                    var storage = Storages![i];
                    storage.Owner.ReleaseLease(storage, _tokens[i]);
                    _tokens[i] = 0;
                }
            }
        }

        public void Dispose()
        {
            End();
            _image?.Dispose();
            _image = null;
            Frames = null;
            Storages = null;
        }
    }
}
