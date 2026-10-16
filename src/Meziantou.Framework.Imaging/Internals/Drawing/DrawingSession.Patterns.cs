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
/// <c>H / (th + sy)</c>), with a clip of the tile when there is a spacing. The shape is then painted with that image,
/// placed by the scaling that maps it back, then by <c>T</c>, with the wrap modes of the brush and
/// <see cref="ImageSampling.Bilinear"/>. Under a projective transformation of the canvas, <c>T</c> is the transformation
/// of the brush followed by the scaling by the factor of the canvas where <c>W</c> is the one of the origin (see
/// <see cref="GetLocalScale"/>), and the image is placed by the scaling, the transformation of the brush, then the
/// projective one.
/// </para>
/// <para>
/// The image of the last tile is kept until the session ends: the shapes that follow with the same brush at the same
/// size, with the same antialiasing and in the same compositing space (the tile is drawn as the shapes are), do not
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
    private bool _patternIsAliased;
    private CompositingSpace _patternSpace;
    private IccPipeline? _patternPipeline;

    /// <summary>Draws the tile of a pattern, unless it is the one that was drawn last, and gets the transformation that places its image on the canvas.</summary>
    /// <returns>The frame that holds the tile, or <see langword="null"/> when the pattern paints nothing.</returns>
    /// <exception cref="InvalidOperationException">The pattern is drawn inside too many patterns.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the tile exceeds the limits of the image that is drawn on.</exception>
    private ImageFrame? PreparePattern(PatternBrush brush, out ProjectiveTransform placement)
    {
        placement = default;

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

        // Beyond the largest coordinate, the image is refused by the limits of the image whatever its exact size
        var width = (int)Math.Min(Math.Max(Math.Ceiling(tileWidth - (1.0 / 1024)), 1), DrawingGuard.MaxCoordinate);
        var height = (int)Math.Min(Math.Max(Math.Ceiling(tileHeight - (1.0 / 1024)), 1), DrawingGuard.MaxCoordinate);
        var isWide = _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24);
        var image = _patternImage;
        if (image is null || image.Width != width || image.Height != height || !ReferenceEquals(_patternBrush, brush) || _patternIsAliased != _isAliased || _patternSpace != _compositingSpace || !ReferenceEquals(_patternPipeline, _paintPipeline))
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

            DrawTile(brush, image, width / periodWidth, height / periodHeight);
            _patternBrush = brush;
            _patternIsAliased = _isAliased;
            _patternSpace = _compositingSpace;
            _patternPipeline = _paintPipeline;
        }

        var toTile = new AffineTransform(periodWidth / width, 0, 0, periodHeight / height, tile.X, tile.Y);
        placement = _isProjective ? ((ProjectiveTransform)toTile.Then(brush.Transform)).Then(_projection) : toTile.Then(transform);
        return image.Frames[0];
    }

    /// <summary>Runs the callback of a pattern on the image of its tile.</summary>
    private void DrawTile(PatternBrush brush, Image image, double scaleX, double scaleY)
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
        session.RestoreCompositingSpace(_compositingSpace);
        if (_isAliased)
        {
            session.SetAntialiasing(enabled: false);
        }

        var tile = brush.Tile;
        session.PushTransform(new AffineTransform(scaleX, 0, 0, scaleY, -tile.X * scaleX, -tile.Y * scaleY));
        if (brush.SpacingX != 0 || brush.SpacingY != 0)
        {
            // The spacing stays transparent: only the tile shows what the callback draws
            session.PushClip(tile);
        }

        brush.Content(new DrawingCanvas(session));
    }

    private void DisposePatterns()
    {
        _patternBrush = null;
        _patternImage?.Dispose();
        _patternImage = null;
    }
}
