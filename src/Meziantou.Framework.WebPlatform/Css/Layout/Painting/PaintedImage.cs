using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Om;
using Meziantou.Framework.WebPlatform.Css.Syntax;
using Meziantou.Framework.WebPlatform.Css.Values;
using Meziantou.Framework.WebPlatform.Dom;
using Meziantou.Framework.WebPlatform.Infra;
using Meziantou.Framework.WebPlatform.Rendering;
using Meziantou.Framework.WebPlatform.Urls;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// An &lt;image&gt; value (https://drafts.csswg.org/css-images-3/#image-values) resolved for the painting: a fetched raster
/// image, a gradient, or a solid color (<c>image(color)</c>). It is drawn in a rectangle of a concrete object size (the size of
/// a tile of a background layer, of a border image, of a list marker...).
/// </summary>
internal abstract class PaintedImage
{
    /// <summary>The natural width in CSS px (zoomed), or null when the image has none (a gradient).</summary>
    public virtual double? NaturalWidth => null;

    /// <summary>The natural height in CSS px (zoomed), or null when the image has none (a gradient).</summary>
    public virtual double? NaturalHeight => null;

    /// <summary>The natural aspect ratio (width / height), or null.</summary>
    public virtual double? NaturalRatio => NaturalWidth is { } width && NaturalHeight is { } height && width > 0 && height > 0 ? width / height : null;

    /// <summary>
    /// Returns the shader that paints the image in a rectangle from (0, 0) to <paramref name="size"/> (nothing outside of it for a
    /// raster image), or null when nothing is painted.
    /// </summary>
    public abstract DisplayShader? CreateShader(PhysicalSize size);

    /// <summary>
    /// Paints the image in a rectangle (clipped to the current clip): a raster image is drawn scaled, a generated image fills
    /// the rectangle with its shader.
    /// </summary>
    public virtual void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source)
    {
        if (destination.IsEmpty || CreateShader(destination.Size) is not { } shader)
            return;

        builder.Add(new DrawRectItem(destination, new DisplayPaint(DisplayColor.Black) { Shader = shader with { LocalTransform = Translate(shader.LocalTransform, destination.X, destination.Y) } }) { Source = source });
    }

    /// <summary>Returns a transform followed by a translation.</summary>
    internal static DisplayMatrix Translate(DisplayMatrix? transform, double x, double y)
    {
        var translation = DisplayMatrix.Translation(x, y);
        return transform is { } matrix ? translation.PreConcat(matrix) : translation;
    }

    /// <summary>
    /// Resolves an &lt;image&gt; value of a computed value (e.g. a layer of <c>background-image</c>, <c>list-style-image</c>,
    /// <c>border-image-source</c>): a <c>url()</c> of an image fetched by <see cref="CssImageFetching"/>, a gradient, <c>image()</c>
    /// (its color, or its first image), <c>image-set()</c> (its image for the resolution of the device) and <c>cross-fade()</c>.
    /// Returns null for <c>none</c>, an image that is not available (not fetched yet, not decodable) and <c>element()</c>.
    /// </summary>
    /// <param name="document">The document of the image.</param>
    /// <param name="style">The style of the element that paints the image.</param>
    /// <param name="value">The image value.</param>
    /// <param name="painter">The painter, which selects the displayed frame of the animated images, or null for the first frame.</param>
    public static PaintedImage? Resolve(Document document, ComputedStyle style, CssComponentValue value, Painter? painter = null)
    {
        switch (value)
        {
            case CssPreservedToken { Type: CssTokenType.Url }:
            case CssFunction url when InfraAscii.EqualsIgnoreAsciiCase(url.Name, "url") || InfraAscii.EqualsIgnoreAsciiCase(url.Name, "src"):
                return CssUrlModifiers.TryGetUrl(value, out var address, out _) ? ResolveUrl(document, style, address, painter) : null;

            case CssPreservedToken { Type: CssTokenType.String } text:
                return ResolveUrl(document, style, text.Token.Value, painter);

            case CssFunction function:
            {
                var name = InfraAscii.ToAsciiLowercase(function.Name);
                if (name.EndsWith("gradient", StringComparison.Ordinal))
                    return CssGradient.Parse(function, style) is { } gradient ? new GradientImage(gradient) : null;

                if (name is "image-set" or "-webkit-image-set")
                {
                    // https://drafts.csswg.org/css-images-4/#image-set-notation: the option for the resolution of the device (as
                    // for the fetching), whose resolution is the natural resolution of the image (an image of 100x50 image
                    // pixels at 0.5x is 200x100 CSS px); an option whose resolution is zero is an invalid image
                    if (CssImageSet.Select(function, document.GetStyleEngine().Environment.DevicePixelRatio) is not { Resolution: > 0 } option || Resolve(document, style, option.Image, painter) is not { } selected)
                        return null;

                    return option.Resolution == 1 || selected.NaturalWidth is null ? selected : new ScaledImage(selected, 1 / option.Resolution);
                }

                if (name == "cross-fade")
                    return CrossFadeImage.Create(document, style, function, painter) is { } crossFade ? (crossFade.GetColor() is { } mixed ? new ColorImage(mixed) : crossFade) : null;

                if (name == "filter")
                {
                    // https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue: filter( [ <image> | <string> ], <filter-value-list> )
                    var arguments = ValueMatcher.RemoveWhitespace(function.Value);
                    var comma = arguments.FindIndex(argument => argument is CssPreservedToken { Type: CssTokenType.Comma });
                    if (comma != 1 || Resolve(document, style, arguments[0], painter) is not { } input || FilterEffects.Get(arguments.GetRange(comma + 1, arguments.Count - comma - 1), style, space: null) is not { } filters)
                        return null;

                    return new FilteredImage(input, filters);
                }

                if (name == "paint")
                    return PaintWorkletImage.Create(document, style, function);

                if (name == "image")
                {
                    // https://drafts.csswg.org/css-images-4/#image-notation: image(<color>) is a solid color image without natural
                    // dimensions; image(<image>, <color>) is the image (the fallback color is used when it cannot be loaded)
                    PaintedImage? image = null;
                    foreach (var argument in ValueMatcher.SplitAtCommas(ValueMatcher.RemoveWhitespace(function.Value)))
                    {
                        if (argument is not [var single])
                            continue;

                        if (ValueMatcher.IsColor(single))
                        {
                            if (image is null && Painter.ResolveColor(style, single) is { } color)
                                return new ColorImage(color);

                            continue;
                        }

                        image ??= Resolve(document, style, single, painter);
                    }

                    return image;
                }

                return null;
            }

            default:
                return null;
        }
    }

    /// <summary>Resolves the image of a URL: the image fetched for the computed values of the document.</summary>
    private static PaintedImage? ResolveUrl(Document document, ComputedStyle style, string url, Painter? painter)
    {
        if (url.Length == 0 || document.CssPaintImages is not { } images || UrlParser.Parse(url, document.DocumentBaseUrl) is not { } parsed)
            return null;

        if (!images.TryGetValue(parsed.Serialize(), out var image))
            return null;

        if (image.IsSvg)
            return SvgImage.Get(image, document) is { } svgImage ? SvgPaintedImage.Create(svgImage, style) : null;

        if (image.Width <= 0 || image.Height <= 0)
            return null;

        // https://drafts.csswg.org/css-images-3/#image-orientation: image-orientation applies to the CSS images of the element (its
        // backgrounds, border images, list markers, masks), as in the browsers
        image = image.WithOrientation(style.Layout.Replaced.ImageOrientation.FromImage);
        if (painter is not null)
        {
            image = painter.GetDisplayedFrame(image);
        }

        return new RasterImage(image, style.Layout.EffectiveZoom, GetSampling(style.GetComputedValue("image-rendering")));
    }

    /// <summary>Returns the sampling of a value of <c>image-rendering</c> (https://drafts.csswg.org/css-images-3/#the-image-rendering).</summary>
    internal static ImageSampling GetSampling(string imageRendering) => imageRendering switch
    {
        "pixelated" or "crisp-edges" or "optimizespeed" or "-webkit-optimize-contrast" => ImageSampling.Pixelated,
        "high-quality" => ImageSampling.HighQuality,
        _ => ImageSampling.Smooth,
    };

    /// <summary>An image whose natural dimensions are scaled (the natural resolution given by an option of <c>image-set()</c>).</summary>
    private sealed class ScaledImage(PaintedImage input, double scale) : PaintedImage
    {
        public override double? NaturalWidth => input.NaturalWidth * scale;

        public override double? NaturalHeight => input.NaturalHeight * scale;

        public override double? NaturalRatio => input.NaturalRatio;

        public override DisplayShader? CreateShader(PhysicalSize size) => input.CreateShader(size);

        public override void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source) => input.Draw(builder, destination, source);
    }

    /// <summary>A raster image: its natural size is its size in image pixels (one CSS px each), multiplied by the zoom.</summary>
    internal sealed class RasterImage(PaintImage image, double zoom, ImageSampling sampling) : PaintedImage
    {
        public PaintImage Image { get; } = image;

        public override double? NaturalWidth => Image.Width * zoom;

        public override double? NaturalHeight => Image.Height * zoom;

        // The edges of the image are clamped (as the images drawn by the browsers: a scaled image does not fade at its edges); the
        // users of the shader paint only the rectangle of the image
        public override DisplayShader CreateShader(PhysicalSize size)
            => new ImageShader(Image, SpreadMode.Pad, SpreadMode.Pad, sampling) { LocalTransform = DisplayMatrix.Scale(size.Width / Image.Width, size.Height / Image.Height) };

        public override void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source)
        {
            if (!destination.IsEmpty)
            {
                builder.Add(new DrawImageItem(Image, new PhysicalRect(0, 0, Image.Width, Image.Height), destination, sampling) { Source = source });
            }
        }
    }

    /// <summary>
    /// An SVG image (<see cref="SvgImage"/>): its natural dimensions are the ones of its root svg element (multiplied by the zoom),
    /// it is painted by its document laid out in the rectangle it is drawn in, clipped to the rectangle.
    /// </summary>
    internal sealed class SvgPaintedImage(SvgImage image, double zoom, string? colorScheme, string? preserveAspectRatio = null) : PaintedImage
    {
        public SvgImage Image { get; } = image;

        /// <summary>Creates the image used by an element: the color scheme of the image is the used color scheme of the element.</summary>
        public static SvgPaintedImage Create(SvgImage image, ComputedStyle style)
            => new(image, style.Layout.EffectiveZoom, CssValueComputer.GetUsedColorScheme(style));

        public override double? NaturalWidth => Image.NaturalSize.Width * zoom;

        public override double? NaturalHeight => Image.NaturalSize.Height * zoom;

        public override double? NaturalRatio => base.NaturalRatio ?? Image.NaturalSize.AspectRatio;

        public override DisplayShader? CreateShader(PhysicalSize size)
            => Image.GetDisplayList(size, colorScheme, preserveAspectRatio, zoom) is { } list ? new DisplayListShader(list, size) : null;

        public override void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source) => Draw(builder, destination, source, layoutSize: null);

        /// <summary>
        /// Draws the image in a rectangle. The image is laid out at <paramref name="layoutSize"/> (the size of the rectangle before
        /// it was snapped to the device pixels) and scaled to the rectangle, so that an image whose size has the aspect ratio of
        /// its viewBox is not letterboxed by the snapping.
        /// </summary>
        public void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source, PhysicalSize? layoutSize)
        {
            var size = layoutSize is { Width: > 0, Height: > 0 } unsnapped ? unsnapped : destination.Size;
            if (destination.IsEmpty || Image.GetDisplayList(size, colorScheme, preserveAspectRatio, zoom) is not { } list)
                return;

            var saveCount = builder.SaveCount;
            if (source is null)
            {
                builder.Save();
            }
            else
            {
                builder.Save(source);
            }

            builder.Add(new ClipRectItem(destination) { Source = source });
            builder.Transform(DisplayMatrix.Translation(destination.X, destination.Y));
            if (size != destination.Size)
            {
                builder.Transform(DisplayMatrix.Scale(destination.Width / size.Width, destination.Height / size.Height));
            }

            // https://drafts.fxtf.org/compositing-1/#csscompositingrules_SVG: an SVG document linked as an image is an isolated
            // group: the elements of the image that blend (mix-blend-mode) do not blend with what is below the image
            if (HasBlendedGroups(list))
            {
                builder.SaveLayer(new SaveLayerItem(1, BlendMode.Normal, Filters: null) { Source = source });
            }

            builder.AddList(list);
            builder.RestoreTo(saveCount);
        }

        private static bool HasBlendedGroups(DisplayList list)
        {
            foreach (var item in list.Items)
            {
                if (item is SaveLayerItem { BlendMode: not BlendMode.Normal })
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// An image of the <c>filter()</c> function (https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue): the input image drawn in
    /// its rectangle, filtered in the coordinates of the rectangle (the lengths of the filters are not scaled with the image, as
    /// in the browsers), and clipped to the rectangle.
    /// </summary>
    private sealed class FilteredImage(PaintedImage input, DisplayFilter[] filters) : PaintedImage
    {
        public override double? NaturalWidth => input.NaturalWidth;

        public override double? NaturalHeight => input.NaturalHeight;

        public override DisplayShader? CreateShader(PhysicalSize size)
            => size.Width > 0 && size.Height > 0 && input.CreateShader(size) is { } content ? new FilteredShader(content, size, filters) : null;
    }

    /// <summary>
    /// The <c>cross-fade()</c> function (https://drafts.csswg.org/css-images-4/#cross-fade-function): the weighted sum of its images
    /// (the colors are solid color images), each drawn in the rectangle of the result, with premultiplied colors. Its natural
    /// dimensions are the weighted average of the natural dimensions of the images that have some. The percentages that are
    /// omitted share what the other ones leave of 100%; percentages over 100% in total are scaled down to 100%, and a total
    /// under 100% leaves the result partially transparent.
    /// </summary>
    private sealed class CrossFadeImage : PaintedImage
    {
        private readonly List<(PaintedImage Image, double Weight)> _images;

        private CrossFadeImage(List<(PaintedImage Image, double Weight)> images)
        {
            _images = images;
            NaturalWidth = Average(static image => image.NaturalWidth);
            NaturalHeight = Average(static image => image.NaturalHeight);
        }

        public static CrossFadeImage? Create(Document document, ComputedStyle style, CssFunction function, Painter? painter)
        {
            var images = new List<(PaintedImage? Image, double? Weight)>();
            double specified = 0;
            var omitted = 0;
            foreach (var argument in ValueMatcher.SplitAtCommas(function.Value))
            {
                CssComponentValue? value = null;
                double? percentage = null;
                foreach (var component in ValueMatcher.RemoveWhitespace(argument))
                {
                    if (component is CssPreservedToken { Type: CssTokenType.Percentage } token)
                    {
                        percentage = Math.Clamp(token.Token.NumericValue, 0, 100);
                    }
                    else
                    {
                        value = component;
                    }
                }

                if (value is null)
                    return null;

                // An image that cannot be drawn (e.g. it is not loaded) keeps its share of the result, which stays transparent
                PaintedImage? image = ValueMatcher.IsColor(value)
                    ? (Painter.ResolveColor(style, value) is { } color ? new ColorImage(color) : null)
                    : Resolve(document, style, value, painter);
                images.Add((image, percentage));
                specified += percentage ?? 0;
                omitted += percentage is null ? 1 : 0;
            }

            if (images.Count == 0)
                return null;

            var share = omitted > 0 ? Math.Max(0, 100 - specified) / omitted : 0;
            var total = specified + (share * omitted);
            var scale = total > 100 ? 1 / total : 0.01;
            var weighted = new List<(PaintedImage Image, double Weight)>(images.Count);
            foreach (var (image, weight) in images)
            {
                if (image is not null && (weight ?? share) * scale is > 0 and var normalized)
                {
                    weighted.Add((image, normalized));
                }
            }

            return weighted.Count == 0 ? null : new CrossFadeImage(weighted);
        }

        /// <summary>
        /// Returns the color of a cross-fade of colors only (the sum of the premultiplied colors multiplied by their weights),
        /// or null when one of the images is not a color: the colors are mixed exactly, not as layers of 8-bit pixels.
        /// </summary>
        public DisplayColor? GetColor()
        {
            float r = 0, g = 0, b = 0, a = 0;
            foreach (var (image, weight) in _images)
            {
                if (image is not ColorImage { Color: var color })
                    return null;

                var alpha = color.A * (float)weight;
                r += color.R * alpha;
                g += color.G * alpha;
                b += color.B * alpha;
                a += alpha;
            }

            return a > 0 ? new DisplayColor(r / a, g / a, b / a, Math.Min(a, 1)) : new DisplayColor(0, 0, 0, 0);
        }

        public override double? NaturalWidth { get; }

        public override double? NaturalHeight { get; }

        private double? Average(Func<PaintedImage, double?> getDimension)
        {
            double sum = 0;
            double weights = 0;
            foreach (var (image, weight) in _images)
            {
                if (getDimension(image) is { } dimension)
                {
                    sum += dimension * weight;
                    weights += weight;
                }
            }

            return weights > 0 ? sum / weights : null;
        }

        public override DisplayShader? CreateShader(PhysicalSize size)
        {
            if (!(size.Width > 0) || !(size.Height > 0))
                return null;

            // The sum of the images multiplied by their weights: each one is a layer added (plus-lighter) to an isolated group
            var builder = new DisplayListBuilder();
            var rect = new PhysicalRect(0, 0, size.Width, size.Height);
            builder.Add(new ClipRectItem(rect));
            foreach (var (image, weight) in _images)
            {
                builder.SaveLayer(new SaveLayerItem(weight, BlendMode.PlusLighter, Filters: null));
                image.Draw(builder, rect, source: null);
                builder.Restore();
            }

            return new DisplayListShader(builder.ToDisplayList(size), size);
        }
    }

    /// <summary>
    /// A <c>paint()</c> image (https://drafts.css-houdini.org/css-paint-api-1/#paint-notation): no natural dimensions. The
    /// paint callback of its class draws it for each concrete object size (https://drafts.css-houdini.org/css-paint-api-1/#draw-a-paint-image),
    /// in CSS pixels: the recorded drawing operations are scaled by the zoom of the element. An image that cannot be drawn
    /// (a paint that is not registered, arguments that do not match, an exception) is an invalid image: nothing is painted.
    /// </summary>
    private sealed class PaintWorkletImage(PaintWorklet worklet, ComputedStyle style, CssFunction function) : PaintedImage
    {
        public static PaintWorkletImage? Create(Document document, ComputedStyle style, CssFunction function)
        {
            // The paints are registered by scripts: there is no paint worklet without script engine
            if (document.RelevantWindow?.ScriptEngine is null || !document.EnvironmentOptions!.EffectiveFeatures.PaintWorkletEnabled)
                return null;

            var worklet = document.PaintWorkletCore;
            worklet.HasPaintImages = true;
            return new PaintWorkletImage(worklet, style, function);
        }

        public override DisplayShader? CreateShader(PhysicalSize size)
        {
            var zoom = style.Layout.EffectiveZoom;
            if (!(zoom > 0))
            {
                zoom = 1;
            }

            var paintSize = new PhysicalSize(size.Width / zoom, size.Height / zoom);
            if (worklet.DrawPaintImage(style, function, paintSize.Width, paintSize.Height) is not { } list)
                return null;

            return new DisplayListShader(list, paintSize) { LocalTransform = zoom == 1 ? null : DisplayMatrix.Scale(zoom, zoom), IsCanvasRecording = true };
        }
    }

    /// <summary>A gradient (https://drafts.csswg.org/css-images-4/#gradients): no natural dimensions.</summary>
    private sealed class GradientImage(CssGradient gradient) : PaintedImage
    {
        public override DisplayShader? CreateShader(PhysicalSize size) => gradient.CreateShader(size);
    }

    /// <summary>A solid color image (<c>image(color)</c>): no natural dimensions.</summary>
    private sealed class ColorImage(DisplayColor color) : PaintedImage
    {
        public DisplayColor Color => color;

        public override DisplayShader? CreateShader(PhysicalSize size)
            => color.IsTransparent ? null : new LinearGradientShader(PhysicalOffset.Zero, new PhysicalOffset(1, 0), [new GradientStop(0, color), new GradientStop(1, color)]);

        public override void Draw(DisplayListBuilder builder, PhysicalRect destination, Node? source)
        {
            if (!destination.IsEmpty && !color.IsTransparent)
            {
                builder.Add(new DrawRectItem(destination, new DisplayPaint(color)) { Source = source });
            }
        }
    }
}
