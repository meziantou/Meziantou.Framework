using System.Diagnostics.CodeAnalysis;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Rendering;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>The kinds of the gradients of a canvas.</summary>
internal enum CanvasGradientKind
{
    Linear,
    Radial,
    Conic,
}

/// <summary>
/// A gradient of a canvas (https://html.spec.whatwg.org/multipage/canvas.html#canvasgradient): created by
/// <c>createLinearGradient()</c>, <c>createRadialGradient()</c> and <c>createConicGradient()</c>, it is a fill or stroke style
/// whose geometry is in the coordinates of the drawing operations that use it.
/// </summary>
/// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#canvasgradient"/>
[IdlInterface("CanvasGradient", "https://html.spec.whatwg.org/multipage/canvas.html#canvasgradient")]
public sealed class CanvasGradient
{
    // The proposal of the interpolation methods of the canvas gradients (the IDL members need a URL with a fragment)
    private const string ColorInterpolationHref = "https://github.com/whatwg/html/pull/10986#dom-canvasgradient-colorinterpolationmethod";
    private const string HueInterpolationHref = "https://github.com/whatwg/html/pull/10986#dom-canvasgradient-hueinterpolationmethod";

    private readonly CanvasGradientKind _kind;
    private readonly double[] _geometry;
    private readonly List<GradientStop> _stops = [];

    // The colors of the stops (in the order of _stops), interpolated in another color space than sRGB
    private readonly List<CssColorComponents?> _stopColors = [];

    // Whether all the stops are legacy sRGB colors (https://drafts.csswg.org/css-color-4/#legacy-color-syntax)
    private bool _allLegacy = true;

    // The color space set by colorInterpolationMethod, or null when it is not set
    private string? _colorInterpolationMethod;

    // The hue interpolation method set by hueInterpolationMethod
    private string _hueInterpolationMethod = "shorter";

    internal CanvasGradient(CanvasGradientKind kind, double[] geometry, CanvasRenderingContext2DBase context)
    {
        _kind = kind;
        _geometry = geometry;
        Context = context;
    }

    /// <summary>The context that created the gradient.</summary>
    internal CanvasRenderingContext2DBase Context { get; }

    /// <summary>
    /// Adds a color stop (https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasgradient-addcolorstop): a stop at the same
    /// offset as existing stops is placed after them.
    /// </summary>
    /// <param name="offset">The offset of the stop, from 0 to 1.</param>
    /// <param name="color">A CSS color (<c>currentcolor</c> is opaque black).</param>
    /// <exception cref="DomException">An <c>IndexSizeError</c> when the offset is out of range, a <c>SyntaxError</c> when the color cannot be parsed.</exception>
    /// <seealso href="https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasgradient-addcolorstop"/>
    [IdlMember("addColorStop", "https://html.spec.whatwg.org/multipage/canvas.html#dom-canvasgradient-addcolorstop")]
    public void AddColorStop(double offset, string color)
    {
        ArgumentNullException.ThrowIfNull(color);
        if (!(offset >= 0 && offset <= 1))
            throw new DomException("The offset must be between 0 and 1", "IndexSizeError");

        if (CanvasColors.Parse(color, element: null, Context.Bitmap.Options.ColorSpace) is not { } parsed)
            throw new DomException("The color cannot be parsed", "SyntaxError");

        var index = _stops.Count;
        while (index > 0 && _stops[index - 1].Offset > offset)
        {
            index--;
        }

        _stops.Insert(index, new GradientStop(offset, parsed.Color));
        _stopColors.Insert(index, CssColorConversion.FromColor(parsed.CssColor));
        _allLegacy &= parsed.CssColor.LegacyRgb || parsed.CssColor.Function is "hsl" or "hwb";
    }

    /// <summary>
    /// Gets or sets the color space the colors of the gradient are interpolated in (https://github.com/whatwg/html/pull/10986):
    /// <c>srgb</c>, <c>srgb-linear</c>, <c>display-p3</c>, <c>display-p3-linear</c>, <c>a98-rgb</c>, <c>prophoto-rgb</c>,
    /// <c>rec2020</c>, <c>lab</c>, <c>oklab</c>, <c>xyz</c>, <c>xyz-d50</c>, <c>xyz-d65</c>, or the polar color spaces
    /// <c>hsl</c>, <c>hwb</c>, <c>lch</c> and <c>oklch</c>, whose hues are interpolated with <see cref="HueInterpolationMethod"/>.
    /// The other values are ignored. When it is not set, the value is <c>srgb</c>, but the colors are interpolated as in the
    /// CSS gradients (as in the browsers): in sRGB when all the stops are legacy sRGB colors, else in Oklab.
    /// </summary>
    /// <seealso href="https://github.com/whatwg/html/pull/10986#dom-canvasgradient-colorinterpolationmethod"/>
    [IdlMember("colorInterpolationMethod", ColorInterpolationHref)]
    [Experimental(ExperimentalApis.CanvasGradientInterpolation, UrlFormat = ExperimentalApis.UrlFormat)]
    public string ColorInterpolationMethod
    {
        get => _colorInterpolationMethod ?? "srgb";
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value is "srgb" or "hsl" or "hwb" or "srgb-linear" or "display-p3" or "display-p3-linear" or "a98-rgb" or "prophoto-rgb" or "rec2020" or "lab" or "oklab" or "lch" or "oklch" or "xyz" or "xyz-d50" or "xyz-d65")
            {
                _colorInterpolationMethod = value;
            }
        }
    }

    /// <summary>
    /// Gets or sets how the hues are interpolated in the polar color spaces of <see cref="ColorInterpolationMethod"/>
    /// (https://drafts.csswg.org/css-color-4/#hue-interpolation): <c>shorter</c> (the default), <c>longer</c>,
    /// <c>increasing</c> or <c>decreasing</c>. The other values are ignored.
    /// </summary>
    /// <seealso href="https://github.com/whatwg/html/pull/10986#dom-canvasgradient-hueinterpolationmethod"/>
    [IdlMember("hueInterpolationMethod", HueInterpolationHref)]
    [Experimental(ExperimentalApis.CanvasGradientInterpolation, UrlFormat = ExperimentalApis.UrlFormat)]
    public string HueInterpolationMethod
    {
        get => _hueInterpolationMethod;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            if (value is "shorter" or "longer" or "increasing" or "decreasing")
            {
                _hueInterpolationMethod = value;
            }
        }
    }

    /// <summary>
    /// Returns the shader of the gradient (https://html.spec.whatwg.org/multipage/canvas.html#interpolation): the colors are
    /// interpolated without premultiplying the alpha. Returns null when nothing is painted (no color stop, or a degenerate gradient).
    /// </summary>
    internal DisplayShader? CreateShader()
    {
        if (_stops.Count == 0)
            return null;

        var g = _geometry;
        var stops = GetInterpolatedStops();
        switch (_kind)
        {
            case CanvasGradientKind.Linear:
                // https://html.spec.whatwg.org/multipage/canvas.html#dom-context-2d-createlineargradient: "if x0 = x1 and y0 = y1, then the linear gradient must paint nothing"
                if (g[0] == g[2] && g[1] == g[3])
                    return null;

                return new LinearGradientShader(new PhysicalOffset(g[0], g[1]), new PhysicalOffset(g[2], g[3]), stops) { InterpolatesPremultiplied = false };

            case CanvasGradientKind.Radial:
                // "If x0 = x1 and y0 = y1 and r0 = r1, then the radial gradient must paint nothing"
                if (g[0] == g[3] && g[1] == g[4] && g[2] == g[5])
                    return null;

                return new RadialGradientShader(new PhysicalOffset(g[0], g[1]), g[2], new PhysicalOffset(g[3], g[4]), g[5], stops) { InterpolatesPremultiplied = false };

            default:
                return new ConicGradientShader(new PhysicalOffset(g[1], g[2]), g[0], stops) { InterpolatesPremultiplied = false };
        }
    }

    /// <summary>
    /// Returns the stops of the shader: the colors interpolated in another color space than sRGB are sampled into more stops,
    /// as the colors of the CSS gradients (the shaders interpolate the colors in sRGB). The colors are not premultiplied.
    /// </summary>
    private GradientStop[] GetInterpolatedStops()
    {
        var space = _colorInterpolationMethod switch
        {
            null => _allLegacy ? "srgb" : "oklab",
            "xyz" => "xyz-d65",
            var value => value,
        };

        if (space == "srgb" || _stopColors.Contains(null))
            return [.. _stops];

        var hueMethod = _hueInterpolationMethod switch
        {
            "longer" => CssColorMix.HueMethod.Longer,
            "increasing" => CssColorMix.HueMethod.Increasing,
            "decreasing" => CssColorMix.HueMethod.Decreasing,
            _ => CssColorMix.HueMethod.Shorter,
        };

        // The number of samples depends on the length the gradient is painted over: the size of the canvas, or the length of
        // the gradient when it is larger
        var g = _geometry;
        var length = _kind switch
        {
            CanvasGradientKind.Linear => Math.Sqrt(((g[2] - g[0]) * (g[2] - g[0])) + ((g[3] - g[1]) * (g[3] - g[1]))),
            CanvasGradientKind.Radial => Math.Max(g[2], g[5]) + Math.Sqrt(((g[3] - g[0]) * (g[3] - g[0])) + ((g[4] - g[1]) * (g[4] - g[1]))),
            _ => 0,
        };
        var bitmap = Context.Bitmap;
        var extent = Math.Min(Math.Max(double.IsFinite(length) ? length : 0, Math.PI * Math.Max(bitmap.Width, bitmap.Height)), 1e6);
        var positions = new double?[_stops.Count];
        for (var i = 0; i < positions.Length; i++)
        {
            positions[i] = _stops[i].Offset;
        }

        var (offsets, colors) = CssGradient.InterpolateStops(_stopColors, positions, space, hueMethod, extent, bitmap.Options.ColorSpace, premultiplied: false);
        var result = new GradientStop[offsets.Count];
        for (var i = 0; i < result.Length; i++)
        {
            result[i] = new GradientStop(offsets[i], colors[i]);
        }

        return result;
    }
}
