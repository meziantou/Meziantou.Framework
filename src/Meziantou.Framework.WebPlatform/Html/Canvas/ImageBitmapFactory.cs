using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Dom.FileApi;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

public partial class GlobalScope
{
    /// <summary>
    /// Creates a bitmap from an image source (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap):
    /// an <c>img</c> element, a <c>canvas</c> element, a <see cref="Blob"/> of an encoded image, an <see cref="ImageData"/> or
    /// another <see cref="ImageBitmap"/>. The images are decoded by the rendering backend of the browsing context
    /// (<see cref="BrowsingContextOptions.RenderingBackend"/>): without backend, an image or a blob cannot be decoded.
    /// </summary>
    /// <param name="image">The image source.</param>
    /// <param name="options">The orientation, the scaling and the color options.</param>
    /// <returns>A task that completes with the bitmap (in a task of the event loop), or fails with an <c>InvalidStateError</c> when the source cannot be used or decoded.</returns>
    /// <seealso href="https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap"/>
    [IdlMember("createImageBitmap", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap")]
    public Task<ImageBitmap> CreateImageBitmap(object? image, ImageBitmapOptions? options = null) => ImageBitmapFactory.Create(this, image, sourceRect: null, options);

    /// <summary>
    /// Creates a bitmap from a rectangle of an image source (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap):
    /// see <see cref="CreateImageBitmap(object?, ImageBitmapOptions?)"/>. The parts of the rectangle outside of the image are
    /// transparent black; a negative width or height extends the rectangle to the left or to the top.
    /// </summary>
    /// <seealso href="https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap"/>
    [IdlMember("createImageBitmap", "https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap")]
    public Task<ImageBitmap> CreateImageBitmap(object? image, int sx, int sy, int sw, int sh, ImageBitmapOptions? options = null) => ImageBitmapFactory.Create(this, image, (sx, sy, sw, sh), options);
}

/// <summary>
/// The steps of <c>createImageBitmap()</c> (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap):
/// the bitmap data of the source, cropped to the source rectangle with formatting (scaled, flipped, premultiplied). The
/// pixels are processed in managed code; only the decoding of the images needs the rendering backend.
/// </summary>
internal static class ImageBitmapFactory
{
    public static Task<ImageBitmap> Create(GlobalScope global, object? image, (int X, int Y, int Width, int Height)? sourceRect, ImageBitmapOptions? options)
    {
        ArgumentNullException.ThrowIfNull(global);
        ArgumentNullException.ThrowIfNull(image);
        options ??= new ImageBitmapOptions();

        // 1. A resize width or height of zero, 2. a source rectangle with a zero width or height
        if (options.ResizeWidth == 0 || options.ResizeHeight == 0)
            return Reject("The resize width and height must not be zero", "InvalidStateError");

        if (sourceRect is { Width: 0 } or { Height: 0 })
            return Task.FromException<ImageBitmap>(new RangeErrorException("The width and the height of the source rectangle must not be zero"));

        BitmapPixels? bitmap;
        try
        {
            // An SVG image is rendered at the size of the bitmap; the other sources are cropped to the source rectangle with formatting
            bitmap = GetSvgImage(image) is var (svgImage, corsSameOrigin)
                ? RenderSvgImage(global, svgImage, corsSameOrigin, sourceRect, options)
                : Format(GetSourceBitmap(global, image, options), sourceRect, options);
        }
        catch (DomException ex)
        {
            return Task.FromException<ImageBitmap>(ex);
        }

        if (bitmap is null)
            return Reject("The image bitmap is too large", "InvalidStateError");

        // The promise is resolved in a task (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap:
        // "queue a global task on the bitmap task source")
        var completion = new TaskCompletionSource<ImageBitmap>();
        global.EventLoop.QueueTask(() => completion.TrySetResult(new ImageBitmap(bitmap)));
        return completion.Task;

        static Task<ImageBitmap> Reject(string message, string name) => Task.FromException<ImageBitmap>(new DomException(message, name));
    }

    /// <summary>
    /// Returns the bitmap data of a source, after the check of its usability
    /// (https://html.spec.whatwg.org/multipage/canvas.html#check-the-usability-of-the-image-argument), or throws an <c>InvalidStateError</c>.
    /// </summary>
    private static BitmapPixels GetSourceBitmap(GlobalScope global, object image, ImageBitmapOptions options)
    {
        var convertColors = options.ColorSpaceConversion != ColorSpaceConversion.None;
        switch (image)
        {
            case HtmlImageElement img:
            {
                if (img.GetBitmapSource() is not var (paintImage, naturalSize, corsSameOrigin))
                    throw InvalidState("The image is not available");

                // An image without natural dimensions (e.g. an SVG image without width or height) cannot be used
                if (naturalSize.Width is not > 0 || naturalSize.Height is not > 0)
                    throw InvalidState("The image has no natural dimensions");

                if (global.Options.RenderingBackend is not { } backend || paintImage?.GetDecoder(backend) is not { } decoder)
                    throw InvalidState("The image cannot be decoded");

                // The default image of an animated image (its first frame), with its EXIF orientation
                return ScaleToNaturalSize(Decode(decoder, paintImage.Orientation, convertColors, corsSameOrigin), paintImage);
            }

            case HtmlCanvasElement canvas:
            {
                var width = canvas.Width;
                var height = canvas.Height;
                if (width == 0 || height == 0)
                    throw InvalidState("The canvas has no pixels");

                if ((long)width * height > BitmapPixels.MaxPixelCount)
                    throw InvalidState("The canvas is too large");

                return canvas.GetBitmapSnapshot();
            }

            case OffscreenCanvas offscreen:
            {
                if (offscreen.IsDetached)
                    throw InvalidState("The OffscreenCanvas is detached");

                if (offscreen.Width == 0 || offscreen.Height == 0)
                    throw InvalidState("The OffscreenCanvas has no pixels");

                if ((double)offscreen.Width * offscreen.Height > BitmapPixels.MaxPixelCount)
                    throw InvalidState("The OffscreenCanvas is too large");

                return offscreen.GetBitmapSnapshot();
            }

            case ImageBitmap bitmap:
                return bitmap.Bitmap ?? throw InvalidState("The image bitmap is detached");

            case ImageData imageData:
            {
                if (imageData.IsDetached)
                    throw InvalidState("The buffer of the image data is detached");

                // The bitmaps are sRGB: the pixels of the other color spaces are converted
                var pixels = imageData.Data.ToArray();
                if (imageData.PixelFormat == ImageDataPixelFormat.RgbaFloat16)
                    CanvasRenderingContext2DBase.ConvertColorSpace(unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(pixels.AsSpan())), imageData.ColorSpace, PredefinedColorSpace.Srgb);
                else
                    CanvasRenderingContext2DBase.ConvertColorSpace(pixels, imageData.ColorSpace, PredefinedColorSpace.Srgb);

                return new BitmapPixels((int)imageData.Width, (int)imageData.Height, pixels, pixelFormat: imageData.PixelFormat);
            }

            case Blob blob:
            {
                // The data of the blob is sniffed (https://mimesniff.spec.whatwg.org/#rules-for-sniffing-images-specifically)
                var data = blob.Data;
                if (global.Options.RenderingBackend is not { } backend || !ImageHeaders.HasRasterSignature(data.Span))
                    throw InvalidState("The blob is not an image that can be decoded");

                using var decoder = backend.CreateImageDecoder(data, mimeType: null) ?? throw InvalidState("The blob is not an image that can be decoded");
                return Decode(decoder, ImageMetadata.GetExifOrientation(data.Span), convertColors, originClean: true);
            }

            case HtmlVideoElement video:
            {
                // https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap: the frame at
                // the current playback position (the media backend gives it), unless the ready state is HAVE_NOTHING or HAVE_METADATA
                if (video.GetVideoFrame() is not { } frame)
                    throw InvalidState("The video has no frame");

                return new BitmapPixels(frame.Width, frame.Height, frame.Pixels.ToArray(), video.IsMediaDataCorsSameOrigin);
            }

            case SvgImageElement svgImage:
            {
                // The SVG image elements are used as the img elements: their image, once it is loaded
                if (svgImage.Image is not { } paintImage || svgImage.NaturalSize is not { Width: > 0, Height: > 0 })
                    throw InvalidState("The image is not available");

                if (global.Options.RenderingBackend is not { } backend || paintImage.GetDecoder(backend) is not { } decoder)
                    throw InvalidState("The image cannot be decoded");

                return ScaleToNaturalSize(Decode(decoder, paintImage.Orientation, convertColors, paintImage.IsCorsSameOrigin), paintImage);
            }

            default:
                // A video frame or another source
                throw InvalidState("The image source cannot be used");
        }
    }

    /// <summary>Returns the document of the SVG image of an <c>img</c> or SVG <c>image</c> element whose resource is an SVG image, and whether it is CORS-same-origin; or null.</summary>
    /// <exception cref="DomException">An <c>InvalidStateError</c> when the SVG image cannot be parsed.</exception>
    private static (SvgImage Image, bool CorsSameOrigin)? GetSvgImage(object image)
    {
        var (paintImage, host, corsSameOrigin) = image switch
        {
            HtmlImageElement img when img.GetBitmapSource() is ({ IsSvg: true } source, _, var cors) => (source, img.NodeDocument, cors),
            SvgImageElement svgImage when svgImage.Image is { IsSvg: true } source => (source, svgImage.NodeDocument, source.IsCorsSameOrigin),
            _ => default((PaintImage?, Document?, bool)),
        };
        if (paintImage is null || host is null)
            return null;

        return SvgImage.Get(paintImage, host) is { } svg ? (svg, corsSameOrigin) : throw InvalidState("The image cannot be decoded");
    }

    /// <summary>
    /// Renders an SVG image to a bitmap (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-createimagebitmap):
    /// an image without natural dimensions needs the resize width and height (the size it is rendered at). The image is
    /// rendered at the output size (the resize options scale the vector image, not its pixels), then cropped to the source
    /// rectangle with formatting.
    /// </summary>
    private static BitmapPixels? RenderSvgImage(GlobalScope global, SvgImage svg, bool corsSameOrigin, (int X, int Y, int Width, int Height)? sourceRect, ImageBitmapOptions options)
    {
        double width, height;
        if (svg.NaturalSize is { Width: > 0 and var naturalWidth, Height: > 0 and var naturalHeight })
        {
            (width, height) = (naturalWidth, naturalHeight);
        }
        else if (svg.NaturalSize.Width is 0 || svg.NaturalSize.Height is 0 || options.ResizeWidth is not { } resizeWidth || options.ResizeHeight is not { } resizeHeight)
        {
            throw InvalidState("The SVG image has no natural dimensions");
        }
        else
        {
            (width, height) = (resizeWidth, resizeHeight);
        }

        if (global.Options.RenderingBackend is not { } backend)
            throw InvalidState("The image cannot be decoded");

        // The source rectangle in image pixels (a negative size extends it to the left or to the top), and the output size
        double sx = 0, sy = 0, sw = width, sh = height;
        if (sourceRect is var (x, y, w, h))
        {
            (sx, sy, sw, sh) = (w < 0 ? x + w : x, h < 0 ? y + h : y, Math.Abs((double)w), Math.Abs((double)h));
        }

        var outputWidth = options.ResizeWidth is { } resizeWidth2 ? resizeWidth2 : options.ResizeHeight is { } heightOnly ? Math.Ceiling(sw * heightOnly / sh) : Math.Round(sw, MidpointRounding.AwayFromZero);
        var outputHeight = options.ResizeHeight is { } resizeHeight2 ? resizeHeight2 : options.ResizeWidth is { } widthOnly ? Math.Ceiling(sh * widthOnly / sw) : Math.Round(sh, MidpointRounding.AwayFromZero);
        var scaleX = outputWidth / sw;
        var scaleY = outputHeight / sh;
        var renderWidth = Math.Round(width * scaleX, MidpointRounding.AwayFromZero);
        var renderHeight = Math.Round(height * scaleY, MidpointRounding.AwayFromZero);
        if (!(outputWidth >= 1) || !(outputHeight >= 1) || !(renderWidth >= 1) || !(renderHeight >= 1) || renderWidth * renderHeight > BitmapPixels.MaxPixelCount || outputWidth * outputHeight > BitmapPixels.MaxPixelCount)
            return null;

        using var rendered = svg.Rasterize(backend, (int)renderWidth, (int)renderHeight) ?? throw InvalidState("The image cannot be decoded");
        var pixels = new byte[(long)rendered.Width * rendered.Height * 4];
        rendered.ReadPixels(pixels);
        var input = new BitmapPixels(rendered.Width, rendered.Height, pixels, corsSameOrigin);
        var scaledRect = ((int)Math.Round(sx * scaleX, MidpointRounding.AwayFromZero), (int)Math.Round(sy * scaleY, MidpointRounding.AwayFromZero), (int)outputWidth, (int)outputHeight);
        return Format(input, scaledRect, new ImageBitmapOptions { ImageOrientation = options.ImageOrientation, PremultiplyAlpha = options.PremultiplyAlpha, ColorSpaceConversion = options.ColorSpaceConversion, ResizeQuality = options.ResizeQuality });
    }

    private static BitmapPixels Decode(RenderingImageDecoder decoder, ExifOrientation orientation, bool convertColors, bool originClean)
    {
        using var frame = decoder.DecodeFrame(0, orientation, convertColors) ?? throw InvalidState("The image cannot be decoded");
        var pixels = new byte[(long)frame.Width * frame.Height * (frame.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4)];
        frame.ReadPixels(pixels, frame.PixelFormat);
        return new BitmapPixels(frame.Width, frame.Height, pixels, originClean, frame.PixelFormat);
    }

    /// <summary>
    /// Scales the decoded pixels of an image to its natural size when they differ: the density-corrected dimensions of an image
    /// whose EXIF resolution gives them (https://html.spec.whatwg.org/multipage/images.html#prepare-an-image-for-presentation),
    /// so that the source rectangle and the size of the bitmap are in the natural pixels of the image.
    /// </summary>
    private static BitmapPixels ScaleToNaturalSize(BitmapPixels pixels, PaintImage image)
    {
        var width = (int)Math.Round(image.Width, MidpointRounding.AwayFromZero);
        var height = (int)Math.Round(image.Height, MidpointRounding.AwayFromZero);
        if (width <= 0 || height <= 0 || (width == pixels.Width && height == pixels.Height))
            return pixels;

        return Format(pixels, sourceRect: null, new ImageBitmapOptions { ResizeWidth = (uint)width, ResizeHeight = (uint)height, ResizeQuality = ResizeQuality.High }) ?? throw InvalidState("The image is too large");
    }

    private static DomException InvalidState(string message) => new(message, "InvalidStateError");

    /// <summary>
    /// Crops a bitmap to a source rectangle with formatting (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#cropped-to-the-source-rectangle-with-formatting):
    /// the pixels outside of the input are transparent black, the result is scaled to the output size, flipped vertically for
    /// <c>flipY</c>, and premultiplied for <c>premultiply</c>. Returns null when the result is too large.
    /// </summary>
    internal static BitmapPixels? Format(BitmapPixels input, (int X, int Y, int Width, int Height)? sourceRect, ImageBitmapOptions options)
    {
        // 1. The source rectangle (a negative size extends it to the left or to the top)
        long sx = 0, sy = 0, sw = input.Width, sh = input.Height;
        if (sourceRect is var (x, y, w, h))
        {
            (sx, sy, sw, sh) = (x, y, w, h);
            if (sw < 0)
            {
                sx += sw;
                sw = -sw;
            }

            if (sh < 0)
            {
                sy += sh;
                sh = -sh;
            }
        }

        // 2-3. The output size: the resize width and height, or the size of the source rectangle (keeping its aspect ratio
        // when only one of them is set)
        double outputWidthValue = options.ResizeWidth is { } resizeWidth ? resizeWidth : options.ResizeHeight is { } heightOnly ? Math.Ceiling(sw * (double)heightOnly / sh) : sw;
        double outputHeightValue = options.ResizeHeight is { } resizeHeight ? resizeHeight : options.ResizeWidth is { } widthOnly ? Math.Ceiling(sh * (double)widthOnly / sw) : sh;
        if (outputWidthValue <= 0 || outputHeightValue <= 0 || outputWidthValue * outputHeightValue > BitmapPixels.MaxPixelCount || (double)sw * sh > BitmapPixels.MaxPixelCount)
            return null;

        var outputWidth = (long)outputWidthValue;
        var outputHeight = (long)outputHeightValue;

        // 4-5. The source rectangle of the input (the pixels outside of the input are transparent black)
        var bytesPerPixel = input.PixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4;
        byte[] cropped;
        if (sx == 0 && sy == 0 && sw == input.Width && sh == input.Height)
        {
            cropped = input.PixelBytes;
        }
        else
        {
            cropped = new byte[sw * sh * bytesPerPixel];
            var left = Math.Max(0, sx);
            var right = Math.Min(input.Width, sx + sw);
            if (right > left)
            {
                for (var row = Math.Max(0, sy); row < Math.Min(input.Height, sy + sh); row++)
                {
                    var sourceOffset = ((row * input.Width) + left) * bytesPerPixel;
                    var destinationOffset = (((row - sy) * sw) + (left - sx)) * bytesPerPixel;
                    Array.Copy(input.PixelBytes, sourceOffset, cropped, destinationOffset, (right - left) * bytesPerPixel);
                }
            }
        }

        // 6. Scaled to the output size with the resize quality
        var output = outputWidth == sw && outputHeight == sh ? cropped : Resize(cropped, (int)sw, (int)sh, (int)outputWidth, (int)outputHeight, options.ResizeQuality, input.PixelFormat);

        // 7. Flipped vertically
        if (options.ImageOrientation == ImageBitmapOrientation.FlipY)
        {
            output = FlipVertically(output, (int)outputWidth, (int)outputHeight, bytesPerPixel);
        }

        // 8. Premultiplied: the colors keep the precision of premultiplied 8-bit components (the bitmap stores unpremultiplied colors)
        if (options.PremultiplyAlpha == PremultiplyAlpha.Premultiply)
        {
            output = ReducePrecisionByPremultiplication(output, input.PixelFormat);
        }

        return new BitmapPixels((int)outputWidth, (int)outputHeight, output, input.IsOriginClean, input.PixelFormat);
    }

    private static byte[] FlipVertically(byte[] pixels, int width, int height, int bytesPerPixel = 4)
    {
        var result = new byte[pixels.Length];
        var stride = width * bytesPerPixel;
        for (var row = 0; row < height; row++)
        {
            Array.Copy(pixels, row * stride, result, (height - 1 - row) * stride, stride);
        }

        return result;
    }

    private static byte[] ReducePrecisionByPremultiplication(byte[] pixels, ImageDataPixelFormat pixelFormat)
    {
        var result = new byte[pixels.Length];
        if (pixelFormat == ImageDataPixelFormat.RgbaFloat16)
        {
            var source = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(pixels.AsSpan()));
            var target = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(result.AsSpan()));
            for (var i = 0; i < source.Length; i += 4)
            {
                var alpha = (float)source[i + 3];
                target[i + 3] = source[i + 3];
                if (alpha == 0)
                    continue;

                for (var c = 0; c < 3; c++)
                {
                    var premultiplied = (Half)((float)source[i + c] * alpha);
                    target[i + c] = (Half)((float)premultiplied / alpha);
                }
            }

            return result;
        }

        for (var i = 0; i < pixels.Length; i += 4)
        {
            var alpha = pixels[i + 3];
            result[i + 3] = alpha;
            if (alpha == 0)
                continue;

            for (var c = 0; c < 3; c++)
            {
                var premultiplied = (int)Math.Round(pixels[i + c] * alpha / 255.0, MidpointRounding.AwayFromZero);
                result[i + c] = (byte)Math.Min(255, (int)Math.Round(premultiplied * 255.0 / alpha, MidpointRounding.AwayFromZero));
            }
        }

        return result;
    }

    /// <summary>
    /// Scales pixels (https://html.spec.whatwg.org/multipage/imagebitmap-and-animations.html#dom-imagebitmapoptions-resizequality):
    /// the nearest pixel for <c>pixelated</c>, a bilinear interpolation of the premultiplied colors otherwise, which averages
    /// the covered pixels when the image is reduced with the <c>medium</c> and <c>high</c> qualities.
    /// </summary>
    internal static byte[] Resize(byte[] pixels, int width, int height, int outputWidth, int outputHeight, ResizeQuality quality, ImageDataPixelFormat pixelFormat = ImageDataPixelFormat.RgbaUnorm8)
    {
        var bytesPerPixel = pixelFormat == ImageDataPixelFormat.RgbaFloat16 ? 8 : 4;
        var result = new byte[(long)outputWidth * outputHeight * bytesPerPixel];
        var scaleX = (double)width / outputWidth;
        var scaleY = (double)height / outputHeight;
        Span<double> sum = stackalloc double[4];
        for (var y = 0; y < outputHeight; y++)
        {
            for (var x = 0; x < outputWidth; x++)
            {
                var destination = (((long)y * outputWidth) + x) * 4;
                if (quality == ResizeQuality.Pixelated)
                {
                    var sourceX = Math.Min(width - 1, (int)((x + 0.5) * scaleX));
                    var sourceY = Math.Min(height - 1, (int)((y + 0.5) * scaleY));
                    Array.Copy(pixels, (((long)sourceY * width) + sourceX) * bytesPerPixel, result, destination * (bytesPerPixel / 4), bytesPerPixel);
                    continue;
                }

                sum.Clear();
                if (quality is ResizeQuality.Medium or ResizeQuality.High && (scaleX > 1 || scaleY > 1))
                {
                    // The average of the source pixels covered by the destination pixel (weighted by their coverage)
                    var x0 = x * scaleX;
                    var x1 = x0 + scaleX;
                    var y0 = y * scaleY;
                    var y1 = y0 + scaleY;
                    for (var sourceY = (int)y0; sourceY < Math.Min(height, (int)Math.Ceiling(y1)); sourceY++)
                    {
                        var coverageY = Math.Min(y1, sourceY + 1) - Math.Max(y0, sourceY);
                        for (var sourceX = (int)x0; sourceX < Math.Min(width, (int)Math.Ceiling(x1)); sourceX++)
                        {
                            var coverage = coverageY * (Math.Min(x1, sourceX + 1) - Math.Max(x0, sourceX));
                            Accumulate(pixels, (((long)sourceY * width) + sourceX) * 4, coverage, sum, pixelFormat);
                        }
                    }
                }
                else
                {
                    // Bilinear interpolation between the centers of the source pixels (the edges are clamped)
                    var u = ((x + 0.5) * scaleX) - 0.5;
                    var v = ((y + 0.5) * scaleY) - 0.5;
                    var left = (int)Math.Floor(u);
                    var top = (int)Math.Floor(v);
                    var fx = u - left;
                    var fy = v - top;
                    for (var j = 0; j < 2; j++)
                    {
                        var sourceY = Math.Clamp(top + j, 0, height - 1);
                        var weightY = j == 0 ? 1 - fy : fy;
                        for (var i = 0; i < 2; i++)
                        {
                            var sourceX = Math.Clamp(left + i, 0, width - 1);
                            var weight = weightY * (i == 0 ? 1 - fx : fx);
                            if (weight > 0)
                            {
                                Accumulate(pixels, (((long)sourceY * width) + sourceX) * 4, weight, sum, pixelFormat);
                            }
                        }
                    }
                }

                // The weighted sums of the premultiplied colors and of the alpha (from 0 to 1)
                var totalAlpha = sum[3];
                if (totalAlpha <= 0)
                    continue;

                WriteComponent(result, destination + 3, totalAlpha / WeightSum(quality, scaleX, scaleY, x, y, width, height), pixelFormat);
                for (var c = 0; c < 3; c++)
                {
                    WriteComponent(result, destination + c, sum[c] / totalAlpha, pixelFormat);
                }
            }
        }

        return result;

        static void Accumulate(byte[] pixels, long offset, double weight, Span<double> sum, ImageDataPixelFormat pixelFormat)
        {
            var alpha = ReadComponent(pixels, offset + 3, pixelFormat) * weight;
            sum[0] += ReadComponent(pixels, offset, pixelFormat) * alpha;
            sum[1] += ReadComponent(pixels, offset + 1, pixelFormat) * alpha;
            sum[2] += ReadComponent(pixels, offset + 2, pixelFormat) * alpha;
            sum[3] += alpha;
        }

        static double ReadComponent(byte[] pixels, long offset, ImageDataPixelFormat pixelFormat)
            => pixelFormat == ImageDataPixelFormat.RgbaFloat16 ? (double)unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(pixels.AsSpan()))[(int)offset] : pixels[offset] / 255.0;

        static void WriteComponent(byte[] pixels, long offset, double value, ImageDataPixelFormat pixelFormat)
        {
            if (pixelFormat == ImageDataPixelFormat.RgbaFloat16)
            {
                var halves = unsafe(System.Runtime.InteropServices.MemoryMarshal.Cast<byte, Half>(pixels.AsSpan()));
                halves[(int)offset] = (Half)value;
            }
            else
            {
                pixels[offset] = (byte)Math.Clamp(Math.Round(value * 255, MidpointRounding.AwayFromZero), 0, 255);
            }
        }
    }

    /// <summary>The total weight of the source pixels of a destination pixel (the area of the pixel in source pixels, 1 for the bilinear interpolation).</summary>
    private static double WeightSum(ResizeQuality quality, double scaleX, double scaleY, int x, int y, int width, int height)
    {
        if (quality is not (ResizeQuality.Medium or ResizeQuality.High) || (scaleX <= 1 && scaleY <= 1))
            return 1;

        var x0 = x * scaleX;
        var y0 = y * scaleY;
        return (Math.Min(width, x0 + scaleX) - x0) * (Math.Min(height, y0 + scaleY) - y0);
    }
}
