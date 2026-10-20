using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

// The images of the 2D contexts (https://html.spec.whatwg.org/multipage/canvas.html#drawing-images) and the pixel
// manipulation (https://html.spec.whatwg.org/multipage/canvas.html#pixel-manipulation)
public abstract partial class CanvasRenderingContext2DBase
{
    /// <summary>
    /// An image source that can be drawn: its image, its size in image pixels, whether it is origin-clean, and the document of an
    /// SVG image (drawn as vectors at its concrete object size), or null.
    /// </summary>
    private readonly record struct UsableImage(PaintImage Image, double Width, double Height, bool IsOriginClean, SvgImage? Svg = null);

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasDrawImage (https://html.spec.whatwg.org/multipage/canvas.html#drawing-images)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Draws an image at its natural size (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage).</summary>
    /// <param name="image">An <c>img</c>, <c>svg:image</c>, <c>video</c> or <c>canvas</c> element, an <see cref="ImageBitmap"/>, an <see cref="OffscreenCanvas"/> or a <see cref="Css.TypedOm.CssImageValue"/> (the image of a URL fetched for the styles of the document).</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is broken, or a canvas or a bitmap without pixels.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage"/>
    [IdlMember("drawImage", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage")]
    public void DrawImage(object? image, double dx, double dy)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || GetUsableImage(image) is not { } source)
            return;

        DrawImageCore(source, 0, 0, source.Width, source.Height, dx, dy, source.Width, source.Height);
    }

    /// <summary>Draws an image scaled to a rectangle (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage).</summary>
    /// <param name="image">An <c>img</c>, <c>svg:image</c>, <c>video</c> or <c>canvas</c> element, an <see cref="ImageBitmap"/>, an <see cref="OffscreenCanvas"/> or a <see cref="Css.TypedOm.CssImageValue"/> (the image of a URL fetched for the styles of the document).</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="dw">The width of the destination.</param>
    /// <param name="dh">The height of the destination.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is broken, or a canvas or a bitmap without pixels.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage"/>
    [IdlMember("drawImage", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage")]
    public void DrawImage(object? image, double dx, double dy, double dw, double dh)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dw) || !double.IsFinite(dh) || GetUsableImage(image) is not { } source)
            return;

        DrawImageCore(source, 0, 0, source.Width, source.Height, dx, dy, dw, dh);
    }

    /// <summary>Draws a rectangle of an image scaled to a rectangle (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage).</summary>
    /// <param name="image">An <c>img</c>, <c>svg:image</c>, <c>video</c> or <c>canvas</c> element, an <see cref="ImageBitmap"/>, an <see cref="OffscreenCanvas"/> or a <see cref="Css.TypedOm.CssImageValue"/> (the image of a URL fetched for the styles of the document).</param>
    /// <param name="sx">The x coordinate of the source rectangle, in image pixels.</param>
    /// <param name="sy">The y coordinate of the source rectangle, in image pixels.</param>
    /// <param name="sw">The width of the source rectangle.</param>
    /// <param name="sh">The height of the source rectangle.</param>
    /// <param name="dx">The x coordinate of the destination.</param>
    /// <param name="dy">The y coordinate of the destination.</param>
    /// <param name="dw">The width of the destination.</param>
    /// <param name="dh">The height of the destination.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is broken, or a canvas or a bitmap without pixels.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage"/>
    [IdlMember("drawImage", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage")]
    public void DrawImage(object? image, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        ArgumentNullException.ThrowIfNull(image);
        if (!double.IsFinite(sx) || !double.IsFinite(sy) || !double.IsFinite(sw) || !double.IsFinite(sh)
            || !double.IsFinite(dx) || !double.IsFinite(dy) || !double.IsFinite(dw) || !double.IsFinite(dh)
            || GetUsableImage(image) is not { } source)
        {
            return;
        }

        DrawImageCore(source, sx, sy, sw, sh, dx, dy, dw, dh);
    }

    private void DrawImageCore(UsableImage source, double sx, double sy, double sw, double sh, double dx, double dy, double dw, double dh)
    {
        // https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage: the image is drawn even when nothing is
        // painted, so a source of another origin taints the canvas
        if (!source.IsOriginClean)
        {
            Bitmap.IsOriginClean = false;
        }

        // 5. A source or destination rectangle of zero width or height draws nothing
        if (sw == 0 || sh == 0 || dw == 0 || dh == 0)
            return;

        // The rectangles with a negative width or height are normalized
        var sourceRect = NormalizeRect(sx, sy, sw, sh);
        var destinationRect = NormalizeRect(dx, dy, dw, dh);

        // "When the source rectangle is outside the source image, the source rectangle must be clipped to the source image and
        // the destination rectangle must be clipped in the same proportion"
        var scaleX = destinationRect.Width / sourceRect.Width;
        var scaleY = destinationRect.Height / sourceRect.Height;
        var clippedLeft = Math.Max(sourceRect.Left, 0);
        var clippedTop = Math.Max(sourceRect.Top, 0);
        var clippedRight = Math.Min(sourceRect.Right, source.Width);
        var clippedBottom = Math.Min(sourceRect.Bottom, source.Height);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
            return;

        destinationRect = new PhysicalRect(
            destinationRect.X + ((clippedLeft - sourceRect.Left) * scaleX),
            destinationRect.Y + ((clippedTop - sourceRect.Top) * scaleY),
            (clippedRight - clippedLeft) * scaleX,
            (clippedBottom - clippedTop) * scaleY);
        sourceRect = new PhysicalRect(clippedLeft, clippedTop, clippedRight - clippedLeft, clippedBottom - clippedTop);

        if (source.Svg is { } svg)
        {
            DrawSvgImage(svg, source.Width, source.Height, sourceRect, destinationRect);
            return;
        }

        // The image is painted with the global alpha, sampled per the image smoothing settings
        var paint = new DisplayPaint(new DisplayColor(0, 0, 0, (float)_state.GlobalAlpha));
        DrawInUserSpace(new DrawImageItem(source.Image, sourceRect, destinationRect, ImageSampling, paint));
    }

    /// <summary>
    /// Draws an SVG image (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage): its document laid out at
    /// its concrete object size is painted as vectors, mapped from the source rectangle to the destination rectangle and clipped
    /// to the destination rectangle, with the global alpha.
    /// </summary>
    private void DrawSvgImage(SvgImage svg, double width, double height, PhysicalRect sourceRect, PhysicalRect destinationRect)
    {
        if (Invert(_state.Transform) is null || svg.GetDisplayList(new PhysicalSize(width, height)) is not { } list)
            return;

        var scaleX = destinationRect.Width / sourceRect.Width;
        var scaleY = destinationRect.Height / sourceRect.Height;
        var alpha = _state.GlobalAlpha;
        DrawShape(builder =>
        {
            builder.Save();
            builder.Transform(_state.Transform);
            builder.Add(new ClipRectItem(destinationRect));
            builder.Transform(DisplayMatrix.Translation(destinationRect.X - (sourceRect.X * scaleX), destinationRect.Y - (sourceRect.Y * scaleY)));
            builder.Transform(DisplayMatrix.Scale(scaleX, scaleY));
            if (alpha < 1)
            {
                builder.SaveLayer(new SaveLayerItem(alpha, BlendMode.Normal, Filters: null));
            }

            builder.AddList(list);
            builder.Restore();
        });
    }

    /// <summary>
    /// Returns the image of an SVG image source (https://html.spec.whatwg.org/multipage/canvas.html#check-the-usability-of-the-image-argument):
    /// bad (null) when it cannot be parsed or a natural dimension is zero; its size is its concrete object size, whose default
    /// object size is the size of the output bitmap (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage).
    /// </summary>
    private UsableImage? GetSvgUsableImage(PaintImage image, Document host, bool corsSameOrigin)
    {
        if (SvgImage.Get(image, host) is not { } svg || svg.NaturalSize.Width is 0 || svg.NaturalSize.Height is 0)
            return null;

        var (width, height) = svg.NaturalSize.GetConcreteSize(Bitmap.Width, Bitmap.Height);
        return width > 0 && height > 0 ? new UsableImage(image, width, height, corsSameOrigin, svg) : null;
    }

    /// <summary>
    /// Checks the usability of an image argument (https://html.spec.whatwg.org/multipage/canvas.html#check-the-usability-of-the-image-argument):
    /// returns the image to draw, or null when nothing can be drawn (an image that is not fully decodable, a video without frame).
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is broken, or a canvas or a bitmap without pixels.</exception>
    private UsableImage? GetUsableImage(object image)
    {
        var backend = Bitmap.Backend;
        switch (image)
        {
            case HtmlImageElement img:
            {
                if (img.IsImageBroken)
                    throw new DomException("The image is broken", "InvalidStateError");

                // An image whose source changed is not fully decodable until it is loaded (as in the browsers)
                if (img.IsImageUpdatePending || img.GetBitmapSource() is not var (paintImage, _, corsSameOrigin) || paintImage is null)
                    return null;

                if (paintImage.IsSvg)
                    return GetSvgUsableImage(paintImage, img.NodeDocument, corsSameOrigin);

                return GetRasterUsableImage(img, paintImage, corsSameOrigin, backend);
            }

            case HtmlCanvasElement canvas:
            {
                if (canvas.Width == 0 || canvas.Height == 0)
                    throw new DomException("The canvas has no pixels", "InvalidStateError");

                // A placeholder canvas draws the last frame committed by its OffscreenCanvas
                if (canvas.PlaceholderFrame is { Image: { } frame } placeholder)
                    return new UsableImage(frame, placeholder.Pixels.Width, placeholder.Pixels.Height, placeholder.Pixels.IsOriginClean);

                return GetCanvasImage(canvas.RenderingContext, (int)canvas.Width, (int)canvas.Height, backend);
            }

            case OffscreenCanvas offscreen:
            {
                if (offscreen.IsDetached)
                    throw new DomException("The OffscreenCanvas is detached", "InvalidStateError");

                if (offscreen.Width == 0 || offscreen.Height == 0)
                    throw new DomException("The OffscreenCanvas has no pixels", "InvalidStateError");

                return GetCanvasImage(offscreen.RenderingContext, (int)offscreen.Width, (int)offscreen.Height, backend);
            }

            case ImageBitmap bitmap:
            {
                if (bitmap.Bitmap is not { } pixels)
                    throw new DomException("The image bitmap is detached", "InvalidStateError");

                return pixels.GetPaintImage(backend) is { } bitmapImage ? new UsableImage(bitmapImage, pixels.Width, pixels.Height, pixels.IsOriginClean) : null;
            }

            case HtmlVideoElement video:
                // The frame of the video at the current playback position; nothing is drawn while the ready state is
                // HAVE_NOTHING or HAVE_METADATA, or when the media backend gives no frame
                return video.GetVideoFrameImage() is { } frameImage ? new UsableImage(frameImage, (int)video.VideoWidth, (int)video.VideoHeight, video.IsMediaDataCorsSameOrigin) : null;

            case SvgImageElement svgImage:
            {
                // The SVG image elements are used as the img elements: their image, once it is loaded
                if (svgImage.IsImageBroken)
                    throw new DomException("The image is broken", "InvalidStateError");

                if (svgImage.Image is not { } paintImage)
                    return null;

                if (paintImage.IsSvg)
                    return GetSvgUsableImage(paintImage, svgImage.NodeDocument, paintImage.IsCorsSameOrigin);

                return GetRasterUsableImage(svgImage, paintImage, paintImage.IsCorsSameOrigin, backend);
            }

            case Css.TypedOm.CssImageValue cssImage:
            {
                // https://drafts.css-houdini.org/css-paint-api-1/#drawing-a-cssimagevalue: the underlying image of the value, i.e.
                // the image fetched for the computed values of the document (e.g. an input property of a paint class); nothing
                // is drawn for the other images (a gradient, an image that is not loaded)
                var document = Global?.EnvironmentDocument;
                var values = Css.Values.ValueMatcher.RemoveWhitespace(Css.Syntax.CssParser.ParseListOfComponentValues(cssImage.ToString()));
                if (document?.CssPaintImages is not { } images || values is not [var value] || !Css.Values.CssUrlModifiers.TryGetUrl(value, out var address, out _)
                    || Urls.UrlParser.Parse(address, document.DocumentBaseUrl) is not { } parsed || !images.TryGetValue(parsed.Serialize(), out var paintImage))
                {
                    return null;
                }

                if (paintImage.IsSvg)
                    return GetSvgUsableImage(paintImage, document, paintImage.IsCorsSameOrigin);

                return paintImage.GetDecodedImage(backend) is { } decoded ? new UsableImage(paintImage, decoded.Width, decoded.Height, paintImage.IsCorsSameOrigin) : null;
            }

            default:
                throw new TypeErrorException("The value is not an image source");
        }
    }

    /// <summary>Returns a raster image of an <c>img</c> or SVG <c>image</c> element, or null when it cannot be decoded.</summary>
    private static UsableImage? GetRasterUsableImage(Element element, PaintImage image, bool corsSameOrigin, RenderingBackend backend)
    {
        // https://drafts.csswg.org/css-images-3/#the-image-orientation: the EXIF orientation of the image, unless the element has image-orientation: none
        var fromImage = !element.IsConnected || element.NodeDocument.GetStyleEngine().GetStyle(element).Layout.Replaced.ImageOrientation.FromImage;
        image = image.WithOrientation(fromImage);
        if (image.GetDecodedImage(backend) is not { } decoded)
            return null;

        // The size of the image is its natural size (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-drawimage:
        // "the source image's natural dimensions"): the density-corrected dimensions of an image whose EXIF resolution gives them
        return image is { Width: > 0, Height: > 0 } ? new UsableImage(image, image.Width, image.Height, corsSameOrigin) : new UsableImage(image, decoded.Width, decoded.Height, corsSameOrigin);
    }

    /// <summary>Returns the image of the bitmap of a canvas: the snapshot of its 2D context, the output bitmap of its bitmap renderer, or a transparent image.</summary>
    private static UsableImage? GetCanvasImage(object? context, int width, int height, RenderingBackend backend)
    {
        switch (context)
        {
            case CanvasRenderingContext2DBase context2D:
                return context2D.Bitmap.GetSnapshot() is { } snapshot ? new UsableImage(snapshot, width, height, context2D.Bitmap.IsOriginClean) : null;

            case ImageBitmapRenderingContext bitmapRenderer:
            {
                var pixels = bitmapRenderer.GetBitmap();
                return pixels.GetPaintImage(backend) is { } image ? new UsableImage(image, pixels.Width, pixels.Height, pixels.IsOriginClean) : null;
            }

            default:
                // A canvas without context is transparent black (drawing it clears the destination with the operators that are not source-over)
                var transparent = BitmapPixels.CreateTransparent(width, height);
                return transparent.GetPaintImage(backend) is { } image2 ? new UsableImage(image2, width, height, IsOriginClean: true) : null;
        }
    }

    /// <summary>
    /// Creates a pattern of an image (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createpattern), or returns
    /// null when the image cannot be drawn (not fully decodable).
    /// </summary>
    /// <param name="image">An <c>img</c>, <c>svg:image</c>, <c>video</c> or <c>canvas</c> element, an <see cref="ImageBitmap"/>, an <see cref="OffscreenCanvas"/> or a <see cref="Css.TypedOm.CssImageValue"/> (the image of a URL fetched for the styles of the document).</param>
    /// <param name="repetition"><c>repeat</c> (or the empty string), <c>repeat-x</c>, <c>repeat-y</c> or <c>no-repeat</c>.</param>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the image is broken, or a canvas or a bitmap without pixels; a <c>SyntaxError</c> when the repetition is invalid.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createpattern"/>
    [IdlMember("createPattern", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createpattern")]
    public CanvasPattern? CreatePattern(object? image, string repetition)
    {
        ArgumentNullException.ThrowIfNull(image);
        ArgumentNullException.ThrowIfNull(repetition);

        // 1-2. The usability of the image
        if (GetUsableImage(image) is not { } source)
            return null;

        // 3-4. The repetition behavior
        if (!CanvasPattern.IsValidRepetition(repetition))
            throw new DomException("The repetition is invalid", "SyntaxError");

        // An SVG image is rasterized at its concrete object size
        var patternImage = source.Image;
        if (source.Svg is { } svg)
        {
            var width = (int)Math.Ceiling(source.Width);
            var height = (int)Math.Ceiling(source.Height);
            if ((long)width * height > BitmapPixels.MaxPixelCount || svg.Rasterize(Bitmap.Backend, width, height) is not { } rendered)
                return null;

            patternImage = new PaintImage(patternImage.Url, Bitmap.Backend, rendered);
        }

        return new CanvasPattern(patternImage, repetition, source.IsOriginClean);
    }

    // ----------------------------------------------------------------------------------------------------------------
    // CanvasImageData (https://html.spec.whatwg.org/multipage/canvas.html#pixel-manipulation)
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>Creates a transparent black image data (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata).</summary>
    /// <param name="sw">The width (its absolute value is used).</param>
    /// <param name="sh">The height (its absolute value is used).</param>
    /// <param name="settings">The color space (the color space of the canvas by default) and the format of the pixels.</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the width or the height is zero.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata"/>
    [IdlMember("createImageData", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata")]
    public ImageData CreateImageData(int sw, int sh, ImageDataSettings? settings = null)
    {
        if (sw == 0 || sh == 0)
            throw new DomException("The width and the height must not be zero", "IndexSizeError");

        return new ImageData((uint)Math.Abs((long)sw), (uint)Math.Abs((long)sh), new ImageDataSettings { ColorSpace = settings?.ColorSpace ?? Settings.ColorSpace, PixelFormat = settings?.PixelFormat ?? ImageDataPixelFormat.RgbaUnorm8 });
    }

    /// <summary>Creates a transparent black image data of the size and color space of another one (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata-imagedata).</summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata-imagedata"/>
    [IdlMember("createImageData", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createimagedata-imagedata")]
    public ImageData CreateImageData(ImageData imageData)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        return new ImageData(imageData.Width, imageData.Height, new ImageDataSettings { ColorSpace = imageData.ColorSpace });
    }

    /// <summary>
    /// Returns the pixels of a rectangle of the bitmap (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getimagedata):
    /// not premultiplied, in the requested color space; the pixels outside of the bitmap are transparent black.
    /// </summary>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the width or the height is zero, a <c>SecurityError</c> when the canvas is not origin-clean.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getimagedata"/>
    [IdlMember("getImageData", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-getimagedata")]
    public ImageData GetImageData(int sx, int sy, int sw, int sh, ImageDataSettings? settings = null)
    {
        // 1-2. Zero sizes, and a canvas that is not origin-clean
        if (sw == 0 || sh == 0)
            throw new DomException("The width and the height must not be zero", "IndexSizeError");

        if (!Bitmap.IsOriginClean)
            throw new DomException("The canvas has been tainted by cross-origin data", "SecurityError");

        // 3. The image data (a negative width or height extends the rectangle to the left or to the top)
        long x = sx, y = sy, width = sw, height = sh;
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        var colorSpace = settings?.ColorSpace ?? Settings.ColorSpace;
        var format = settings?.PixelFormat ?? ImageDataPixelFormat.RgbaUnorm8;
        var imageData = new ImageData((uint)width, (uint)height, new ImageDataSettings { ColorSpace = colorSpace, PixelFormat = format });

        // 4-5. The pixels of the rectangle, converted from the color space of the bitmap
        var pixels = new byte[width * height * 4];
        if (x < int.MaxValue && y < int.MaxValue && x + width > int.MinValue && y + height > int.MinValue)
        {
            ReadPixels(x, y, (int)width, (int)height, pixels);
        }

        ConvertColorSpace(pixels, SurfaceColorSpace, colorSpace);
        WriteImageData(imageData, pixels);
        return imageData;
    }

    /// <summary>Replaces the pixels of the bitmap with the pixels of an image data (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata).</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the buffer of the image data is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata"/>
    [IdlMember("putImageData", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata")]
    public void PutImageData(ImageData imageData, int dx, int dy)
    {
        ArgumentNullException.ThrowIfNull(imageData);
        PutImageData(imageData, dx, dy, 0, 0, (int)Math.Min(imageData.Width, int.MaxValue), (int)Math.Min(imageData.Height, int.MaxValue));
    }

    /// <summary>
    /// Replaces the pixels of the bitmap with the pixels of a dirty rectangle of an image data (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata):
    /// the pixels are not composited, and the current transformation matrix, the clip region, the global alpha and the shadows do not apply.
    /// </summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the buffer of the image data is detached.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata"/>
    [IdlMember("putImageData", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-putimagedata")]
    public void PutImageData(ImageData imageData, int dx, int dy, int dirtyX, int dirtyY, int dirtyWidth, int dirtyHeight)
    {
        ArgumentNullException.ThrowIfNull(imageData);

        // 1. A detached buffer
        if (imageData.IsDetached)
            throw new DomException("The buffer of the image data is detached", "InvalidStateError");

        // 2-4. The dirty rectangle, normalized and clamped to the image data
        long x = dirtyX, y = dirtyY, width = dirtyWidth, height = dirtyHeight;
        if (width < 0)
        {
            x += width;
            width = -width;
        }

        if (height < 0)
        {
            y += height;
            height = -height;
        }

        if (x < 0)
        {
            width += x;
            x = 0;
        }

        if (y < 0)
        {
            height += y;
            y = 0;
        }

        width = Math.Min(width, imageData.Width - x);
        height = Math.Min(height, imageData.Height - y);
        if (width <= 0 || height <= 0 || !Bitmap.HasPixels)
            return;

        // 5. The pixels of the dirty rectangle, converted to the color space of the bitmap, replace the pixels at (dx + x, dy + y)
        var source = imageData.ToRgbaBytes();
        var rectangle = new byte[width * height * 4];
        for (var row = 0; row < height; row++)
        {
            Array.Copy(source, (((y + row) * imageData.Width) + x) * 4, rectangle, row * width * 4, width * 4);
        }

        ConvertColorSpace(rectangle, imageData.ColorSpace, SurfaceColorSpace);

        // The part of the rectangle outside of the bitmap is ignored
        var left = dx + x;
        var top = dy + y;
        var clippedLeft = Math.Max(left, 0);
        var clippedTop = Math.Max(top, 0);
        var clippedRight = Math.Min(left + width, Bitmap.Width);
        var clippedBottom = Math.Min(top + height, Bitmap.Height);
        if (clippedRight <= clippedLeft || clippedBottom <= clippedTop)
            return;

        var clippedWidth = clippedRight - clippedLeft;
        var clippedHeight = clippedBottom - clippedTop;
        var clipped = new byte[clippedWidth * clippedHeight * 4];
        for (var row = 0; row < clippedHeight; row++)
        {
            Array.Copy(rectangle, (((clippedTop - top + row) * width) + (clippedLeft - left)) * 4, clipped, row * clippedWidth * 4, clippedWidth * 4);
        }

        Bitmap.WritePixels((int)clippedLeft, (int)clippedTop, (int)clippedWidth, (int)clippedHeight, clipped);
        OnBitmapChanged();
    }

    /// <summary>Reads the pixels of a rectangle that may be partly outside of the bitmap (those pixels are transparent black).</summary>
    private void ReadPixels(long x, long y, int width, int height, byte[] destination)
    {
        var left = Math.Max(x, 0);
        var top = Math.Max(y, 0);
        var right = Math.Min(x + width, Bitmap.Width);
        var bottom = Math.Min(y + height, Bitmap.Height);
        if (right <= left || bottom <= top)
            return;

        var readWidth = (int)(right - left);
        var readHeight = (int)(bottom - top);
        if (readWidth == width && readHeight == height)
        {
            Bitmap.ReadPixels((int)left, (int)top, width, height, destination);
            return;
        }

        var buffer = new byte[(long)readWidth * readHeight * 4];
        Bitmap.ReadPixels((int)left, (int)top, readWidth, readHeight, buffer);
        for (var row = 0; row < readHeight; row++)
        {
            Array.Copy(buffer, (long)row * readWidth * 4, destination, ((((top - y) + row) * width) + (left - x)) * 4, readWidth * 4);
        }
    }

    /// <summary>Writes RGBA bytes into the array of an image data (converted to half-precision floats for the <c>rgba-float16</c> format).</summary>
    private static void WriteImageData(ImageData imageData, byte[] pixels)
    {
        var data = imageData.Data.Span;
        if (imageData.PixelFormat == ImageDataPixelFormat.RgbaUnorm8)
        {
            pixels.CopyTo(data);
            return;
        }

        var halves = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(data));
        for (var i = 0; i < pixels.Length; i++)
        {
            halves[i] = (Half)(pixels[i] / 255.0);
        }
    }

    /// <summary>The color space of the pixels of the bitmap (the linear color spaces are stored gamma-encoded).</summary>
    private PredefinedColorSpace SurfaceColorSpace => Bitmap.Options.ColorSpace == RenderingColorSpace.DisplayP3 ? PredefinedColorSpace.DisplayP3 : PredefinedColorSpace.Srgb;

    /// <summary>Converts RGBA bytes (not premultiplied) from a color space to another one, in place.</summary>
    internal static void ConvertColorSpace(byte[] pixels, PredefinedColorSpace from, PredefinedColorSpace to)
    {
        if (from == to)
            return;

        var fromName = GetColorSpaceName(from);
        var toName = GetColorSpaceName(to);
        var cache = new Dictionary<int, int>();
        for (var i = 0; i < pixels.Length; i += 4)
        {
            var key = pixels[i] | (pixels[i + 1] << 8) | (pixels[i + 2] << 16);
            if (!cache.TryGetValue(key, out var converted))
            {
                var xyz = CssColorConversion.ToXyz(fromName, [pixels[i] / 255.0, pixels[i + 1] / 255.0, pixels[i + 2] / 255.0]);
                var result = CssColorConversion.FromXyz(toName, xyz);
                converted = ToByte(result[0]) | (ToByte(result[1]) << 8) | (ToByte(result[2]) << 16);
                cache[key] = converted;
            }

            pixels[i] = (byte)converted;
            pixels[i + 1] = (byte)(converted >> 8);
            pixels[i + 2] = (byte)(converted >> 16);
        }

        static int ToByte(double value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
    }

    private static string GetColorSpaceName(PredefinedColorSpace colorSpace) => colorSpace switch
    {
        PredefinedColorSpace.DisplayP3 => "display-p3",
        PredefinedColorSpace.SrgbLinear => "srgb-linear",
        PredefinedColorSpace.DisplayP3Linear => "display-p3-linear",
        _ => "srgb",
    };
}
