using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Patterns: the tile of a <see cref="PatternBrush"/>, drawn on an image of its own and repeated as an image is.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> With <c>T</c> the transformation that places the pattern on the canvas (the one of the brush, then
/// the one of the canvas), a tile of <c>tw</c> by <c>th</c> has, on the canvas, the width
/// <c>w = tw sqrt(T.M11^2 + T.M12^2)</c> and the height <c>h = th sqrt(T.M21^2 + T.M22^2)</c>. Its image has
/// <c>W = max(1, ceil(w - 1/1024))</c> columns and <c>H</c> rows computed the same way, and is transparent before the
/// callback draws on it, through a canvas of its own whose transformation maps the tile to the image
/// (a scaling by <c>W / tw</c> and <c>H / th</c>). The shape is then painted with that image, placed by the scaling
/// that maps it back to the tile, then by <c>T</c>, with <see cref="ImageWrapMode.Repeat"/> and
/// <see cref="ImageSampling.Bilinear"/>.
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
    private bool _patternIsLinear;

    /// <summary>Draws the tile of a pattern, unless it is the one that was drawn last, and gets the transformation that places its image on the canvas.</summary>
    /// <returns>The frame that holds the tile, or <see langword="null"/> when the pattern paints nothing.</returns>
    /// <exception cref="InvalidOperationException">The pattern is drawn inside too many patterns.</exception>
    /// <exception cref="ImageResourceLimitException">The image of the tile exceeds the limits of the image that is drawn on.</exception>
    private ImageFrame? PreparePattern(PatternBrush brush, out AffineTransform placement)
    {
        placement = default;
        var transform = ToCanvas(brush.Transform);
        var tile = brush.Tile;
        var tileWidth = tile.Width * Math.Sqrt((transform.M11 * transform.M11) + (transform.M12 * transform.M12));
        var tileHeight = tile.Height * Math.Sqrt((transform.M21 * transform.M21) + (transform.M22 * transform.M22));

        // Written so that a size that is not a number paints nothing
        if (!(tileWidth > 0 && tileHeight > 0) || !double.IsFinite(tileWidth) || !double.IsFinite(tileHeight) || transform.Determinant == 0)
            return null;

        // Beyond the largest coordinate, the image is refused by the limits of the image whatever its exact size
        var width = (int)Math.Min(Math.Max(Math.Ceiling(tileWidth - (1.0 / 1024)), 1), DrawingGuard.MaxCoordinate);
        var height = (int)Math.Min(Math.Max(Math.Ceiling(tileHeight - (1.0 / 1024)), 1), DrawingGuard.MaxCoordinate);
        var isWide = _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24);
        var image = _patternImage;
        if (image is null || image.Width != width || image.Height != height || !ReferenceEquals(_patternBrush, brush) || _patternIsAliased != _isAliased || _patternIsLinear != _isLinear)
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

            DrawTile(brush, image, width / tile.Width, height / tile.Height);
            _patternBrush = brush;
            _patternIsAliased = _isAliased;
            _patternIsLinear = _isLinear;
        }

        placement = new AffineTransform(tile.Width / width, 0, 0, tile.Height / height, tile.X, tile.Y).Then(transform);
        return image.Frames[0];
    }

    /// <summary>Runs the callback of a pattern on the image of its tile.</summary>
    private void DrawTile(PatternBrush brush, Image image, double scaleX, double scaleY)
    {
        var frame = image.Frames[0];
        using var session = Begin(image, [frame], [frame.GetStorage()], _cancellationToken);
        session._patternDepth = _patternDepth + 1;

        // The tile is drawn as the shapes it paints are
        session._compositingSpace = _compositingSpace;
        session._decodesSamples = _decodesSamples;
        session._isLinear = _isLinear;
        if (_isAliased)
        {
            session.SetAntialiasing(enabled: false);
        }

        var tile = brush.Tile;
        session.PushTransform(new AffineTransform(scaleX, 0, 0, scaleY, -tile.X * scaleX, -tile.Y * scaleY));
        brush.Content(new DrawingCanvas(session));
    }

    private void DisposePatterns()
    {
        _patternBrush = null;
        _patternImage?.Dispose();
        _patternImage = null;
    }
}
