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
[IdlInterface("CanvasGradient", "https://html.spec.whatwg.org/multipage/canvas.html#canvasgradient")]
public sealed class CanvasGradient
{
    private readonly CanvasGradientKind _kind;
    private readonly double[] _geometry;
    private readonly List<GradientStop> _stops = [];

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
    }

    /// <summary>
    /// Returns the shader of the gradient (https://html.spec.whatwg.org/multipage/canvas.html#interpolation): the colors are
    /// interpolated without premultiplying the alpha. Returns null when nothing is painted (no color stop, or a degenerate gradient).
    /// </summary>
    internal DisplayShader? CreateShader()
    {
        if (_stops.Count == 0)
            return null;

        var stops = _stops.ToArray();
        var g = _geometry;
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
}
