using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;
using Meziantou.Framework.Pdf.Rendering.Colors;

namespace Meziantou.Framework.Pdf.Rendering.Graphics;

/// <summary>
/// Interprets content streams (ISO 32000-2, 8 and 9) and paints them on a canvas of Meziantou.Framework.Imaging: the
/// graphics state, the paths, the text, the images, the shadings, the forms and the transparency.
/// </summary>
/// <remarks>
/// The canvas draws in device space, the pixels of the image: the paths are transformed by the current transformation
/// matrix, except the strokes, which are drawn in user space inside a transform of the canvas so that the line width
/// and the dashes scale as the specification says. A content that is invalid stops where it is invalid; what it painted
/// before stays, as viewers show it.
/// </remarks>
internal sealed partial class ContentRenderer
{
    // The nesting of the forms, patterns, Type 3 glyphs and soft masks a content paints
    private const int MaxDepth = 24;

    // The graphics states saved at once
    private const int MaxSavedStates = 1024;

    private readonly RenderCache _cache;
    private readonly PdfDocument _document;
    private readonly Stack<RenderGraphicsState> _savedStates = new();
    private readonly OptionalContent _optionalContent;

    // The images of the brushes of patterns, disposed when the drawing that uses them is done
    private readonly List<IDisposable> _brushImages = [];
    private readonly int _depth;
    private RenderGraphicsState _state;

    public ContentRenderer(RenderCache cache, PdfDocument document, RenderGraphicsState state, int depth, OptionalContent? optionalContent = null)
    {
        _cache = cache;
        _document = document;
        _state = state;
        _depth = depth;
        _optionalContent = optionalContent ?? new OptionalContent(document);
        if (depth > MaxDepth)
            throw new InvalidPdfContentException("The content paints forms, patterns or glyphs nested too deeply");
    }

    /// <summary>Runs a content stream with its resources.</summary>
    public void Run(DrawingCanvas canvas, ReadOnlyMemory<byte> content, PdfDictionary? resources)
    {
        _cache.Charge((content.Length / 16) + 1);
        var reader = new ContentStreamReader(content);
        var operands = new List<PdfObject>();
        var savedDepth = _savedStates.Count;
        try
        {
            while (reader.TryRead(operands, out var name))
            {
                _cache.Charge(1);
                try
                {
                    Execute(canvas, name, operands, resources);
                }
                catch (Exception ex) when (RenderCache.IsContentError(ex))
                {
                    // An operator whose operands or resources are invalid is skipped (8.1 does not say more)
                }
            }
        }
        finally
        {
            // The content ends with the state it started with: the q that are not matched are restored
            while (_savedStates.Count > savedDepth)
            {
                _state = _savedStates.Pop();
            }

            EndText();
        }
    }

    private void Execute(DrawingCanvas canvas, string name, List<PdfObject> operands, PdfDictionary? resources)
    {
        switch (name)
        {
            // General graphics state (8.4.4)
            case "q":
                if (_savedStates.Count < MaxSavedStates)
                {
                    _savedStates.Push(_state);
                    _state = _state.Clone();
                }

                break;
            case "Q":
                if (_savedStates.Count > 0)
                {
                    _state = _savedStates.Pop();
                }

                break;
            case "cm" when operands.Count >= 6:
                _state.Ctm = ReadMatrix(operands) * _state.Ctm;
                break;
            case "w" when operands.Count >= 1:
                _state.LineWidth = Math.Abs(Number(operands, 0));
                break;
            case "J" when operands.Count >= 1:
                _state.LineCap = (int)Number(operands, 0) switch
                {
                    1 => LineCap.Round,
                    2 => LineCap.Square,
                    _ => LineCap.Butt,
                };
                break;
            case "j" when operands.Count >= 1:
                _state.LineJoin = (int)Number(operands, 0) switch
                {
                    1 => LineJoin.Round,
                    2 => LineJoin.Bevel,
                    _ => LineJoin.Miter,
                };
                break;
            case "M" when operands.Count >= 1:
                _state.MiterLimit = Math.Max(1, Number(operands, 0));
                break;
            case "d" when operands.Count >= 2 && operands[0] is PdfArray dashes:
                _state.DashArray = [.. dashes.Select(item => PdfReference.ResolveIfReference(item) is PdfNumber number ? Math.Abs(number.DoubleValue) : 0)];
                _state.DashPhase = Number(operands, 1);
                break;
            case "gs" when operands.Count >= 1 && operands[^1] is PdfName gsName:
                SetGraphicsState(resources?.Get<PdfDictionary>(PdfNames.ExtGState)?.Get<PdfDictionary>(gsName));
                break;

            // Paths (8.5)
            case "m" when operands.Count >= 2:
                MoveTo(Number(operands, 0), Number(operands, 1));
                break;
            case "l" when operands.Count >= 2:
                LineTo(Number(operands, 0), Number(operands, 1));
                break;
            case "c" when operands.Count >= 6:
                CurveTo(Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3), Number(operands, 4), Number(operands, 5));
                break;
            case "v" when operands.Count >= 4:
                CurveTo(_currentX, _currentY, Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3));
                break;
            case "y" when operands.Count >= 4:
                CurveTo(Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3), Number(operands, 2), Number(operands, 3));
                break;
            case "h":
                ClosePath();
                break;
            case "re" when operands.Count >= 4:
                Rectangle(Number(operands, 0), Number(operands, 1), Number(operands, 2), Number(operands, 3));
                break;
            case "S":
                PaintPath(canvas, fill: null, stroke: true);
                break;
            case "s":
                ClosePath();
                PaintPath(canvas, fill: null, stroke: true);
                break;
            case "f" or "F":
                PaintPath(canvas, FillRule.NonZero, stroke: false);
                break;
            case "f*":
                PaintPath(canvas, FillRule.EvenOdd, stroke: false);
                break;
            case "B":
                PaintPath(canvas, FillRule.NonZero, stroke: true);
                break;
            case "B*":
                PaintPath(canvas, FillRule.EvenOdd, stroke: true);
                break;
            case "b":
                ClosePath();
                PaintPath(canvas, FillRule.NonZero, stroke: true);
                break;
            case "b*":
                ClosePath();
                PaintPath(canvas, FillRule.EvenOdd, stroke: true);
                break;
            case "n":
                PaintPath(canvas, fill: null, stroke: false);
                break;
            case "W":
                _pendingClip = FillRule.NonZero;
                break;
            case "W*":
                _pendingClip = FillRule.EvenOdd;
                break;

            // Colors (8.6.8)
            case "CS" when operands.Count >= 1:
                _state.StrokeColorSpace = RenderColorSpace.Read(operands[^1], resources, _cache);
                _state.StrokeColor = _state.StrokeColorSpace.GetInitialColor();
                _state.StrokePattern = null;
                break;
            case "cs" when operands.Count >= 1:
                _state.FillColorSpace = RenderColorSpace.Read(operands[^1], resources, _cache);
                _state.FillColor = _state.FillColorSpace.GetInitialColor();
                _state.FillPattern = null;
                break;
            case "SC" or "SCN":
                SetColor(operands, stroke: true, resources);
                break;
            case "sc" or "scn":
                SetColor(operands, stroke: false, resources);
                break;
            case "G" when operands.Count >= 1:
                SetDeviceColor(RenderColorSpace.Gray, operands, stroke: true, resources);
                break;
            case "g" when operands.Count >= 1:
                SetDeviceColor(RenderColorSpace.Gray, operands, stroke: false, resources);
                break;
            case "RG" when operands.Count >= 3:
                SetDeviceColor(RenderColorSpace.Rgb, operands, stroke: true, resources);
                break;
            case "rg" when operands.Count >= 3:
                SetDeviceColor(RenderColorSpace.Rgb, operands, stroke: false, resources);
                break;
            case "K" when operands.Count >= 4:
                SetDeviceColor(RenderColorSpace.Cmyk, operands, stroke: true, resources);
                break;
            case "k" when operands.Count >= 4:
                SetDeviceColor(RenderColorSpace.Cmyk, operands, stroke: false, resources);
                break;

            // Text (9)
            case "BT":
                BeginText();
                break;
            case "ET":
                EndText();
                break;
            case "Tc" when operands.Count >= 1:
                _state.CharacterSpacing = Number(operands, 0);
                break;
            case "Tw" when operands.Count >= 1:
                _state.WordSpacing = Number(operands, 0);
                break;
            case "Tz" when operands.Count >= 1:
                _state.HorizontalScaling = Number(operands, 0) / 100;
                break;
            case "TL" when operands.Count >= 1:
                _state.Leading = Number(operands, 0);
                break;
            case "Tf" when operands.Count >= 2:
                _state.FontSize = Number(operands, 1);
                _state.Font = operands[0] is PdfName fontName ? GetFont(resources, fontName) : null;
                break;
            case "Tr" when operands.Count >= 1:
                _state.TextRenderingMode = (int)Number(operands, 0);
                break;
            case "Ts" when operands.Count >= 1:
                _state.TextRise = Number(operands, 0);
                break;
            case "Td" when operands.Count >= 2:
                MoveText(Number(operands, 0), Number(operands, 1));
                break;
            case "TD" when operands.Count >= 2:
                _state.Leading = -Number(operands, 1);
                MoveText(Number(operands, 0), Number(operands, 1));
                break;
            case "Tm" when operands.Count >= 6:
                _textMatrix = ReadMatrix(operands);
                _lineMatrix = _textMatrix;
                break;
            case "T*":
                MoveText(0, -_state.Leading);
                break;
            case "Tj" when operands.Count >= 1 && operands[^1] is PdfString text:
                ShowText(canvas, text.Span, resources);
                break;
            case "'" when operands.Count >= 1 && operands[^1] is PdfString text:
                MoveText(0, -_state.Leading);
                ShowText(canvas, text.Span, resources);
                break;
            case "\"" when operands.Count >= 3 && operands[^1] is PdfString text:
                _state.WordSpacing = Number(operands, 0);
                _state.CharacterSpacing = Number(operands, 1);
                MoveText(0, -_state.Leading);
                ShowText(canvas, text.Span, resources);
                break;
            case "TJ" when operands.Count >= 1 && operands[^1] is PdfArray array:
                ShowTextArray(canvas, array, resources);
                break;
            case "d1" when StencilColor is { } stencil:
                // A Type 3 glyph that is a stencil (9.6.4): its colors are the fill color of the text
                ForcedColor = stencil;
                break;

            // Shadings, images, external objects (8.7.4.2, 8.9, 8.8)
            case "sh" when operands.Count >= 1 && operands[^1] is PdfName shadingName:
                PaintShadingOperator(canvas, resources?.Get<PdfDictionary>(PdfNames.Shading)?.GetResolved(shadingName));
                break;
            case "BI" when operands.Count >= 2 && operands[0] is PdfDictionary inlineDictionary && operands[1] is PdfString inlineData:
                DrawInlineImage(canvas, inlineDictionary, inlineData.Bytes, resources);
                break;
            case "Do" when operands.Count >= 1 && operands[^1] is PdfName xobjectName:
                PaintXObject(canvas, resources?.Get<PdfDictionary>(PdfNames.XObject)?.GetResolved(xobjectName), resources);
                break;

            // Marked content and optional content (14.6, 8.11)
            case "BMC":
                _optionalContent.BeginMarkedContent(null);
                break;
            case "BDC" when operands.Count >= 2:
                _optionalContent.BeginMarkedContent(operands[0] is PdfName { Value: "OC" } ? GetPropertyList(operands[1], resources) : null);
                break;
            case "EMC":
                _optionalContent.EndMarkedContent();
                break;
        }
    }

    private static PdfDictionary? GetPropertyList(PdfObject value, PdfDictionary? resources) => value switch
    {
        PdfDictionary dictionary => dictionary,
        PdfName name => resources?.Get<PdfDictionary>(PdfNames.Properties)?.Get<PdfDictionary>(name),
        _ => null,
    };

    /// <summary>Sets the parameters of a graphics state parameter dictionary (8.4.5).</summary>
    private void SetGraphicsState(PdfDictionary? dictionary)
    {
        if (dictionary is null)
            return;

        foreach (var (key, raw) in dictionary)
        {
            var value = PdfReference.ResolveIfReference(raw);
            switch (key.Value)
            {
                case "LW" when value is PdfNumber number:
                    _state.LineWidth = Math.Abs(number.DoubleValue);
                    break;
                case "LC" when value is PdfNumber number:
                    _state.LineCap = (int)number.DoubleValue switch { 1 => LineCap.Round, 2 => LineCap.Square, _ => LineCap.Butt };
                    break;
                case "LJ" when value is PdfNumber number:
                    _state.LineJoin = (int)number.DoubleValue switch { 1 => LineJoin.Round, 2 => LineJoin.Bevel, _ => LineJoin.Miter };
                    break;
                case "ML" when value is PdfNumber number:
                    _state.MiterLimit = Math.Max(1, number.DoubleValue);
                    break;
                case "D" when value is PdfArray { Count: >= 2 } dash && dash.GetResolved(0) is PdfArray dashes:
                    _state.DashArray = [.. dashes.Select(item => PdfReference.ResolveIfReference(item) is PdfNumber n ? Math.Abs(n.DoubleValue) : 0)];
                    _state.DashPhase = dash.GetNumber(1);
                    break;
                case "Font" when value is PdfArray { Count: >= 2 } font && font[0] is PdfReference fontReference && fontReference.Resolve() is PdfDictionary fontDictionary:
                    _state.Font = GetFont(fontDictionary);
                    _state.FontSize = font.GetNumber(1);
                    break;
                case "CA" when value is PdfNumber number:
                    _state.StrokeAlpha = Math.Clamp(number.DoubleValue, 0, 1);
                    break;
                case "ca" when value is PdfNumber number:
                    _state.FillAlpha = Math.Clamp(number.DoubleValue, 0, 1);
                    break;
                case "BM":
                    _state.BlendMode = ReadBlendMode(value);
                    break;
                case "SMask":
                    _state.SoftMask = value is PdfDictionary mask ? (mask, _state.Ctm) : null;
                    break;

                // TR2 replaces TR (8.4.5)
                case "TR2":
                case "TR" when !dictionary.ContainsKey(PdfName.Create("TR2")):
                    _state.Transfer = ReadTransfer(value);
                    break;
            }
        }
    }

    private static BlendMode ReadBlendMode(PdfObject value)
    {
        // An array lists blend modes by preference: the first one known is used (11.3.5)
        var names = value switch
        {
            PdfName name => [name],
            PdfArray array => array.Select(PdfReference.ResolveIfReference).OfType<PdfName>().ToArray(),
            _ => [],
        };
        foreach (var name in names)
        {
            BlendMode? mode = name.Value switch
            {
                "Normal" or "Compatible" => BlendMode.Normal,
                "Multiply" => BlendMode.Multiply,
                "Screen" => BlendMode.Screen,
                "Overlay" => BlendMode.Overlay,
                "Darken" => BlendMode.Darken,
                "Lighten" => BlendMode.Lighten,
                "ColorDodge" => BlendMode.ColorDodge,
                "ColorBurn" => BlendMode.ColorBurn,
                "HardLight" => BlendMode.HardLight,
                "SoftLight" => BlendMode.SoftLight,
                "Difference" => BlendMode.Difference,
                "Exclusion" => BlendMode.Exclusion,
                "Hue" => BlendMode.Hue,
                "Saturation" => BlendMode.Saturation,
                "Color" => BlendMode.Color,
                "Luminosity" => BlendMode.Luminosity,
                _ => null,
            };
            if (mode is { } known)
                return known;
        }

        return BlendMode.Normal;
    }

    private void SetDeviceColor(RenderColorSpace space, List<PdfObject> operands, bool stroke, PdfDictionary? resources)
    {
        // The device color spaces may be replaced by the default color spaces of the resources
        var name = space.ComponentCount switch { 1 => "DeviceGray", 3 => "DeviceRGB", _ => "DeviceCMYK" };
        var resolved = RenderColorSpace.Read(PdfName.Create(name), resources, _cache);
        var components = ReadComponents(operands, space.ComponentCount);
        if (stroke)
        {
            _state.StrokeColorSpace = resolved;
            _state.StrokeColor = components;
            _state.StrokePattern = null;
        }
        else
        {
            _state.FillColorSpace = resolved;
            _state.FillColor = components;
            _state.FillPattern = null;
        }
    }

    private void SetColor(List<PdfObject> operands, bool stroke, PdfDictionary? resources)
    {
        var space = stroke ? _state.StrokeColorSpace : _state.FillColorSpace;
        if (space is RenderColorSpace.PatternSpace pattern)
        {
            // A pattern: its name, after the components of the underlying color space of an uncolored pattern
            var patternObject = operands.Count > 0 && operands[^1] is PdfName patternName ? resources?.Get<PdfDictionary>(PdfNames.Pattern)?.GetResolved(patternName) : null;
            var components = ReadComponents(operands, pattern.Underlying?.ComponentCount ?? 0);
            if (stroke)
            {
                _state.StrokePattern = patternObject;
                _state.StrokeColor = components;
            }
            else
            {
                _state.FillPattern = patternObject;
                _state.FillColor = components;
            }

            return;
        }

        var values = ReadComponents(operands, space.ComponentCount);
        if (stroke)
        {
            _state.StrokeColor = values;
        }
        else
        {
            _state.FillColor = values;
        }
    }

    private static double[] ReadComponents(List<PdfObject> operands, int count)
    {
        var result = new double[count];
        var numbers = operands.OfType<PdfNumber>().ToList();
        for (var i = 0; i < count && i < numbers.Count; i++)
        {
            result[i] = numbers[i].DoubleValue;
        }

        return result;
    }

    private static double Number(List<PdfObject> operands, int index) => index < operands.Count && operands[index] is PdfNumber number && double.IsFinite(number.DoubleValue) ? number.DoubleValue : 0;

    private static PdfMatrix ReadMatrix(List<PdfObject> operands)
    {
        var start = operands.Count - 6;
        return new PdfMatrix(Number(operands, start), Number(operands, start + 1), Number(operands, start + 2), Number(operands, start + 3), Number(operands, start + 4), Number(operands, start + 5));
    }

    internal static PdfMatrix ReadMatrix(PdfArray? array) => array is { Count: 6 } ? new PdfMatrix(Finite(array.GetNumber(0)), Finite(array.GetNumber(1)), Finite(array.GetNumber(2)), Finite(array.GetNumber(3)), Finite(array.GetNumber(4)), Finite(array.GetNumber(5))) : PdfMatrix.Identity;

    private static double Finite(double value) => double.IsFinite(value) ? value : 0;
}
