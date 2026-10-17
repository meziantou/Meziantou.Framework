namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The fill rules of the paths of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasfillrule).</summary>
[IdlInterface("CanvasFillRule", "https://html.spec.whatwg.org/multipage/canvas.html#canvasfillrule")]
public enum CanvasFillRule
{
    /// <summary>The non-zero winding rule.</summary>
    [IdlMember("nonzero", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillrule-nonzero")]
    NonZero,

    /// <summary>The even-odd rule.</summary>
    [IdlMember("evenodd", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fillrule-evenodd")]
    EvenOdd,
}

/// <summary>The shapes of the ends of the lines of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvaslinecap).</summary>
[IdlInterface("CanvasLineCap", "https://html.spec.whatwg.org/multipage/canvas.html#canvaslinecap")]
public enum CanvasLineCap
{
    /// <summary>The line ends at its end point.</summary>
    [IdlMember("butt", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap-butt")]
    Butt,

    /// <summary>A semicircle is added at the end of the line.</summary>
    [IdlMember("round", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap-round")]
    Round,

    /// <summary>A square is added at the end of the line.</summary>
    [IdlMember("square", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linecap-square")]
    Square,
}

/// <summary>The shapes of the corners of the lines of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvaslinejoin).</summary>
[IdlInterface("CanvasLineJoin", "https://html.spec.whatwg.org/multipage/canvas.html#canvaslinejoin")]
public enum CanvasLineJoin
{
    /// <summary>A rounded corner.</summary>
    [IdlMember("round", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin-round")]
    Round,

    /// <summary>A beveled corner.</summary>
    [IdlMember("bevel", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin-bevel")]
    Bevel,

    /// <summary>A sharp corner (limited by the miter limit).</summary>
    [IdlMember("miter", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-linejoin-miter")]
    Miter,
}

/// <summary>The horizontal alignments of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvastextalign).</summary>
[IdlInterface("CanvasTextAlign", "https://html.spec.whatwg.org/multipage/canvas.html#canvastextalign")]
public enum CanvasTextAlign
{
    /// <summary>The start of the text (left for left-to-right text).</summary>
    [IdlMember("start", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign-start")]
    Start,

    /// <summary>The end of the text (right for left-to-right text).</summary>
    [IdlMember("end", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign-end")]
    End,

    /// <summary>The left of the text.</summary>
    [IdlMember("left", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign-left")]
    Left,

    /// <summary>The right of the text.</summary>
    [IdlMember("right", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign-right")]
    Right,

    /// <summary>The center of the text.</summary>
    [IdlMember("center", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textalign-center")]
    Center,
}

/// <summary>The baselines of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvastextbaseline).</summary>
[IdlInterface("CanvasTextBaseline", "https://html.spec.whatwg.org/multipage/canvas.html#canvastextbaseline")]
public enum CanvasTextBaseline
{
    /// <summary>The top of the em square.</summary>
    [IdlMember("top", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-top")]
    Top,

    /// <summary>The hanging baseline.</summary>
    [IdlMember("hanging", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-hanging")]
    Hanging,

    /// <summary>The middle of the em square.</summary>
    [IdlMember("middle", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-middle")]
    Middle,

    /// <summary>The alphabetic baseline.</summary>
    [IdlMember("alphabetic", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-alphabetic")]
    Alphabetic,

    /// <summary>The ideographic-under baseline.</summary>
    [IdlMember("ideographic", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-ideographic")]
    Ideographic,

    /// <summary>The bottom of the em square.</summary>
    [IdlMember("bottom", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textbaseline-bottom")]
    Bottom,
}

/// <summary>The directions of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasdirection).</summary>
[IdlInterface("CanvasDirection", "https://html.spec.whatwg.org/multipage/canvas.html#canvasdirection")]
public enum CanvasDirection
{
    /// <summary>Left to right.</summary>
    [IdlMember("ltr", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction-ltr")]
    Ltr,

    /// <summary>Right to left.</summary>
    [IdlMember("rtl", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction-rtl")]
    Rtl,

    /// <summary>The direction of the canvas element (left to right for an <c>OffscreenCanvas</c>).</summary>
    [IdlMember("inherit", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-direction-inherit")]
    Inherit,
}

/// <summary>The kerning of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasfontkerning).</summary>
[IdlInterface("CanvasFontKerning", "https://html.spec.whatwg.org/multipage/canvas.html#canvasfontkerning")]
public enum CanvasFontKerning
{
    /// <summary>The kerning is applied (as <c>font-kerning: auto</c>).</summary>
    [IdlMember("auto", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontkerning-auto")]
    Auto,

    /// <summary>The kerning is applied.</summary>
    [IdlMember("normal", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontkerning-normal")]
    Normal,

    /// <summary>The kerning is not applied.</summary>
    [IdlMember("none", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontkerning-none")]
    None,
}

/// <summary>The widths of the fonts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasfontstretch).</summary>
[IdlInterface("CanvasFontStretch", "https://html.spec.whatwg.org/multipage/canvas.html#canvasfontstretch")]
public enum CanvasFontStretch
{
    /// <summary>50%.</summary>
    [IdlMember("ultra-condensed", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-ultra-condensed")]
    UltraCondensed,

    /// <summary>62.5%.</summary>
    [IdlMember("extra-condensed", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-extra-condensed")]
    ExtraCondensed,

    /// <summary>75%.</summary>
    [IdlMember("condensed", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-condensed")]
    Condensed,

    /// <summary>87.5%.</summary>
    [IdlMember("semi-condensed", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-semi-condensed")]
    SemiCondensed,

    /// <summary>100%.</summary>
    [IdlMember("normal", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-normal")]
    Normal,

    /// <summary>112.5%.</summary>
    [IdlMember("semi-expanded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-semi-expanded")]
    SemiExpanded,

    /// <summary>125%.</summary>
    [IdlMember("expanded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-expanded")]
    Expanded,

    /// <summary>150%.</summary>
    [IdlMember("extra-expanded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-extra-expanded")]
    ExtraExpanded,

    /// <summary>200%.</summary>
    [IdlMember("ultra-expanded", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontstretch-ultra-expanded")]
    UltraExpanded,
}

/// <summary>The capitals of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasfontvariantcaps).</summary>
[IdlInterface("CanvasFontVariantCaps", "https://html.spec.whatwg.org/multipage/canvas.html#canvasfontvariantcaps")]
public enum CanvasFontVariantCaps
{
    /// <summary>normal.</summary>
    [IdlMember("normal", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-normal")]
    Normal,

    /// <summary>small-caps.</summary>
    [IdlMember("small-caps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-small-caps")]
    SmallCaps,

    /// <summary>all-small-caps.</summary>
    [IdlMember("all-small-caps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-all-small-caps")]
    AllSmallCaps,

    /// <summary>petite-caps.</summary>
    [IdlMember("petite-caps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-petite-caps")]
    PetiteCaps,

    /// <summary>all-petite-caps.</summary>
    [IdlMember("all-petite-caps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-all-petite-caps")]
    AllPetiteCaps,

    /// <summary>unicase.</summary>
    [IdlMember("unicase", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-unicase")]
    Unicase,

    /// <summary>titling-caps.</summary>
    [IdlMember("titling-caps", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-fontvariantcaps-titling-caps")]
    TitlingCaps,
}

/// <summary>The rendering hints of the texts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvastextrendering).</summary>
[IdlInterface("CanvasTextRendering", "https://html.spec.whatwg.org/multipage/canvas.html#canvastextrendering")]
public enum CanvasTextRendering
{
    /// <summary>auto.</summary>
    [IdlMember("auto", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering-auto")]
    Auto,

    /// <summary>optimizeSpeed.</summary>
    [IdlMember("optimizeSpeed", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering-optimizespeed")]
    OptimizeSpeed,

    /// <summary>optimizeLegibility.</summary>
    [IdlMember("optimizeLegibility", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering-optimizelegibility")]
    OptimizeLegibility,

    /// <summary>geometricPrecision.</summary>
    [IdlMember("geometricPrecision", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-textrendering-geometricprecision")]
    GeometricPrecision,
}

/// <summary>The qualities of the image smoothing of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#imagesmoothingquality).</summary>
[IdlInterface("ImageSmoothingQuality", "https://html.spec.whatwg.org/multipage/canvas.html#imagesmoothingquality")]
public enum ImageSmoothingQuality
{
    /// <summary>A bilinear interpolation.</summary>
    [IdlMember("low", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality-low")]
    Low,

    /// <summary>A bilinear interpolation with mipmaps.</summary>
    [IdlMember("medium", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality-medium")]
    Medium,

    /// <summary>A bicubic interpolation.</summary>
    [IdlMember("high", "https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-imagesmoothingquality-high")]
    High,
}

/// <summary>The types of the components of the pixels of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvascolortype).</summary>
[IdlInterface("CanvasColorType", "https://html.spec.whatwg.org/multipage/canvas.html#canvascolortype")]
public enum CanvasColorType
{
    /// <summary>8-bit unsigned normalized components.</summary>
    [IdlMember("unorm8", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvascolortype-unorm8")]
    Unorm8,

    /// <summary>Half-precision floating-point components (the library stores 8-bit components).</summary>
    [IdlMember("float16", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvascolortype-float16")]
    Float16,
}

/// <summary>
/// The options of the rendering contexts of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasrenderingcontext2dsettings):
/// the second argument of <c>getContext()</c>. The <c>bitmaprenderer</c> contexts only use <see cref="Alpha"/>
/// (https://html.spec.whatwg.org/multipage/canvas.html#imagebitmaprenderingcontextsettings).
/// </summary>
[IdlInterface("CanvasRenderingContext2DSettings", "https://html.spec.whatwg.org/multipage/canvas.html#canvasrenderingcontext2dsettings")]
public sealed class CanvasRenderingContext2DSettings
{
    /// <summary>Gets whether the canvas has an alpha channel (an opaque canvas is initially opaque black).</summary>
    [IdlMember("alpha", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasrenderingcontext2dsettings-alpha")]
    public bool Alpha { get; init; } = true;

    /// <summary>Gets whether the canvas may be presented without synchronization with the event loop (no effect on the drawing).</summary>
    [IdlMember("desynchronized", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasrenderingcontext2dsettings-desynchronized")]
    public bool Desynchronized { get; init; }

    /// <summary>Gets the color space of the bitmap.</summary>
    [IdlMember("colorSpace", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasrenderingcontext2dsettings-colorspace")]
    public PredefinedColorSpace ColorSpace { get; init; }

    /// <summary>Gets the type of the components of the pixels.</summary>
    [IdlMember("colorType", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasrenderingcontext2dsettings-colortype")]
    public CanvasColorType ColorType { get; init; }

    /// <summary>Gets whether the pixels will be read often (a hint to keep the bitmap in memory).</summary>
    [IdlMember("willReadFrequently", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasrenderingcontext2dsettings-willreadfrequently")]
    public bool WillReadFrequently { get; init; }
}

/// <summary>The options of <c>OffscreenCanvas.convertToBlob()</c> (https://html.spec.whatwg.org/multipage/canvas.html#imageencodeoptions).</summary>
[IdlInterface("ImageEncodeOptions", "https://html.spec.whatwg.org/multipage/canvas.html#imageencodeoptions")]
public sealed class ImageEncodeOptions
{
    /// <summary>Gets the MIME type of the image file (PNG when the type is not supported).</summary>
    [IdlMember("type", "https://html.spec.whatwg.org/multipage/canvas.html#dom-imageencodeoptions-type")]
    public string Type { get; init; } = "image/png";

    /// <summary>Gets the quality from 0 to 1 of the lossy formats (JPEG, WebP), or null for the default quality.</summary>
    [IdlMember("quality", "https://html.spec.whatwg.org/multipage/canvas.html#dom-imageencodeoptions-quality")]
    public double? Quality { get; init; }
}

/// <summary>The types of the rendering contexts of an <see cref="OffscreenCanvas"/> (https://html.spec.whatwg.org/multipage/canvas.html#offscreenrenderingcontextid).</summary>
[IdlInterface("OffscreenRenderingContextId", "https://html.spec.whatwg.org/multipage/canvas.html#offscreenrenderingcontextid")]
public enum OffscreenRenderingContextId
{
    /// <summary>A 2D context (<see cref="OffscreenCanvasRenderingContext2D"/>).</summary>
    [IdlMember("2d", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreenrenderingcontextid-2d")]
    TwoD,

    /// <summary>An <see cref="ImageBitmapRenderingContext"/>.</summary>
    [IdlMember("bitmaprenderer", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreenrenderingcontextid-bitmaprenderer")]
    BitmapRenderer,

    /// <summary>A WebGL context (not supported).</summary>
    [IdlMember("webgl", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreenrenderingcontextid-webgl")]
    WebGL,

    /// <summary>A WebGL 2 context (not supported).</summary>
    [IdlMember("webgl2", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreenrenderingcontextid-webgl2")]
    WebGL2,

    /// <summary>A WebGPU context (not supported).</summary>
    [IdlMember("webgpu", "https://html.spec.whatwg.org/multipage/canvas.html#dom-offscreenrenderingcontextid-webgpu")]
    WebGpu,
}
