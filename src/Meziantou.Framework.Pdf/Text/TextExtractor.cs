using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// Extracts the text of a page: runs the text operators of its content (ISO 32000-2, 9.4) and of its form XObjects,
/// places each glyph with the text and graphics matrices, then joins the glyphs into lines in the order of the content.
/// </summary>
internal sealed class TextExtractor
{
    private const int MaxFormDepth = 16;

    // The work a page may do, in units: four bytes of content parsed, an operator, a glyph or an element of a TJ array.
    // A form can paint other forms several times each, so the work grows with the product of the counts of each level,
    // not with the size of the file.
    private const long MaxOperations = 200_000_000;

    // The glyphs are kept until the text is built: their count bounds the memory, apart from the work
    private const int MaxGlyphs = 4_000_000;

    // The characters of the texts of the glyphs, replacement texts included: a text painted many times by forms is
    // counted each time
    private const long MaxTextLength = 32_000_000;

    private readonly List<TextGlyph> _glyphs = [];
    private readonly Dictionary<PdfStream, byte[]?> _formData = new(ReferenceEqualityComparer.Instance);
    private long _operations;
    private long _textLength;
    private readonly Dictionary<PdfString, string> _actualTexts = new(ReferenceEqualityComparer.Instance);

    // The replacement text of the marked-content sequence being read (14.9.4): its glyphs are replaced by it
    private string? _actualText;
    private int _actualTextStart;
    private readonly Dictionary<PdfDictionary, PdfFontDecoder> _decoders = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<PdfStream> _activeForms = new(ReferenceEqualityComparer.Instance);

    private TextExtractor()
    {
    }

    public static string Extract(PdfPage page)
    {
        // The content streams of a page are one content: an operator and its operands may be in two streams (7.8.2)
        var content = new MemoryStream();
        foreach (var stream in page.GetContentStreams())
        {
            try
            {
                var data = stream.GetDecodedData();
                content.Write(data);
                content.WriteByte((byte)'\n');
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
            }
        }

        var extractor = new TextExtractor();
        var state = new TextGraphicsState();
        extractor.Run(content.ToArray(), page.Resources, ref state, depth: 0);
        return extractor.BuildText();
    }

    private void Run(byte[] content, PdfDictionary? resources, ref TextGraphicsState state, int depth)
    {
        Charge((content.Length / 4) + 1);
        var reader = new ContentStreamReader(content);
        var operands = new List<PdfObject>();
        var stack = new Stack<TextGraphicsState>();

        // The marked-content sequences of this content, and the one that has the replacement text
        var markedContentDepth = 0;
        var actualTextDepth = -1;
        while (reader.TryRead(operands, out var name))
        {
            Charge(1);
            switch (name)
            {
                case "q":
                    stack.Push(state);
                    break;
                case "Q":
                    if (stack.Count > 0)
                    {
                        state = stack.Pop();
                    }

                    break;
                case "cm" when operands.Count >= 6:
                    state.Ctm = ReadMatrix(operands) * state.Ctm;
                    break;
                case "BT":
                    state.TextMatrix = PdfMatrix.Identity;
                    state.LineMatrix = PdfMatrix.Identity;
                    break;
                case "Tc" when operands.Count >= 1:
                    state.CharacterSpacing = Number(operands, 0);
                    break;
                case "Tw" when operands.Count >= 1:
                    state.WordSpacing = Number(operands, 0);
                    break;
                case "Tz" when operands.Count >= 1:
                    state.HorizontalScaling = Number(operands, 0) / 100;
                    break;
                case "TL" when operands.Count >= 1:
                    state.Leading = Number(operands, 0);
                    break;
                case "Ts" when operands.Count >= 1:
                    state.Rise = Number(operands, 0);
                    break;
                case "Tf" when operands.Count >= 2:
                    state.FontSize = Number(operands, 1);
                    state.Font = operands[0] is PdfName fontName ? GetDecoder(resources, fontName) : null;
                    break;
                case "Td" when operands.Count >= 2:
                    MoveText(ref state, Number(operands, 0), Number(operands, 1));
                    break;
                case "TD" when operands.Count >= 2:
                    state.Leading = -Number(operands, 1);
                    MoveText(ref state, Number(operands, 0), Number(operands, 1));
                    break;
                case "Tm" when operands.Count >= 6:
                    state.TextMatrix = ReadMatrix(operands);
                    state.LineMatrix = state.TextMatrix;
                    break;
                case "T*":
                    MoveText(ref state, 0, -state.Leading);
                    break;
                case "Tj" when operands.Count >= 1 && operands[^1] is PdfString text:
                    ShowText(ref state, text.Span);
                    break;
                case "'" when operands.Count >= 1 && operands[^1] is PdfString text:
                    MoveText(ref state, 0, -state.Leading);
                    ShowText(ref state, text.Span);
                    break;
                case "\"" when operands.Count >= 3 && operands[^1] is PdfString text:
                    state.WordSpacing = Number(operands, 0);
                    state.CharacterSpacing = Number(operands, 1);
                    MoveText(ref state, 0, -state.Leading);
                    ShowText(ref state, text.Span);
                    break;
                case "TJ" when operands.Count >= 1 && operands[^1] is PdfArray array:
                    foreach (var item in array)
                    {
                        Charge(1);
                        switch (item)
                        {
                            case PdfString segment:
                                ShowText(ref state, segment.Span);
                                break;
                            case PdfNumber adjustment:
                                var offset = -adjustment.DoubleValue / 1000 * state.FontSize;
                                Advance(ref state, (state.Font?.IsVertical ?? false) ? 0 : offset * state.HorizontalScaling, (state.Font?.IsVertical ?? false) ? offset : 0);
                                break;
                        }
                    }

                    break;
                case "BMC":
                    markedContentDepth++;
                    break;
                case "BDC":
                    markedContentDepth++;
                    if (_actualText is null && GetActualText(resources, operands) is { } actualText)
                    {
                        _actualText = actualText;
                        _actualTextStart = _glyphs.Count;
                        actualTextDepth = markedContentDepth;
                    }

                    break;
                case "EMC" when markedContentDepth > 0:
                    if (markedContentDepth == actualTextDepth)
                    {
                        EndActualText(ref state);
                        actualTextDepth = -1;
                    }

                    markedContentDepth--;
                    break;
                case "Do" when operands.Count >= 1 && operands[^1] is PdfName xobjectName && depth < MaxFormDepth:
                    RunForm(resources, xobjectName, state, depth);
                    break;
            }
        }

        // A sequence the content does not end ends with it
        if (actualTextDepth >= 0)
        {
            EndActualText(ref state);
        }
    }

    /// <summary>Gets the replacement text of the property list of a <c>BDC</c> operator: inline, or a resource (14.6.2).</summary>
    private string? GetActualText(PdfDictionary? resources, List<PdfObject> operands)
    {
        var properties = operands.Count >= 2 ? operands[^1] switch
        {
            PdfDictionary dictionary => dictionary,
            PdfName name => resources?.Get<PdfDictionary>(PdfNames.Properties)?.Get<PdfDictionary>(name),
            _ => null,
        } : null;
        if (properties?.GetResolved(PdfNames.ActualText) is not PdfString text)
            return null;

        // A form painted several times decodes its replacement texts once
        if (!_actualTexts.TryGetValue(text, out var value))
        {
            value = text.ToText();
            _actualTexts[text] = value;
        }

        return value;
    }

    /// <summary>Replaces the glyphs of the sequence that has a replacement text by the text, from the start of the first glyph to the end of the last one.</summary>
    private void EndActualText(ref TextGraphicsState state)
    {
        var text = _actualText!;
        _actualText = null;
        if (_glyphs.Count > _actualTextStart)
        {
            var first = _glyphs[_actualTextStart];
            var last = _glyphs[^1];
            _glyphs.RemoveRange(_actualTextStart, _glyphs.Count - _actualTextStart);
            AddGlyph(first with { Text = text, EndX = last.EndX, EndY = last.EndY });
            return;
        }

        // No glyph: the text is at the current point
        var matrix = new PdfMatrix(state.FontSize * state.HorizontalScaling, 0, 0, state.FontSize, 0, state.Rise) * state.TextMatrix * state.Ctm;
        var (x, y) = matrix.Transform(0, 0);
        var (sizeX, sizeY) = matrix.TransformVector(0, 1);
        var (directionX, directionY) = (state.TextMatrix * state.Ctm).TransformVector(1, 0);
        AddGlyph(new TextGlyph(text, x, y, x, y, Math.Sqrt((sizeX * sizeX) + (sizeY * sizeY)), Math.Atan2(directionY, directionX)));
    }

    private void RunForm(PdfDictionary? resources, PdfName name, TextGraphicsState state, int depth)
    {
        if (resources?.Get<PdfDictionary>(PdfNames.XObject)?.Get<PdfStream>(name) is not { } form || form.Dictionary.GetName(PdfNames.Subtype) != PdfNames.Form || !_activeForms.Add(form))
            return;

        try
        {
            if (form.Dictionary.Get<PdfArray>(PdfNames.Matrix) is { Count: 6 } matrix)
            {
                state.Ctm = new PdfMatrix(matrix.GetNumber(0), matrix.GetNumber(1), matrix.GetNumber(2), matrix.GetNumber(3), matrix.GetNumber(4), matrix.GetNumber(5)) * state.Ctm;
            }

            if (!_formData.TryGetValue(form, out var data))
            {
                try
                {
                    data = form.GetDecodedData();
                }
                catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
                {
                    data = null;
                }

                _formData[form] = data;
            }

            if (data is null)
                return;

            Run(data, form.Dictionary.Get<PdfDictionary>(PdfNames.Resources) ?? resources, ref state, depth + 1);
        }
        finally
        {
            _activeForms.Remove(form);
        }
    }

    private PdfFontDecoder? GetDecoder(PdfDictionary? resources, PdfName name)
    {
        if (resources?.Get<PdfDictionary>(PdfNames.Font)?.Get<PdfDictionary>(name) is not { } font)
            return null;

        if (!_decoders.TryGetValue(font, out var decoder))
        {
            decoder = PdfFontDecoder.Create(font);
            _decoders[font] = decoder;
        }

        return decoder;
    }

    private void ShowText(ref TextGraphicsState state, ReadOnlySpan<byte> bytes)
    {
        var font = state.Font;
        if (font is null)
            return;

        while (bytes.Length > 0)
        {
            Charge(1);
            var (code, length) = font.ReadCode(bytes);
            bytes = bytes[length..];

            // The glyph is drawn at the origin of the text space, transformed by the text rendering matrix (9.4.4)
            var renderingMatrix = new PdfMatrix(state.FontSize * state.HorizontalScaling, 0, 0, state.FontSize, 0, state.Rise) * state.TextMatrix * state.Ctm;
            var (x, y) = renderingMatrix.Transform(0, 0);
            var width = font.GetWidth(code);
            var isSpace = length == 1 && code == 32;
            var text = font.GetText(code);
            double advanceX;
            double advanceY;
            if (font.IsVertical)
            {
                advanceX = 0;
                advanceY = -((width * state.FontSize) + state.CharacterSpacing + (isSpace ? state.WordSpacing : 0));
            }
            else
            {
                advanceX = ((width * state.FontSize) + state.CharacterSpacing + (isSpace ? state.WordSpacing : 0)) * state.HorizontalScaling;
                advanceY = 0;
            }

            var (endX, endY) = (state.TextMatrix * state.Ctm).Transform(advanceX, advanceY);
            var (sizeX, sizeY) = renderingMatrix.TransformVector(0, 1);
            var size = Math.Sqrt((sizeX * sizeX) + (sizeY * sizeY));
            var (directionX, directionY) = (state.TextMatrix * state.Ctm).TransformVector(1, 0);
            AddGlyph(new TextGlyph(text, x, y, endX, endY, size, Math.Atan2(directionY, directionX)));
            Advance(ref state, advanceX, advanceY);
        }
    }

    private void AddGlyph(TextGlyph glyph)
    {
        if (_glyphs.Count >= MaxGlyphs)
            throw new PdfResourceLimitException($"The content of the page shows more than {MaxGlyphs} glyphs");

        _textLength += glyph.Text?.Length ?? 0;
        if (_textLength > MaxTextLength)
            throw new PdfResourceLimitException($"The text of the page is longer than {MaxTextLength} characters");

        _glyphs.Add(glyph);
    }

    private void Charge(long count)
    {
        _operations += count;
        if (_operations > MaxOperations)
            throw new PdfResourceLimitException($"The content of the page runs more than {MaxOperations} operators and glyphs");
    }

    private static void Advance(ref TextGraphicsState state, double x, double y) => state.TextMatrix = PdfMatrix.CreateTranslation(x, y) * state.TextMatrix;

    private static void MoveText(ref TextGraphicsState state, double x, double y)
    {
        state.LineMatrix = PdfMatrix.CreateTranslation(x, y) * state.LineMatrix;
        state.TextMatrix = state.LineMatrix;
    }

    private static PdfMatrix ReadMatrix(List<PdfObject> operands)
    {
        var start = operands.Count - 6;
        return new PdfMatrix(Number(operands, start), Number(operands, start + 1), Number(operands, start + 2), Number(operands, start + 3), Number(operands, start + 4), Number(operands, start + 5));
    }

    private static double Number(List<PdfObject> operands, int index) => index >= 0 && index < operands.Count && operands[index] is PdfNumber number ? number.DoubleValue : 0;

    /// <summary>Joins the glyphs: a new line when a glyph leaves the baseline or goes back, a space when a gap is wider than a fraction of the font size.</summary>
    private string BuildText()
    {
        var builder = new StringBuilder();
        TextGlyph? previous = null;
        foreach (var glyph in _glyphs)
        {
            if (glyph.Text is null)
                continue;

            if (previous is { } last)
            {
                var cos = Math.Cos(last.Angle);
                var sin = Math.Sin(last.Angle);

                // The position of the glyph relative to the end of the previous one, along and across the baseline
                var dx = glyph.X - last.EndX;
                var dy = glyph.Y - last.EndY;
                var along = (dx * cos) + (dy * sin);
                var across = (-dx * sin) + (dy * cos);
                var size = Math.Max(Math.Max(last.Size, glyph.Size), 0.001);
                if (Math.Abs(across) > size * 0.5 || along < -size * 2 || Math.Abs(glyph.Angle - last.Angle) > 0.01)
                {
                    builder.Append('\n');
                }
                else if (along > size * 0.15 && !EndsWithSpace(builder) && glyph.Text is not [' ', ..])
                {
                    builder.Append(' ');
                }
            }

            builder.Append(glyph.Text);
            previous = glyph;
        }

        return builder.ToString();
    }

    private static bool EndsWithSpace(StringBuilder builder) => builder.Length > 0 && char.IsWhiteSpace(builder[^1]);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TextGlyph(string? Text, double X, double Y, double EndX, double EndY, double Size, double Angle);
}
