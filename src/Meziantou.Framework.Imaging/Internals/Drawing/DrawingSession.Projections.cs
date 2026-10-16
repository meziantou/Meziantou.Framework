using Meziantou.Framework.Imaging.Drawing;

namespace Meziantou.Framework.Imaging.Internals;

/// <content>Images of the plane of the drawing: what is drawn in that plane before a projective transformation shows it in perspective.</content>
/// <remarks>
/// <para>
/// <b>Contract.</b> Under a projective transformation of the canvas, what has a size in the drawing that is not the size
/// of a shape (the blur and the offset of a shadow, a blur, the glyphs a font draws with its own colors) is drawn on an
/// image of a rectangle of the plane of the drawing, by a session of its own whose transformation is the scaling
/// <c>s</c> of the plane to that image: the factor of <see cref="GetLocalScale"/>, made smaller when the image would have
/// more than <see cref="MaxProjectedPixels"/> pixels, or more than 16 times the pixels of the bounds of the clip (and at
/// least 4,096). The rectangle is the part of the plane whose image the bounds of
/// the clip can show (cut where the plane is near its horizon, and brought within the largest coordinate), with a
/// margin for what is around it and casts a shadow on it, or only the box of a glyph. When the session ends, the image
/// is drawn on the canvas as an image placed by the projective transformation of its pixels, with
/// <see cref="ImageSampling.Smooth"/> and nothing around it.
/// </para>
/// <para>
/// Such an image is rented when the callback starts, or when the glyph is drawn: unlike the other draw calls, a text
/// with color glyphs under a projective transformation rents after it has changed pixels.
/// </para>
/// </remarks>
internal sealed partial class DrawingSession
{
    /// <summary>The largest number of pixels of an image of the plane of the drawing.</summary>
    private const double MaxProjectedPixels = 1 << 22;

    /// <summary>Gets or sets the width the canvas of the session reports: the one of the canvas it draws for.</summary>
    public int CanvasWidth { get; private set; }

    /// <summary>Gets or sets the height the canvas of the session reports.</summary>
    public int CanvasHeight { get; private set; }

    /// <summary>Starts a session that draws on an image of the plane of the drawing, which is drawn in perspective when it ends.</summary>
    /// <param name="content">The rectangle of the plane that is drawn on, or <see langword="null"/> for the part of the plane the clip can show.</param>
    /// <param name="margin">The distance around the part of the plane the clip can show that is drawn on too.</param>
    /// <exception cref="ImageResourceLimitException">The image exceeds the limits of the image that is drawn on.</exception>
    [SuppressMessage("Reliability", "CA2000:Dispose objects before losing scope", Justification = "The session and the image are owned by the scope, released by EndProjectedScope or ProjectedScope.Release.")]
    private ProjectedScope BeginProjectedScope(RectangleD? content, double margin)
    {
        _cancellationToken.ThrowIfCancellationRequested();
        var bounds = GetVisiblePlaneBounds();
        if (!bounds.IsEmpty)
        {
            bounds = Limit(RectangleD.FromLTRB(bounds.Left - margin, bounds.Top - margin, bounds.Right + margin, bounds.Bottom + margin));
        }

        if (content is { } box)
        {
            bounds = RectangleD.Intersect(bounds, box);
        }

        var scale = _transformScale;
        var width = 1;
        var height = 1;
        var isEmpty = !(bounds.Width > 0 && bounds.Height > 0);
        if (!isEmpty)
        {
            // At most 16 times the pixels the clip shows: the image is drawn on them
            GetClipBounds(out var clipLeft, out var clipTop, out var clipRight, out var clipBottom, out _);
            var visible = Math.Max(clipRight - clipLeft, 1) * (double)Math.Max(clipBottom - clipTop, 1);
            var limit = Math.Min(MaxProjectedPixels, Math.Max(16 * visible, 4096));
            var pixels = Math.Ceiling(bounds.Width * scale) * Math.Ceiling(bounds.Height * scale);
            if (pixels > limit)
            {
                scale *= Math.Sqrt(limit / pixels);
            }

            width = (int)Math.Max(1, Math.Ceiling(bounds.Width * scale));
            height = (int)Math.Max(1, Math.Ceiling(bounds.Height * scale));
        }

        var isWide = _frames[0].PixelFormat is not (PixelFormat.Rgba32 or PixelFormat.Bgra32 or PixelFormat.Rgb24);
        _image.Configuration.Limits.EnsureCanvasWithinLimits(width, height);
        Image image = isWide
            ? new Image<Rgba64>(_image.Configuration, new Size(width, height), _image.Owner.Scope, layoutOptions: null)
            : new Image<Rgba32>(_image.Configuration, new Size(width, height), _image.Owner.Scope, layoutOptions: null);
        DrawingSession? session = null;
        try
        {
            var frame = image.Frames[0];
            session = Begin(image, [frame], [frame.GetStorage()], _cancellationToken);

            // The image is drawn as the canvas is, and holds samples of the image that is drawn on: its paints are
            // converted to the color space of that image, and combined in its linear light
            session._patternDepth = _patternDepth;
            session._colorImage = _colorImage ?? _image;
            session._imageTransfer = _imageTransfer;
            session._imageCurves = _imageCurves;
            session._paintPipeline = _paintPipeline;
            session._clipsGeometry = true;
            session.CanvasWidth = CanvasWidth;
            session.CanvasHeight = CanvasHeight;
            session.RestoreCompositingSpace(_compositingSpace);
            if (_isAliased)
            {
                session.SetAntialiasing(enabled: false);
            }

            var toImage = new AffineTransform(scale, 0, 0, scale, -bounds.Left * scale, -bounds.Top * scale);
            if (!isEmpty)
            {
                session.PushTransform(toImage);
            }

            var toPlane = new AffineTransform(1 / scale, 0, 0, 1 / scale, bounds.Left, bounds.Top);
            return new ProjectedScope(image, session, isEmpty ? default : ((ProjectiveTransform)toPlane).Then(GetProjection()), isEmpty);
        }
        catch
        {
            session?.Dispose();
            image.Dispose();
            throw;
        }
    }

    /// <summary>Ends a session on an image of the plane of the drawing, and draws the image in perspective.</summary>
    /// <param name="scope">The session.</param>
    /// <param name="draw"><see langword="false"/> to discard the image: the drawing that was made on it failed.</param>
    /// <param name="mode">The operator that combines the image with the canvas.</param>
    /// <param name="blend">The blend mode that mixes the colors of the image with those of the canvas.</param>
    private void EndProjectedScope(ProjectedScope scope, bool draw, CompositeMode mode, BlendMode blend)
    {
        try
        {
            // The pixels of the image are released before they are read
            scope.Session.Dispose();
            if (draw && !scope.IsEmpty)
            {
                DrawProjectedImage(scope.Image.Frames[0], scope.ToCanvas, ImageWrapMode.Transparent, ImageSampling.Smooth, CoverageRasterizer.FullCoverage, mode, blend);
            }
        }
        finally
        {
            scope.Image.Dispose();
        }
    }

    /// <summary>Starts a layer drawn on an image of the plane of the drawing: <see cref="PopLayer"/> draws it in perspective.</summary>
    /// <returns>The session the drawings of the layer are made by.</returns>
    private DrawingSession PushProjectedLayer(double margin)
    {
        var scope = BeginProjectedScope(content: null, margin);
        if (_layerDepth == _layers.Count)
        {
            _layers.Add(new LayerLevel());
        }

        _layers[_layerDepth].Projected = scope;
        _layerDepth++;
        return scope.Session;
    }

    /// <summary>Ends the layer of an image of the plane of the drawing that was started last, without drawing it: its own layer did not start.</summary>
    private void DiscardProjectedLayer()
    {
        var level = _layers[--_layerDepth];
        var scope = level.Projected!;
        level.Projected = null;
        scope.Release();
    }

    /// <summary>Gets the rectangle of the plane of the drawing whose image is in the bounds of the clip.</summary>
    private RectangleD GetVisiblePlaneBounds()
    {
        GetClipBounds(out var left, out var top, out var right, out var bottom, out _);
        if (right <= left || bottom <= top || !GetProjection().TryInvert(out var inverse))
            return RectangleD.Empty;

        // The corners of the bounds, cut where W is too small: that part of the canvas shows the plane far beyond
        // what is near the viewer, and the image of the plane would be unbounded
        Span<double> corners = [left, top, right, top, right, bottom, left, bottom];
        var largest = 0d;
        for (var i = 0; i < 8; i += 2)
        {
            largest = Math.Max(largest, Math.Abs(inverse.TransformW(corners[i], corners[i + 1])));
        }

        var threshold = largest / 1048576;
        var minX = double.PositiveInfinity;
        var minY = double.PositiveInfinity;
        var maxX = double.NegativeInfinity;
        var maxY = double.NegativeInfinity;
        for (var i = 0; i < 8; i += 2)
        {
            var ax = corners[i];
            var ay = corners[i + 1];
            var bx = corners[(i + 2) % 8];
            var by = corners[(i + 3) % 8];
            var wa = inverse.TransformW(ax, ay) - threshold;
            var wb = inverse.TransformW(bx, by) - threshold;
            if (wa >= 0)
            {
                Add(ax, ay);
            }

            if ((wa >= 0) != (wb >= 0))
            {
                var t = wa / (wa - wb);
                Add(ax + (t * (bx - ax)), ay + (t * (by - ay)));
            }
        }

        if (!(minX <= maxX && minY <= maxY))
            return RectangleD.Empty;

        return Limit(RectangleD.FromLTRB(minX, minY, maxX, maxY));

        void Add(double x, double y)
        {
            var w = inverse.TransformW(x, y);
            var px = inverse.TransformX(x, y) / w;
            var py = inverse.TransformY(x, y) / w;
            if (double.IsFinite(px) && double.IsFinite(py))
            {
                minX = Math.Min(minX, px);
                minY = Math.Min(minY, py);
                maxX = Math.Max(maxX, px);
                maxY = Math.Max(maxY, py);
            }
        }
    }

    /// <summary>Brings a rectangle within the largest coordinate.</summary>
    private static RectangleD Limit(RectangleD bounds)
    {
        var left = Math.Max(bounds.Left, -DrawingGuard.MaxCoordinate);
        var top = Math.Max(bounds.Top, -DrawingGuard.MaxCoordinate);
        var right = Math.Min(bounds.Right, DrawingGuard.MaxCoordinate);
        var bottom = Math.Min(bounds.Bottom, DrawingGuard.MaxCoordinate);
        return right > left && bottom > top ? RectangleD.FromLTRB(left, top, right, bottom) : RectangleD.Empty;
    }

    /// <summary>A session that draws on an image of the plane of the drawing, and the transformation that places its pixels on the canvas.</summary>
    private sealed class ProjectedScope(Image image, DrawingSession session, ProjectiveTransform toCanvas, bool isEmpty)
    {
        public Image Image { get; } = image;

        public DrawingSession Session { get; } = session;

        public ProjectiveTransform ToCanvas { get; } = toCanvas;

        /// <summary>Gets a value indicating whether the image shows nothing of the canvas: it is not drawn.</summary>
        public bool IsEmpty { get; } = isEmpty;

        /// <summary>Ends the session and releases the image, without drawing it.</summary>
        public void Release()
        {
            Session.Dispose();
            Image.Dispose();
        }
    }
}
