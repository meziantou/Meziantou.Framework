using Meziantou.Framework.Imaging;
using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Fonts;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

internal sealed partial class ContentRenderer
{
    private PdfMatrix _textMatrix = PdfMatrix.Identity;
    private PdfMatrix _lineMatrix = PdfMatrix.Identity;
    private List<VectorPath>? _textClip;

    private RenderFont? GetFont(PdfDictionary? resources, PdfName name) => resources?.Get<PdfDictionary>(PdfNames.Font)?.Get<PdfDictionary>(name) is { } font ? GetFont(font) : null;

    private RenderFont? GetFont(PdfDictionary font) => _cache.GetOrCreate(font, () => RenderFontFactory.Create(font, _cache));

    private void BeginText()
    {
        _textMatrix = PdfMatrix.Identity;
        _lineMatrix = PdfMatrix.Identity;
    }

    /// <summary>Ends a text object: the glyphs of the clipping modes become the clipping path (9.3.6).</summary>
    private void EndText()
    {
        var clip = _textClip;
        _textClip = null;
        if (clip is null)
            return;

        // The glyphs of the text object, one path
        var builder = new VectorPathBuilder();
        foreach (var path in clip)
        {
            foreach (var segment in path.EnumerateSegments())
            {
                switch (segment.Kind)
                {
                    case PathSegmentKind.MoveTo:
                        builder.MoveTo(segment.Point);
                        break;
                    case PathSegmentKind.LineTo:
                        builder.LineTo(segment.Point);
                        break;
                    case PathSegmentKind.QuadraticBezierTo:
                        builder.QuadraticBezierTo(segment.Control1, segment.Point);
                        break;
                    case PathSegmentKind.CubicBezierTo:
                        builder.CubicBezierTo(segment.Control1, segment.Control2, segment.Point);
                        break;
                    case PathSegmentKind.Close:
                        builder.Close();
                        break;
                }
            }
        }

        AddClip(builder.Build(), FillRule.NonZero);
    }

    private void MoveText(double x, double y)
    {
        _lineMatrix = PdfMatrix.CreateTranslation(x, y) * _lineMatrix;
        _textMatrix = _lineMatrix;
    }

    private void ShowTextArray(DrawingCanvas canvas, PdfArray array, PdfDictionary? resources)
    {
        foreach (var item in array)
        {
            _cache.Charge(1);
            switch (PdfReference.ResolveIfReference(item))
            {
                case PdfString text:
                    ShowText(canvas, text.Span, resources);
                    break;
                case PdfNumber adjustment:
                    {
                        // A number moves the next glyph by thousandths of the font size (9.4.3)
                        var offset = -adjustment.DoubleValue / 1000 * _state.FontSize;
                        _textMatrix = (_state.Font?.IsVertical ?? false)
                            ? PdfMatrix.CreateTranslation(0, offset) * _textMatrix
                            : PdfMatrix.CreateTranslation(offset * _state.HorizontalScaling, 0) * _textMatrix;
                        break;
                    }
            }
        }
    }

    /// <summary>Shows the glyphs of a string (9.4.4): each glyph is painted at the origin of the text space, then the text matrix moves by its width.</summary>
    private void ShowText(DrawingCanvas canvas, ReadOnlySpan<byte> bytes, PdfDictionary? resources)
    {
        // A clipping mode clips to the glyphs shown, even when they have no outline (9.3.6)
        if (_state.TextRenderingMode >= 4 && !bytes.IsEmpty)
        {
            _textClip ??= [];
        }

        var font = _state.Font;
        if (font is null)
            return;

        var mode = _state.TextRenderingMode;
        var paints = _optionalContent.IsVisible && mode is not (3 or 7);
        while (bytes.Length > 0)
        {
            _cache.Charge(1);
            var (code, length) = font.Decoder.ReadCode(bytes);
            bytes = bytes[Math.Max(1, length)..];
            var width = font.Decoder.GetWidth(code);
            var isSpace = length == 1 && code == 32;

            // The text rendering matrix: text space to device space; a vertical font places the glyph by its position vector
            var rendering = new PdfMatrix(_state.FontSize * _state.HorizontalScaling, 0, 0, _state.FontSize, 0, _state.TextRise) * _textMatrix;
            if (font.IsVertical)
            {
                rendering = PdfMatrix.CreateTranslation(-width / 2, -0.88) * rendering;
            }

            if (font.Type3 is { } type3)
            {
                if (paints)
                {
                    RenderType3Glyph(canvas, type3, code, rendering, resources);
                }
            }
            else if (font.GetGlyphPath(code) is { } glyph && (paints || mode >= 4))
            {
                DrawGlyph(canvas, glyph, rendering, mode);
            }

            // The displacement: the width, the character spacing, the word spacing for the single-byte code 32 (9.4.4)
            var spacing = _state.CharacterSpacing + (isSpace ? _state.WordSpacing : 0);
            _textMatrix = font.IsVertical
                ? PdfMatrix.CreateTranslation(0, (-1 * _state.FontSize) + spacing) * _textMatrix
                : PdfMatrix.CreateTranslation(((width * _state.FontSize) + spacing) * _state.HorizontalScaling, 0) * _textMatrix;
        }
    }

    /// <summary>Paints a glyph with the text rendering mode: fill, stroke, both, and the clipping modes that add it to the clipping path (9.3.6).</summary>
    private void DrawGlyph(DrawingCanvas canvas, VectorPath glyph, PdfMatrix rendering, int mode)
    {
        var toUser = rendering;
        if (mode is 0 or 2 or 4 or 6 && _optionalContent.IsVisible && TryGetDeviceTransform(toUser * _state.Ctm, out var toDevice) && TransformPath(glyph, toDevice) is { } devicePath)
        {
            Paint(canvas, stroke: false, (target, brush) => target.Fill(brush, devicePath, FillRule.NonZero), devicePath.Bounds);
        }

        if (mode is 1 or 2 or 5 or 6 && _optionalContent.IsVisible && TryGetDeviceTransform(toUser, out var toUserTransform) && TransformPath(glyph, toUserTransform) is { } userPath)
        {
            StrokeUserPath(canvas, userPath);
        }

        if (mode >= 4 && TryGetDeviceTransform(toUser * _state.Ctm, out var clipTransform) && TransformPath(glyph, clipTransform) is { } clipPath)
        {
            (_textClip ??= []).Add(clipPath);
        }
    }

    /// <summary>Runs the glyph procedure of a Type 3 font (9.6.4) with the font matrix and the text rendering matrix as transformation.</summary>
    private void RenderType3Glyph(DrawingCanvas canvas, Type3Glyphs glyphs, uint code, PdfMatrix rendering, PdfDictionary? resources)
    {
        if (glyphs.GetProcedure(code) is not { } procedure || _depth >= MaxDepth)
            return;

        var state = _state.Clone();
        state.Ctm = glyphs.FontMatrix * rendering * _state.Ctm;
        state.Font = null;
        var fill = _state.FillColorSpace.ToRgb(_state.FillColor);
        var renderer = new ContentRenderer(_cache, _document, state, _depth + 1, _optionalContent)
        {
            ForcedColor = ForcedColor,
            BaseMatrix = BaseMatrix,
            DeviceSize = DeviceSize,
            StencilColor = _state.FillColorSpace is Colors.RenderColorSpace.PatternSpace ? null : fill,

            // A glyph of a text-showing operator of a knockout group is drawn in the element of the operator
            IsKnockoutContext = IsKnockoutContext,
            Element = _element,
        };
        renderer._activeForms.UnionWith(_activeForms);
        renderer.Run(canvas, procedure.GetDecodedData(), glyphs.Resources ?? resources);
    }
}
