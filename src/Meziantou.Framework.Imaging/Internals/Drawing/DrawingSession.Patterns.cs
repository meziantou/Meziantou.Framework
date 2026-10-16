using System.Runtime.InteropServices;
using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Patterns: the tile of a <see cref="PatternBrush"/>, drawn on an image of its own and repeated as an image is.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>T</c> the transformation that places the pattern on the canvas (the one of the brush, then
/// the one of the canvas), a tile of <c>tw</c> by <c>th</c> followed by its spacing <c>sx</c> and <c>sy</c> has, on the
/// canvas, the width <c>w = (tw + sx) sqrt(T.M11^2 + T.M12^2)</c> and the height
/// <c>h = (th + sy) sqrt(T.M21^2 + T.M22^2)</c>. Its image has <c>W = max(1, ceil(w - 1/1024))</c> columns and <c>H</c>
/// rows computed the same way, and is transparent before the callback draws on it, through a canvas of its own whose
/// transformation maps the tile and its spacing to the image (a scaling by <c>W / (tw + sx)</c> and
/// <c>H / (th + sy)</c>), with a clip of the tile when there is a spacing. The image has the samples of a layer (8-bit
/// on an 8-bit color canvas, 16-bit otherwise), or 8-bit samples with <see cref="PatternTilePrecision.EightBit"/>. The shape is then painted with that image,
/// placed by the scaling that maps it back, then by <c>T</c>, with the wrap modes of the brush and
/// <see cref="ImageSampling.Bilinear"/>. Under a projective transformation of the canvas, <c>T</c> is the transformation
/// of the brush followed by the scaling by the factor of the canvas where <c>W</c> is the one of the origin (see
/// <see cref="GetLocalScale"/>), and the image is placed by the scaling, the transformation of the brush, then the
/// projective one.
/// </para>
/// <para>
/// When that image would have more than four times the pixels of the bounds of the clip (and 65,536) or exceed the
/// limits of the image, it holds the part of the pattern the canvas shows instead (<see cref="TryGetPatternWindow"/>):
/// along each axis where the box of the image of the bounds of the clip, with 2 pixels around it, is narrower than a
/// tile, the pixels of the pattern from that box, at the same scale, in which the callback draws each tile it shows
/// (moved by whole tiles, mirrored for the odd tiles of a reflection, clipped to the tile), painted with transparent
/// pixels around them along that axis (or the edges extended, for <see cref="ImageWrapMode.Pad"/>). The pixels are
/// those of the whole image, with the drawing of a mirrored tile in place of the mirrored pixels. Otherwise, and under
/// a projective transformation, the image of the tile has fewer pixels: its width and height are multiplied by the
/// factor that brings it within those limits.
/// </para>
/// <para>
/// The image of the last tile is kept until the session ends: the shapes that follow with the same brush at the same
/// size, with the same antialiasing, flattening tolerance and compositing space (the tile is drawn as the shapes are), do not
/// draw it again. It comes from the allocation scope of the image that is drawn on.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    /// <summary>The deepest a pattern can be drawn inside the tile of another one.</summary>
    private const int MaxPatternDepth = 8;

    private int _patternDepth;
    private Image? _patternImage;
    private PatternBrush? _patternBrush;
    private PatternWindow _patternWindow;
    private bool _patternIsAliased;
    private CompositingSpace _patternSpace;
    private double _patternTolerance;
    private IccPipeline? _patternPipeline;

    /// <summary>Draws the tile of a pattern, unless it is the one that was drawn last, and gets the transformation that places its image on the canvas.</summary>
    /// <param name="brush">The pattern.</param>
    /// <param name="placement">The transformation that places the image on the canvas.</param>
    /// <param name="wrapModeX">How the image is laid along x: the mode of the brush, or transparent when the image holds what the canvas shows of the pattern along x.</param>
    /// <param name="wrapModeY">How the image is laid along y.</param>
    /// <returns>The frame that holds the tile, or <see langword="null"/> when the pattern paints nothing.</returns>
    /// <exception cref="InvalidOperationException">The pattern is drawn inside too many patterns.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the tile exceeds the limits of the image that is drawn on.</exception>
    private ImageFrame? PreparePattern(PatternBrush brush, out ProjectiveTransform placement, out ImageWrapMode wrapModeX, out ImageWrapMode wrapModeY)
    {
        placement = default;
        wrapModeX = brush.WrapModeX;
        wrapModeY = brush.WrapModeY;

        // Under a projective transformation, the tile has the size the factor of the transformation where W is the one
        // of the origin gives it
        var transform = _isProjective ? brush.Transform.Then(AffineTransform.CreateScale(_transformScale, _transformScale)) : ToCanvas(brush.Transform);
        // The tile and the spacing after it repeat together
        var tile = brush.Tile;
        var periodWidth = tile.Width + brush.SpacingX;
        var periodHeight = tile.Height + brush.SpacingY;
        var tileWidth = periodWidth * Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        var tileHeight = periodHeight * Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));

        // Written so that a size that is not a number paints nothing
        if (!(tileWidth > 0 && tileHeight > 0) || !double.IsFinite(tileWidth) || !double.IsFinite(tileHeight) || transform.Determinant == 0)
            return null;

        // The pixels of the image of the tile and of its spacing, and the part of them the image holds: all of them, or,
        // when that image would be much larger than what the canvas shows, the part of the pattern the canvas shows
        var periodPixelsX = Math.Max(Math.Ceiling(tileWidth - (1.0 / 1024)), 1);
        var periodPixelsY = Math.Max(Math.Ceiling(tileHeight - (1.0 / 1024)), 1);
        var window = new PatternWindow(0, 0, periodPixelsX, periodPixelsY, 0, 0, 0, 0);
        if (IsPatternImageTooLarge(periodPixelsX, periodPixelsY))
        {
            if (_isProjective || !TryGetPatternWindow(brush, transform, periodPixelsX, periodPixelsY, out window) || IsPatternImageTooLarge(window.Width, window.Height))
            {
                // The image of the tile is drawn at a lower resolution, as large as the limits allow
                var factor = GetPatternReduction(periodPixelsX, periodPixelsY);
                periodPixelsX = Math.Max(Math.Floor(periodPixelsX * factor), 1);
                periodPixelsY = Math.Max(Math.Floor(periodPixelsY * factor), 1);
                window = new PatternWindow(0, 0, periodPixelsX, periodPixelsY, 0, 0, 0, 0);
            }
            else
            {
                // The image holds every tile the canvas shows along an axis where it is a window: nothing repeats
                wrapModeX = window.InstancesX > 0 && wrapModeX is ImageWrapMode.Repeat or ImageWrapMode.Reflect ? ImageWrapMode.Transparent : wrapModeX;
                wrapModeY = window.InstancesY > 0 && wrapModeY is ImageWrapMode.Repeat or ImageWrapMode.Reflect ? ImageWrapMode.Transparent : wrapModeY;
            }
        }

        var width = (int)window.Width;
        var height = (int)window.Height;
        var isWide = brush.TilePrecision == PatternTilePrecision.Canvas && _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24);
        var image = _patternImage;
        if (image is not null && image.PixelFormat != (isWide ? PixelFormat.Rgba64 : PixelFormat.Rgba32))
        {
            // The image of another precision cannot hold the tile
            _patternImage = null;
            _patternBrush = null;
            image.Dispose();
            image = null;
        }

        if (image is null || image.Width != width || image.Height != height || _patternWindow != window || !ReferenceEquals(_patternBrush, brush) || _patternIsAliased != _isAliased || _patternSpace != _compositingSpace || _patternTolerance != _curveTolerance || !ReferenceEquals(_patternPipeline, _paintPipeline))
        {
            if (_patternDepth >= MaxPatternDepth)
                throw new InvalidOperationException("A pattern brush is drawn inside too many pattern brushes: a pattern must not paint itself.");

            _cancellationToken.ThrowIfCancellationRequested();
            _patternBrush = null;
            if (image is null || image.Width != width || image.Height != height)
            {
                _patternImage = null;
                image?.Dispose();
                _image.Configuration.Limits.EnsureCanvasWithinLimits(width, height);
                image = isWide
                    ? new Image<Rgba64>(_image.Configuration, new Size(width, height), _image.Owner.Scope, layoutOptions: null)
                    : new Image<Rgba32>(_image.Configuration, new Size(width, height), _image.Owner.Scope, layoutOptions: null);
                _patternImage = image;
            }
            else
            {
                var pixels = image.Frames[0].GetStorage();
                for (var y = 0; y < height; y++)
                {
                    pixels.GetRowSpanCore(y)[..(width * pixels.BytesPerPixel)].Clear();
                }
            }

            DrawTile(brush, image, window, periodPixelsX, periodPixelsY, periodPixelsX / periodWidth, periodPixelsY / periodHeight);
            _patternBrush = brush;
            _patternWindow = window;
            _patternIsAliased = _isAliased;
            _patternSpace = _compositingSpace;
            _patternTolerance = _curveTolerance;
            _patternPipeline = _paintPipeline;
        }

        var unitX = periodWidth / periodPixelsX;
        var unitY = periodHeight / periodPixelsY;
        var toTile = new AffineTransform(unitX, 0, 0, unitY, tile.X + (window.Left * unitX), tile.Y + (window.Top * unitY));
        placement = _isProjective ? ((ProjectiveTransform)toTile.Then(brush.Transform)).Then(_projection) : toTile.Then(transform);
        return image.Frames[0];
    }

    /// <summary>Gets a value indicating whether the image of a tile would exceed the limits, or have more than four times the pixels of the bounds of the clip (and 65,536).</summary>
    private bool IsPatternImageTooLarge(double width, double height)
    {
        var limits = _image.Configuration.Limits;
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        var visible = Math.Max(right - left, 0) * (double)Math.Max(bottom - top, 0);
        return width > limits.MaxWidth || height > limits.MaxHeight || width * height > Math.Min(limits.MaxFramePixels, (4 * visible) + 65536);
    }

    /// <summary>Gets the factor that brings the image of a tile within the limits.</summary>
    private double GetPatternReduction(double width, double height)
    {
        var limits = _image.Configuration.Limits;
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        var visible = Math.Max(right - left, 0) * (double)Math.Max(bottom - top, 0);
        var pixels = Math.Min(limits.MaxFramePixels, (4 * visible) + 65536);
        return Math.Min(1, Math.Min(Math.Min(limits.MaxWidth / width, limits.MaxHeight / height), Math.Sqrt(pixels / (width * height))));
    }

    /// <summary>
    /// Gets the part of the pattern the canvas shows, in the pixels of the image of the tile (the tile at the origin
    /// covers <c>[0, W)</c>, the next one <c>[W, 2W)</c>): along each axis where it is narrower than a tile, the box of
    /// the image of the bounds of the clip with a margin of 2 pixels, and the whole tile along the other ones.
    /// </summary>
    /// <returns><see langword="false"/> when the canvas shows a whole tile along both axes.</returns>
    private bool TryGetPatternWindow(PatternBrush brush, AffineTransform transform, double periodPixelsX, double periodPixelsY, out PatternWindow window)
    {
        window = default;
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        var tile = brush.Tile;
        var toTile = new AffineTransform((tile.Width + brush.SpacingX) / periodPixelsX, 0, 0, (tile.Height + brush.SpacingY) / periodPixelsY, tile.X, tile.Y);
        if (!(right > left && bottom > top) || !toTile.Then(transform).TryInvert(out var toPixels))
            return false;

        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var corner = 0; corner < 4; corner++)
        {
            var point = toPixels.Transform(new PointD((corner & 1) == 0 ? left : right, (corner & 2) == 0 ? top : bottom));
            minX = Math.Min(minX, point.X);
            minY = Math.Min(minY, point.Y);
            maxX = Math.Max(maxX, point.X);
            maxY = Math.Max(maxY, point.Y);
        }

        var (startX, sizeX, instancesX) = GetPatternWindowAxis(Math.Floor(minX) - 2, Math.Ceiling(maxX) + 2, periodPixelsX, brush.WrapModeX);
        var (startY, sizeY, instancesY) = GetPatternWindowAxis(Math.Floor(minY) - 2, Math.Ceiling(maxY) + 2, periodPixelsY, brush.WrapModeY);
        if (instancesX == 0 && instancesY == 0)
            return false;

        window = new PatternWindow(startX, startY, sizeX, sizeY, instancesX, instancesY, startX - (Math.Floor(startX / periodPixelsX) * periodPixelsX), startY - (Math.Floor(startY / periodPixelsY) * periodPixelsY));
        return true;
    }

    /// <summary>Gets the part of the pixels of a pattern along one axis that the image holds, and how many tiles it shows of them (0 when it holds a whole tile, which repeats).</summary>
    private static (double Start, double Size, int Instances) GetPatternWindowAxis(double start, double end, double period, ImageWrapMode mode)
    {
        if (!(end - start < period))
            return (0, period, 0);

        if (mode is ImageWrapMode.Transparent or ImageWrapMode.Pad)
        {
            // The tile alone: the part of it the window shows, with its edge where the window goes beyond it
            start = Math.Clamp(start, 0, period - 1);
            end = Math.Clamp(end, start + 1, period);
            return (start, end - start, 1);
        }

        // One tile, or the end of one and the start of the next one
        var first = Math.Floor(start / period);
        var last = Math.Floor((end - 1) / period);
        return (start, end - start, (int)(last - first) + 1);
    }

    /// <summary>
    /// Runs the callback of a pattern on the image of its tile, or of the part of the pattern a window holds: once for
    /// each tile the window shows, moved there (and mirrored for the tiles a reflection turns), each one clipped to its
    /// tile.
    /// </summary>
    private void DrawTile(PatternBrush brush, Image image, PatternWindow window, double periodPixelsX, double periodPixelsY, double scaleX, double scaleY)
    {
        var frame = image.Frames[0];
        using var session = Begin(image, [frame], [frame.GetStorage()], _cancellationToken);
        session._patternDepth = _patternDepth + 1;

        // The tile is drawn as the shapes it paints are, and holds samples of the image that is drawn on: its
        // paints are converted to the color space of that image, and combined in its linear light
        session._colorImage = _colorImage ?? _image;
        session._imageTransfer = _imageTransfer;
        session._imageCurves = _imageCurves;
        session._paintPipeline = _paintPipeline;
        session._curveTolerance = _curveTolerance;
        session.RestoreCompositingSpace(_compositingSpace);
        if (_isAliased)
        {
            session.SetAntialiasing(enabled: false);
        }

        var tile = brush.Tile;
        var isWindow = window.InstancesX > 0 || window.InstancesY > 0;
        var firstX = window.Left - window.OffsetX;
        var firstY = window.Top - window.OffsetY;
        for (var j = 0; j < Math.Max(window.InstancesY, 1); j++)
        {
            for (var i = 0; i < Math.Max(window.InstancesX, 1); i++)
            {
                // The tile that starts at that pixel of the pattern, in the image
                var originX = window.InstancesX > 0 ? firstX + (i * periodPixelsX) : 0;
                var originY = window.InstancesY > 0 ? firstY + (j * periodPixelsY) : 0;
                var mirrorX = brush.WrapModeX == ImageWrapMode.Reflect && window.InstancesX > 0 && IsOddTile(originX, periodPixelsX);
                var mirrorY = brush.WrapModeY == ImageWrapMode.Reflect && window.InstancesY > 0 && IsOddTile(originY, periodPixelsY);
                var offsetX = (mirrorX ? originX + periodPixelsX + (tile.X * scaleX) : originX - (tile.X * scaleX)) - window.Left;
                var offsetY = (mirrorY ? originY + periodPixelsY + (tile.Y * scaleY) : originY - (tile.Y * scaleY)) - window.Top;
                session.PushTransform(new AffineTransform(mirrorX ? -scaleX : scaleX, 0, 0, mirrorY ? -scaleY : scaleY, offsetX, offsetY));
                var isClipped = isWindow || brush.SpacingX != 0 || brush.SpacingY != 0;
                if (isClipped)
                {
                    // The spacing stays transparent, and so do the other tiles: only the tile shows what the callback draws
                    session.PushClip(tile);
                }

                try
                {
                    brush.Content(new DrawingCanvas(session));
                }
                finally
                {
                    if (isClipped)
                    {
                        session.PopClip();
                    }

                    session.PopTransform();
                }
            }
        }

        static bool IsOddTile(double origin, double period) => Math.Abs(Math.Floor(origin / period) % 2) == 1;
    }

    /// <summary>
    /// The part of the pixels of a pattern an image of a tile holds, from the pixel <c>(Left, Top)</c> of the pattern
    /// (the tile at the origin covers <c>[0, W)</c>), how many tiles it shows along each axis (0 when it holds one whole
    /// tile, which repeats), and where it starts in its first tile.
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct PatternWindow(double Left, double Top, double Width, double Height, int InstancesX, int InstancesY, double OffsetX, double OffsetY);

    private void DisposePatterns()
    {
        _patternBrush = null;
        _patternImage?.Dispose();
        _patternImage = null;
    }
}
