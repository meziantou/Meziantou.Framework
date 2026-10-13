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
        if ((uint)mode > (uint)CompositeMode.Clear)
            throw new ArgumentOutOfRangeException(nameof(mode), mode, "The composite mode is not valid.");

        if ((uint)blend > (uint)BlendMode.Luminosity)
            throw new ArgumentOutOfRangeException(nameof(blend), blend, "The blend mode is not valid.");

        var level = PushLayerCore(margin: 0);
        level.Opacity = coverage;
        level.Mode = mode;
        level.Blend = blend;
        level.HasShadow = false;
    }

    /// <summary>Starts a layer whose drawings are drawn with their shadow when it ends.</summary>
    /// <param name="color">The color of the shadow; its alpha is the opacity of the shadow of an opaque drawing.</param>
    /// <param name="offset">The distance from the drawing to its shadow.</param>
    /// <param name="sigma">The standard deviation of the blur of the shadow.</param>
    /// <exception cref="ArgumentException">A coordinate of the offset, or the standard deviation, is not a finite number.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The offset is beyond the largest coordinate, or the standard deviation is negative or too large.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the layer exceeds the allocation limit.</exception>
    public void PushShadow(Rgba64 color, PointD offset, double sigma)
    {
        DrawingGuard.Point(offset, nameof(offset));
        GaussianKernel.Validate(sigma, nameof(sigma));

        // What is drawn around the pixels the clip can show casts a shadow on them: the layer keeps that much more
        var reach = GaussianKernel.GetRadius(sigma) + Math.Ceiling(Math.Max(Math.Abs(offset.X), Math.Abs(offset.Y))) + 1;
        var level = PushLayerCore((int)Math.Min(reach, Math.Max(Width, Height)));
        level.Opacity = CoverageRasterizer.FullCoverage;
        level.Mode = CompositeMode.SourceOver;
        level.Blend = BlendMode.Normal;
        level.HasShadow = true;
        level.ShadowColor = color;
        level.ShadowOffsetX = offset.X;
        level.ShadowOffsetY = offset.Y;
        level.ShadowSigma = sigma;
    }

    /// <summary>Starts a layer on the pixels the clip can show, and on a margin around them.</summary>
    private LayerLevel PushLayerCore(int margin)
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

        level.Begin(_image, _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24), Width, Height, left, top, right, bottom);
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
        _clipDepth--;
        _frames = level.ParentFrames!;
        _storages = level.ParentStorages!;
        level.ParentFrames = null;
        level.ParentStorages = null;
        var isEmpty = level.Opacity == 0 || level.Right <= level.Left || level.Bottom <= level.Top;
        try
        {
            if (draw && !isEmpty && level.HasShadow)
            {
                DrawShadow(level);
            }
        }
        finally
        {
            level.End();
        }

        if (!draw || isEmpty)
            return;

        // The layer is an image at the origin of the canvas: its pixels are those of the canvas, read as they are
        var bounds = Rectangle.FromLTRB(level.Left, level.Top, level.Right, level.Bottom);
        var sink = BeginFill();
        AddRectangle(sink, bounds);
        sink.End();
        var evaluator = _imagePaint ??= new ImageEvaluator(_image.Owner.Scope);
        evaluator.Prepare(level.Frames![0], bounds, AffineTransform.Identity, ImageWrapMode.Transparent, ImageSampling.Nearest, _frames[0], Width, nameof(draw));
        if (level.Opacity != CoverageRasterizer.FullCoverage)
        {
            EnsureCoverageRow();
        }

        if (_rasterizer!.Begin(_edges!, FillRule.NonZero, _cancellationToken))
        {
            Paint(default, evaluator, level.Mode, level.Blend, level.Opacity);
        }
    }

    private void DisposeLayers()
    {
        // A session that ends inside a layer leaves what it was drawing on as the frames of the image
        while (_layerDepth > 0)
        {
            var level = _layers[--_layerDepth];
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
    }

    /// <summary>A layer of the stack: its image, what was drawn on before it started, and how it is drawn when it ends.</summary>
    private sealed class LayerLevel : IDisposable
    {
        private Image? _image;
        private long _token;

        public ImageFrame[]? Frames { get; private set; }

        public PixelStorage[]? Storages { get; private set; }

        public ImageFrame[]? ParentFrames { get; set; }

        public PixelStorage[]? ParentStorages { get; set; }

        public uint Opacity { get; set; }

        public CompositeMode Mode { get; set; }

        public BlendMode Blend { get; set; }

        /// <summary>Gets or sets a value indicating whether the shadow of the layer is drawn under it.</summary>
        public bool HasShadow { get; set; }

        public Rgba64 ShadowColor { get; set; }

        public double ShadowOffsetX { get; set; }

        public double ShadowOffsetY { get; set; }

        public double ShadowSigma { get; set; }

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
        public void Begin(Image owner, bool isWide, int width, int height, int left, int top, int right, int bottom)
        {
            if (_image is null)
            {
                Image image = isWide
                    ? new Image<Rgba64>(owner.Configuration, new Size(width, height), owner.Owner.Scope, layoutOptions: null)
                    : new Image<Rgba32>(owner.Configuration, new Size(width, height), owner.Owner.Scope, layoutOptions: null);
                _image = image;
                var frame = image.Frames[0];
                Frames = [frame];
                Storages = [frame.GetStorage()];
            }

            Left = left;
            Top = top;
            Right = right;
            Bottom = bottom;
            var storage = Storages![0];
            _token = storage.AcquireLease().Token;
            if (right > left)
            {
                var bytesPerPixel = storage.BytesPerPixel;
                for (var y = top; y < bottom; y++)
                {
                    storage.GetRowSpanCore(y).Slice(left * bytesPerPixel, (right - left) * bytesPerPixel).Clear();
                }
            }
        }

        /// <summary>Releases the pixels of the layer: they can be read to draw it.</summary>
        public void End()
        {
            if (_token != 0)
            {
                var storage = Storages![0];
                storage.Owner.ReleaseLease(storage, _token);
                _token = 0;
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
