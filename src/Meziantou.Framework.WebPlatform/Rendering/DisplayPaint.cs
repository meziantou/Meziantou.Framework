using System.Globalization;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Values;

namespace Meziantou.Framework.WebPlatform.Rendering;

/// <summary>
/// A color of the display list: the components of an sRGB color from 0 to 1, not premultiplied. The CSS colors are gamut
/// mapped to the gamut of the surface they are painted on (<see cref="FromCss(CssColor, RenderingColorSpace)"/>): the components of the colors of a Display
/// P3 surface that are outside of the sRGB gamut (e.g. <c>color(display-p3 0 1 0)</c>) are outside of [0, 1] (extended sRGB).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct DisplayColor(float R, float G, float B, float A)
{
    /// <summary>Transparent black.</summary>
    public static DisplayColor Transparent => default;

    /// <summary>Opaque black.</summary>
    public static DisplayColor Black { get; } = new(0, 0, 0, 1);

    /// <summary>Opaque white.</summary>
    public static DisplayColor White { get; } = new(1, 1, 1, 1);

    /// <summary>Whether the color is fully transparent: painting it has no effect with the default compositing.</summary>
    public bool IsTransparent => A <= 0;

    /// <summary>Whether the color is fully opaque.</summary>
    public bool IsOpaque => A >= 1;

    /// <summary>Creates an opaque color from 8-bit sRGB components.</summary>
    public static DisplayColor FromRgb(byte r, byte g, byte b) => new(r / 255f, g / 255f, b / 255f, 1);

    /// <summary>Returns the color with its alpha multiplied by a factor (e.g. <c>fill-opacity</c>).</summary>
    public DisplayColor MultiplyAlpha(double factor) => this with { A = (float)(A * Math.Clamp(factor, 0, 1)) };

    /// <summary>
    /// Converts a used color (https://drafts.csswg.org/css-color-4/#resolving-color-values, e.g. the resolved value of a color
    /// property) to a display color, or returns null when it is not a color.
    /// </summary>
    /// <param name="color">The color.</param>
    /// <param name="gamut">
    /// The color space of the surface the color is painted on: the colors outside of its gamut are CSS gamut mapped
    /// (https://drafts.csswg.org/css-color-4/#css-gamut-mapping), as the surfaces have 8-bit components.
    /// </param>
    public static DisplayColor? FromCss(CssColor color, RenderingColorSpace gamut = RenderingColorSpace.Srgb)
    {
        // The legacy sRGB colors (rgb(), hsl() and hwb() without none) are 8-bit colors whose components are clamped to
        // [0, 255] (https://drafts.csswg.org/css-color-4/#css-serialization-of-srgb), as in the browsers: e.g.
        // hsl(120, 200%, 50%) is lime
        if (color.LegacyRgb)
            return new DisplayColor((float)Math.Clamp((color.Channels[0] ?? 0) / 255, 0, 1), (float)Math.Clamp((color.Channels[1] ?? 0) / 255, 0, 1), (float)Math.Clamp((color.Channels[2] ?? 0) / 255, 0, 1), (float)Math.Clamp(color.Alpha ?? 0, 0, 1));

        if (CssColorConversion.FromColor(color) is not { } components)
            return null;

        // https://drafts.csswg.org/css-color-4/#missing: the missing components (none) are zero when the color is rendered,
        // the alpha included
        return FromCss(components, gamut, defaultAlpha: 0);
    }

    /// <summary>Converts a CSS color to extended sRGB without gamut mapping, for floating-point canvases.</summary>
    internal static DisplayColor? FromCssUnmapped(CssColor color)
    {
        if (color.LegacyRgb)
            return FromCss(color);

        if (CssColorConversion.FromColor(color) is not { } components)
            return null;

        var channels = CssColorConversion.FromXyz("srgb", CssColorConversion.ToXyz(components.Space, [components.Channels[0] ?? 0, components.Channels[1] ?? 0, components.Channels[2] ?? 0]));
        return new DisplayColor((float)channels[0], (float)channels[1], (float)channels[2], (float)Math.Clamp(components.Alpha ?? 0, 0, 1));
    }

    /// <summary>Converts the components of a color to a display color, CSS gamut mapped to the gamut of a surface (<see cref="FromCss(CssColor, RenderingColorSpace)"/>).</summary>
    internal static DisplayColor FromCss(CssColorComponents color, RenderingColorSpace gamut, double defaultAlpha)
    {
        double[] channels = [color.Channels[0] ?? 0, color.Channels[1] ?? 0, color.Channels[2] ?? 0];
        var alpha = (float)Math.Clamp(color.Alpha ?? defaultAlpha, 0, 1);
        if (gamut == RenderingColorSpace.DisplayP3)
        {
            var p3 = CssColorConversion.GamutMap(color.Space, channels, "display-p3");
            var srgb = CssColorConversion.FromXyz("srgb", CssColorConversion.ToXyz("display-p3", p3));
            return new DisplayColor((float)srgb[0], (float)srgb[1], (float)srgb[2], alpha);
        }

        var rgb = CssColorConversion.GamutMap(color.Space, channels, "srgb");
        return new DisplayColor((float)rgb[0], (float)rgb[1], (float)rgb[2], alpha);
    }

    /// <summary>Parses a used color serialized by the style engine (e.g. <c>rgb(255, 0, 0)</c>), or returns null when it is not a color.</summary>
    public static DisplayColor? Parse(string value)
    {
        switch (value)
        {
            case "transparent" or "rgba(0, 0, 0, 0)":
                return Transparent;

            case "rgb(0, 0, 0)":
                return Black;

            case "rgb(255, 255, 255)":
                return White;
        }

        return CssValueComputer.ParseCssColorValue(value) is { } color ? FromCss(color) : null;
    }

    /// <summary>Returns the color as <c>#rrggbb</c>, or <c>#rrggbbaa</c> when it is not opaque (the components are clamped and rounded to 8 bits).</summary>
    public override string ToString()
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"#{ToByte(R):x2}{ToByte(G):x2}{ToByte(B):x2}");
        return IsOpaque ? text : string.Create(CultureInfo.InvariantCulture, $"{text}{ToByte(A):x2}");

        static int ToByte(float value) => (int)Math.Round(Math.Clamp(value, 0, 1) * 255, MidpointRounding.AwayFromZero);
    }
}

/// <summary>Whether a shape is filled or stroked.</summary>
internal enum PaintStyle : byte
{
    /// <summary>The interior of the shape is painted (with the fill rule of its path).</summary>
    Fill,

    /// <summary>The outline of the shape is painted with the <see cref="DisplayPaint.Stroke"/> properties.</summary>
    Stroke,
}

/// <summary>
/// The compositing and blending operators (https://drafts.fxtf.org/compositing-1/): the Porter-Duff operators of
/// <c>globalCompositeOperation</c> and the blend modes of <c>mix-blend-mode</c>, <c>background-blend-mode</c> and the canvas.
/// </summary>
internal enum BlendMode : byte
{
    /// <summary>source-over (the default).</summary>
    Normal,

    /// <summary>copy.</summary>
    Copy,

    /// <summary>clear (the destination is cleared where the source is drawn).</summary>
    Clear,

    /// <summary>source-in.</summary>
    SourceIn,

    /// <summary>source-out.</summary>
    SourceOut,

    /// <summary>source-atop.</summary>
    SourceAtop,

    /// <summary>destination-over.</summary>
    DestinationOver,

    /// <summary>destination-in (e.g. a mask layer).</summary>
    DestinationIn,

    /// <summary>destination-out.</summary>
    DestinationOut,

    /// <summary>destination-atop.</summary>
    DestinationAtop,

    /// <summary>xor.</summary>
    Xor,

    /// <summary>lighter (https://drafts.fxtf.org/compositing-1/#porterduffcompositingoperators_plus).</summary>
    Lighter,

    /// <summary>plus-lighter (https://drafts.fxtf.org/compositing-2/#porterduffcompositingoperators_plus_lighter).</summary>
    PlusLighter,

    /// <summary>multiply.</summary>
    Multiply,

    /// <summary>screen.</summary>
    Screen,

    /// <summary>overlay.</summary>
    Overlay,

    /// <summary>darken.</summary>
    Darken,

    /// <summary>lighten.</summary>
    Lighten,

    /// <summary>color-dodge.</summary>
    ColorDodge,

    /// <summary>color-burn.</summary>
    ColorBurn,

    /// <summary>hard-light.</summary>
    HardLight,

    /// <summary>soft-light.</summary>
    SoftLight,

    /// <summary>difference.</summary>
    Difference,

    /// <summary>exclusion.</summary>
    Exclusion,

    /// <summary>hue.</summary>
    Hue,

    /// <summary>saturation.</summary>
    Saturation,

    /// <summary>color.</summary>
    Color,

    /// <summary>luminosity.</summary>
    Luminosity,
}

/// <summary>The fill rules of the paths (https://svgwg.org/svg2-draft/painting.html#FillRuleProperty).</summary>
internal enum FillRule : byte
{
    /// <summary>nonzero.</summary>
    NonZero,

    /// <summary>evenodd.</summary>
    EvenOdd,
}

/// <summary>The shapes of the ends of the open subpaths of a stroke (https://svgwg.org/svg2-draft/painting.html#StrokeLinecapProperty).</summary>
internal enum LineCap : byte
{
    /// <summary>butt.</summary>
    Butt,

    /// <summary>round.</summary>
    Round,

    /// <summary>square.</summary>
    Square,
}

/// <summary>The shapes of the corners of a stroke (https://svgwg.org/svg2-draft/painting.html#StrokeLinejoinProperty).</summary>
internal enum LineJoin : byte
{
    /// <summary>miter.</summary>
    Miter,

    /// <summary>round.</summary>
    Round,

    /// <summary>bevel.</summary>
    Bevel,
}

/// <summary>The properties of a stroke.</summary>
/// <param name="Width">The width of the stroke (0 is a hairline of one device pixel).</param>
/// <param name="Cap">The shape of the ends of the open subpaths.</param>
/// <param name="Join">The shape of the corners.</param>
/// <param name="MiterLimit">The limit of the ratio of the miter length to the stroke width.</param>
/// <param name="Dashes">The lengths of the dashes and of the gaps (an even number of positive values), or null for a solid stroke.</param>
/// <param name="DashOffset">The distance into the dash pattern at the start of each subpath.</param>
internal sealed record StrokeStyle(double Width, LineCap Cap = LineCap.Butt, LineJoin Join = LineJoin.Miter, double MiterLimit = 4, double[]? Dashes = null, double DashOffset = 0);

/// <summary>
/// How an image is sampled when it is scaled (https://drafts.csswg.org/css-images-3/#the-image-rendering): the backend chooses
/// the filters of each quality.
/// </summary>
internal enum ImageSampling : byte
{
    /// <summary><c>auto</c>, <c>smooth</c>: a smooth interpolation (e.g. bilinear with mipmaps).</summary>
    Smooth,

    /// <summary><c>high-quality</c>: a higher quality interpolation (e.g. bicubic).</summary>
    HighQuality,

    /// <summary><c>pixelated</c>, <c>crisp-edges</c>: the nearest neighbor.</summary>
    Pixelated,
}

/// <summary>How a shader is extended outside of its area: the gradients outside of their stops, the images outside of their bounds.</summary>
internal enum SpreadMode : byte
{
    /// <summary>The edge color is repeated (the edge pixels of an image).</summary>
    Pad,

    /// <summary>The pattern is repeated.</summary>
    Repeat,

    /// <summary>The pattern is repeated and mirrored.</summary>
    Reflect,

    /// <summary>Nothing is painted (transparent black).</summary>
    Decal,
}

/// <summary>A color stop of a gradient: its offset from 0 to 1 and its color.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct GradientStop(double Offset, DisplayColor Color);

/// <summary>
/// A shader of a <see cref="DisplayPaint"/>: the color of each point of a painted shape (a gradient, an image pattern), in the
/// coordinates of the display list where the shape is drawn, transformed by <see cref="LocalTransform"/>.
/// </summary>
internal abstract record DisplayShader
{
    /// <summary>The transform from the coordinates of the shader to the coordinates of the shape, or null for the identity.</summary>
    public DisplayMatrix? LocalTransform { get; init; }
}

/// <summary>
/// A gradient (https://drafts.csswg.org/css-images-3/#gradients, https://html.spec.whatwg.org/multipage/canvas.html#canvasgradient):
/// the colors are interpolated between the stops in sRGB, premultiplied by alpha when <see cref="InterpolatesPremultiplied"/>
/// is set (the other interpolation color spaces of CSS are expanded into more stops by the painting).
/// </summary>
internal abstract record GradientShader(GradientStop[] Stops, SpreadMode Spread) : DisplayShader
{
    /// <summary>Whether the colors are interpolated premultiplied by alpha (https://drafts.csswg.org/css-color-4/#interpolation-alpha).</summary>
    public bool InterpolatesPremultiplied { get; init; } = true;
}

/// <summary>A linear gradient from <paramref name="Start"/> (offset 0) to <paramref name="End"/> (offset 1).</summary>
internal sealed record LinearGradientShader(PhysicalOffset Start, PhysicalOffset End, GradientStop[] Stops, SpreadMode Spread = SpreadMode.Pad) : GradientShader(Stops, Spread);

/// <summary>
/// A radial gradient between two circles (https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createradialgradient):
/// the offset 0 is the start circle, 1 the end circle. The elliptical gradients of CSS are circles scaled by <see cref="DisplayShader.LocalTransform"/>.
/// </summary>
internal sealed record RadialGradientShader(PhysicalOffset StartCenter, double StartRadius, PhysicalOffset EndCenter, double EndRadius, GradientStop[] Stops, SpreadMode Spread = SpreadMode.Pad) : GradientShader(Stops, Spread);

/// <summary>
/// A conic gradient around <paramref name="Center"/> (https://drafts.csswg.org/css-images-4/#conic-gradients,
/// https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createconicgradient): the offset 0 is at
/// <paramref name="StartAngle"/> (in radians, clockwise from the positive x axis), the offsets increase clockwise.
/// </summary>
internal sealed record ConicGradientShader(PhysicalOffset Center, double StartAngle, GradientStop[] Stops, SpreadMode Spread = SpreadMode.Pad) : GradientShader(Stops, Spread);

/// <summary>
/// An image pattern (the tiles of a background image, a canvas pattern): the image is drawn with its top-left corner at the
/// origin, one CSS px per image pixel, transformed by <see cref="DisplayShader.LocalTransform"/>, and extended in each axis.
/// </summary>
internal sealed record ImageShader(PaintImage Image, SpreadMode SpreadX, SpreadMode SpreadY, ImageSampling Sampling = ImageSampling.Smooth) : DisplayShader
{
    /// <summary>
    /// The area of the image that is drawn (in image pixels), or null for the whole image: the image is cropped to it, at the
    /// same position, and extended from its edges. The pixels outside of the area are never sampled, so a scaled slice of an
    /// image (a slice of a border image) has no color of the pixels next to it at its edges.
    /// </summary>
    public PhysicalRect? SourceRect { get; init; }
}

/// <summary>
/// The image of a shader in a rectangle from (0, 0) to <paramref name="Size"/>, filtered (the <c>filter()</c> image function,
/// https://drafts.fxtf.org/filter-effects/#FilterCSSImageValue): transparent outside of the rectangle.
/// </summary>
internal sealed record FilteredShader(DisplayShader Content, PhysicalSize Size, DisplayFilter[] Filters) : DisplayShader;

/// <summary>
/// The drawing of a display list in a rectangle from (0, 0) to <paramref name="Size"/> (an SVG image painted by its document, the
/// tile of an SVG pattern, https://svgwg.org/svg2-draft/pservers.html#PatternElement): transparent outside of the rectangle.
/// </summary>
internal sealed record DisplayListShader(DisplayList Content, PhysicalSize Size) : DisplayShader
{
    /// <summary>
    /// Whether the display list is the drawing of a canvas (a paint worklet, https://drafts.css-houdini.org/css-paint-api-1/): it
    /// is rasterized into a bitmap of the resolution of the device, with the precision of the bitmaps of the canvases.
    /// </summary>
    public bool IsCanvasRecording { get; init; }
}

/// <summary>
/// A pattern of tiles (the tiles of a background layer, https://drafts.csswg.org/css-backgrounds/#background-repeat): the
/// <paramref name="Content"/> shader fills the tile from (0, 0) to <paramref name="TileSize"/>, and the tile is repeated every
/// <paramref name="TileSize"/> + <paramref name="Spacing"/> in the axes whose spread mode is <see cref="SpreadMode.Repeat"/>
/// (the spacing is transparent; <see cref="SpreadMode.Decal"/> paints a single tile in that axis), transformed by <see cref="DisplayShader.LocalTransform"/>.
/// </summary>
internal sealed record TiledShader(DisplayShader Content, PhysicalSize TileSize, PhysicalSize Spacing, SpreadMode SpreadX, SpreadMode SpreadY) : DisplayShader;

/// <summary>
/// How a shape, a text or an image is painted: a color or a shader, filled or stroked, composited with a blend mode.
/// </summary>
internal sealed record DisplayPaint
{
    /// <summary>Creates a paint that fills with a color.</summary>
    public DisplayPaint(DisplayColor color)
    {
        Color = color;
    }

    /// <summary>The color, or the alpha the shader is multiplied by (only the alpha of the color is used with a shader).</summary>
    public DisplayColor Color { get; init; }

    /// <summary>The shader, or null for the solid <see cref="Color"/>.</summary>
    public DisplayShader? Shader { get; init; }

    /// <summary>Whether the shapes are filled or stroked.</summary>
    public PaintStyle Style { get; init; }

    /// <summary>The properties of the stroke when <see cref="Style"/> is <see cref="PaintStyle.Stroke"/>.</summary>
    public StrokeStyle? Stroke { get; init; }

    /// <summary>The compositing and blending operator.</summary>
    public BlendMode BlendMode { get; init; }

    /// <summary>Whether the edges are antialiased.</summary>
    public bool AntiAlias { get; init; } = true;

    /// <summary>
    /// The standard deviation of a Gaussian blur of the coverage of the shape (a blurred shadow, https://drafts.csswg.org/css-backgrounds/#shadow-blur),
    /// or 0.
    /// </summary>
    public double BlurSigma { get; init; }

    /// <summary>Creates a paint that strokes with a color.</summary>
    public static DisplayPaint ForStroke(DisplayColor color, StrokeStyle stroke) => new(color) { Style = PaintStyle.Stroke, Stroke = stroke };
}

/// <summary>
/// A filter effect of a layer (https://drafts.fxtf.org/filter-effects/#FilterProperty): the CSS filter functions are blurs,
/// drop shadows and color matrices; the filters of the SVG filter elements are graphs of primitives (#376, #377).
/// </summary>
internal abstract record DisplayFilter;

/// <summary>A Gaussian blur (https://drafts.fxtf.org/filter-effects/#funcdef-filter-blur), with a standard deviation per axis.</summary>
internal sealed record BlurFilter(double DeviationX, double DeviationY) : DisplayFilter;

/// <summary>A drop shadow (https://drafts.fxtf.org/filter-effects/#funcdef-filter-drop-shadow): the alpha of the input offset, blurred and colored, below the input.</summary>
internal sealed record DropShadowFilter(PhysicalOffset Offset, double Deviation, DisplayColor Color) : DisplayFilter
{
    /// <summary>
    /// Whether only the shadow is drawn, without the input over it (e.g. the shadows of the canvas, composited separately from
    /// the shapes, https://html.spec.whatwg.org/multipage/canvas.html#drawing-model).
    /// </summary>
    public bool ShadowOnly { get; init; }
}

/// <summary>
/// A color matrix (https://drafts.fxtf.org/filter-effects/#feColorMatrixElement): 4 rows of 5 values that compute R, G, B, A
/// (not premultiplied, from 0 to 1) from R, G, B, A and 1. The functions brightness(), contrast(), grayscale(), hue-rotate(),
/// invert(), opacity(), saturate() and sepia() are color matrices.
/// </summary>
internal sealed record ColorMatrixFilter(float[] Matrix) : DisplayFilter;
