using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <content>
/// The elements of knockout groups (ISO 32000-2, 11.4.8): in the content of a knockout group, each painting operator,
/// image, shading, text-showing operator and transparency group is an element, composited with the initial backdrop of
/// the group, which replaces the elements before it in proportion to its shape. The element is drawn with an opacity
/// of 1 (<see cref="ElementAlpha"/>); its constant alpha and its soft mask are its opacity
/// (<see cref="DrawingCanvas.KnockoutElement(double, DrawingAction, DrawingAction?, MaskMode)"/>).
/// </content>
internal sealed partial class ContentRenderer
{
    // The opacity of the element of a knockout group being drawn, when one is
    private double? _element;

    // Whether a text-showing operator is being drawn, with the antialiasing of the text
    private bool _isDrawingText;

    /// <summary>Gets or initializes a value indicating whether the content is the content of a knockout group, directly or through forms that are not groups and Type 3 glyphs.</summary>
    public bool IsKnockoutContext { get; init; }

    /// <summary>Gets or initializes the opacity of the element of a knockout group the content is drawn in (a Type 3 glyph of a text-showing operator, for instance), or <see langword="null"/>.</summary>
    public double? Element
    {
        get => _element;
        init => _element = value;
    }

    /// <summary>Gets the alpha a drawing has in the element being drawn: its alpha divided by the opacity of the element, which applies it.</summary>
    private double ElementAlpha(double alpha) => _element is { } opacity ? (opacity > 0 ? Math.Min(1, alpha / opacity) : 1) : alpha;

    /// <summary>Draws an element of the knockout group the content is drawn in, with an opacity and the soft mask of the graphics state; draws it as it is elsewhere.</summary>
    private void PaintElement(DrawingCanvas canvas, double opacity, Action<DrawingCanvas> draw)
    {
        if (!IsKnockoutContext || _element is not null)
        {
            draw(canvas);
            return;
        }

        // Alpha is shape (11.3.7.2): the element knocks out in proportion to its constant alpha and its soft mask, which
        // are applied as it is drawn, and its opacity is 1
        if (_state.AlphaIsShape)
        {
            opacity = 1;
        }

        _element = opacity;
        try
        {
            if (_state.SoftMask is { } softMask && _depth < MaxDepth && !_state.AlphaIsShape)
            {
                var isLuminosity = softMask.Mask.GetName(PdfNames.S)?.Value != "Alpha";
                canvas.KnockoutElement(opacity, element => draw(element), mask => RenderSoftMask(mask, softMask.Mask, softMask.Matrix, isLuminosity), isLuminosity ? MaskMode.Luminance : MaskMode.Alpha);
            }
            else
            {
                canvas.KnockoutElement(opacity, element => draw(element));
            }
        }
        finally
        {
            _element = null;
        }
    }

    /// <summary>
    /// Shows a text, with the text knockout of the graphics state (9.3.8): when it is on, the glyphs of a text-showing
    /// operator are one object, whose glyphs knock out one another where they overlap (at the granularity of the
    /// operator); when it is off, each glyph is an object of its own. In a knockout group, the object is an element of the
    /// group, with the alpha of the fill or of the stroke as its opacity; the modes that fill and stroke composite the
    /// stroke over the fill, as a path does.
    /// </summary>
    private void PaintTextElement(DrawingCanvas canvas, Action<DrawingCanvas> show, PdfObject text)
    {
        // The antialiasing of the text, when it is not the one of the paths
        if (_cache.Options.TextAntialiasing != _cache.Options.PathAntialiasing && !_isDrawingText)
        {
            _isDrawingText = true;
            try
            {
                canvas.Antialiasing(_cache.Options.TextAntialiasing, target => PaintTextElement(target, show, text));
            }
            finally
            {
                _isDrawingText = false;
            }

            return;
        }

        if (!_state.TextKnockout)
        {
            show(canvas);
            return;
        }

        var opacity = _state.TextRenderingMode switch
        {
            0 or 4 => _state.FillAlpha,
            1 or 5 => _state.StrokeAlpha,
            2 or 6 => 1,
            _ => (double?)null,
        };
        if (opacity is not { } value)
        {
            show(canvas);
            return;
        }

        if (IsKnockoutContext && _element is null)
        {
            PaintElement(canvas, value, show);
            return;
        }

        // Elsewhere, the glyphs that fill or that stroke, with an opacity or a blend mode, are drawn opaque on a layer
        // composited as one object, so that the glyphs that overlap are composited once; a soft mask applies to each glyph
        var blend = _state.BlendMode;
        var paintsOneColor = _state.TextRenderingMode is 0 or 1 or 4 or 5;
        if (_element is not null || !paintsOneColor || (value >= 1 && blend == BlendMode.Normal) || _state.SoftMask is not null || ForcedColor is not null || !ShowsSeveralGlyphs(text))
        {
            show(canvas);
            return;
        }

        if (ToPixelBounds(GetClipBounds(), margin: 1) is not { } bounds)
            return;

        var (fillAlpha, strokeAlpha) = (_state.FillAlpha, _state.StrokeAlpha);
        _cache.Charge(((long)bounds.Width * bounds.Height / 1024) + 1);
        canvas.Clip(bounds, clipped => clipped.Layer(value, layer =>
        {
            _state.FillAlpha = 1;
            _state.StrokeAlpha = 1;
            _state.BlendMode = BlendMode.Normal;
            try
            {
                show(layer);
            }
            finally
            {
                _state.FillAlpha = fillAlpha;
                _state.StrokeAlpha = strokeAlpha;
                _state.BlendMode = blend;
            }
        }, CompositeMode.SourceOver, blend));
    }

    /// <summary>Indicates whether a string or the strings of an array show more than one glyph, which may overlap.</summary>
    private bool ShowsSeveralGlyphs(PdfObject text)
    {
        if (_state.Font is not { } font)
            return false;

        var count = 0;
        bool Count(ReadOnlySpan<byte> bytes)
        {
            while (bytes.Length > 0)
            {
                var (_, length) = font.Decoder.ReadCode(bytes);
                bytes = bytes[Math.Max(1, length)..];
                if (++count > 1)
                    return true;
            }

            return false;
        }

        return text switch
        {
            PdfString single => Count(single.Span),
            PdfArray array => array.Any(item => PdfReference.ResolveIfReference(item) is PdfString part && Count(part.Span)),
            _ => false,
        };
    }
}
