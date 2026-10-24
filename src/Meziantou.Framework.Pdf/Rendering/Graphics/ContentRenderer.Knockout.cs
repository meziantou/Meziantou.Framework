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

        _element = opacity;
        try
        {
            if (_state.SoftMask is { } softMask && _depth < MaxDepth)
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
    /// Shows a text as an element of the knockout group the content is drawn in: the glyphs of a text-showing operator
    /// are one object (the text knockout of 9.3.8, at the granularity of the operator), with the alpha of the fill or of
    /// the stroke as its opacity; the modes that fill and stroke composite the stroke over the fill, as a path does.
    /// </summary>
    private void PaintTextElement(DrawingCanvas canvas, Action<DrawingCanvas> show)
    {
        var opacity = _state.TextRenderingMode switch
        {
            0 or 4 => _state.FillAlpha,
            1 or 5 => _state.StrokeAlpha,
            2 or 6 => 1,
            _ => (double?)null,
        };
        if (opacity is { } value)
        {
            PaintElement(canvas, value, show);
        }
        else
        {
            show(canvas);
        }
    }
}
