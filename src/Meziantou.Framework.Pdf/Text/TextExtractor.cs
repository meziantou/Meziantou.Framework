using System.Runtime.InteropServices;
using System.Text;
using Meziantou.Framework.Pdf.Content;
using Meziantou.Framework.Pdf.Objects;

namespace Meziantou.Framework.Pdf.Text;

/// <summary>
/// Extracts the text of a page: runs the text operators of its content (ISO 32000-2, 9.4) and of its form XObjects,
/// places each glyph with the text and graphics matrices, then joins the glyphs into lines in the order of the content.
/// </summary>
internal sealed partial class TextExtractor
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

    // The boxes of the glyphs, when the positions are extracted
    private readonly List<PdfRectangle>? _bounds;
    private readonly Dictionary<PdfStream, byte[]?> _formData = new(ReferenceEqualityComparer.Instance);
    private long _operations;
    private long _textLength;
    private readonly Dictionary<PdfString, string> _actualTexts = new(ReferenceEqualityComparer.Instance);

    // The replacement text of the marked-content sequence being read (14.9.4): its glyphs are replaced by it
    private string? _actualText;
    private int _actualTextStart;
    private readonly TextDecoderCache _decoders;
    private readonly bool _expandLigatures;
    private readonly bool _layoutOrder;
    private readonly HashSet<PdfStream> _activeForms = new(ReferenceEqualityComparer.Instance);

    private TextExtractor(TextDecoderCache decoders, PdfTextExtractionOptions options, bool withBounds)
    {
        _decoders = decoders;
        _expandLigatures = options.ExpandLigatures;
        _layoutOrder = options.ReadingOrder is PdfTextReadingOrder.Layout;
        _bounds = withBounds || _layoutOrder ? [] : null;
    }

    public static string Extract(PdfPage page, PdfTextExtractionOptions options)
    {
        if (options.ReadingOrder is PdfTextReadingOrder.Layout)
            return ExtractLayout(page, options).Text;

        var extractor = new TextExtractor(page.Document.TextDecoders, options, withBounds: false);
        extractor.RunPage(page);
        return extractor.BuildText(layout: false).Text;
    }

    public static PdfTextLayout ExtractLayout(PdfPage page, PdfTextExtractionOptions options)
    {
        var extractor = new TextExtractor(page.Document.TextDecoders, options, withBounds: true);
        extractor.RunPage(page);
        var (text, lines) = extractor.BuildText(layout: true);
        if (extractor._layoutOrder)
            return extractor.OrderByLayout(lines!, page.Rotation);

        return new PdfTextLayout(text, lines!, lines!.Count > 0 ? [new PdfTextBlock(lines)] : []);
    }

    /// <summary>Counts the glyphs of a page whose font gives no text, such as the codes of a font without encoding information.</summary>
    public static int CountGlyphsWithoutText(PdfPage page)
    {
        var extractor = new TextExtractor(page.Document.TextDecoders, PdfTextExtractionOptions.Default, withBounds: false);
        extractor.RunPage(page);
        return extractor._glyphs.Count(glyph => glyph.Text is null);
    }

    private void RunPage(PdfPage page)
    {
        // The content streams of a page are one content: an operator and its operands may be in two streams (7.8.2).
        // One stream is read in place; several are copied once, with a line feed between them.
        var streams = new List<byte[]>();
        foreach (var stream in page.GetContentStreams())
        {
            try
            {
                streams.Add(stream.GetDecodedData());
            }
            catch (Exception ex) when (ex is InvalidPdfContentException or UnsupportedPdfFeatureException)
            {
            }
        }

        ReadOnlyMemory<byte> content;
        if (streams.Count == 1)
        {
            content = streams[0];
        }
        else
        {
            var length = 0L;
            foreach (var data in streams)
            {
                length += data.Length + 1;
            }

            if (length > Array.MaxLength)
                throw new PdfResourceLimitException("The content streams of the page are too long");

            var buffer = new byte[length];
            var position = 0;
            foreach (var data in streams)
            {
                data.CopyTo(buffer, position);
                position += data.Length;
                buffer[position++] = (byte)'\n';
            }

            content = buffer;
        }

        var state = new TextGraphicsState();
        Run(content, page.Resources, ref state, depth: 0);
    }

    private void Run(ReadOnlyMemory<byte> content, PdfDictionary? resources, ref TextGraphicsState state, int depth)
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
                                // A number moves the next glyph by thousandths of the font size, against the writing direction (9.4.3)
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
            value = ExpandLigatures(text.ToText())!;
            _actualTexts[text] = value;
        }

        return value;
    }

    /// <summary>Replaces the glyphs of the sequence that has a replacement text by the text, from the start of the first glyph to the end of the last one.</summary>
    private void EndActualText(ref TextGraphicsState state)
    {
        var text = _actualText;
        _actualText = null;
        if (_glyphs.Count > _actualTextStart)
        {
            var first = _glyphs[_actualTextStart];
            var last = _glyphs[^1];
            var count = _glyphs.Count - _actualTextStart;
            var bounds = _bounds is null ? default : Union(CollectionsMarshal.AsSpan(_bounds).Slice(_actualTextStart, count));
            _glyphs.RemoveRange(_actualTextStart, count);
            _bounds?.RemoveRange(_actualTextStart, count);
            AddGlyph(first with { Text = text, EndX = last.EndX, EndY = last.EndY }, bounds);
            return;
        }

        // No glyph: the text is at the current point
        var matrix = state.TextMatrix * state.Ctm;
        var (x, y) = matrix.Transform(0, state.Rise);
        var (sizeX, sizeY) = matrix.TransformVector(0, state.FontSize);
        var (directionX, directionY) = matrix.TransformVector(1, 0);
        AddGlyph(new TextGlyph(text, x, y, x, y, Math.Sqrt((sizeX * sizeX) + (sizeY * sizeY)), Math.Atan2(directionY, directionX), state.Font), new PdfRectangle(x, y, x, y));
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

        return _decoders.Get(font);
    }

    /// <summary>
    /// Shows the glyphs of a string (9.4.4): each glyph is painted at the origin of text space transformed by the text
    /// rendering matrix, then the text matrix moves by its displacement. The matrix from text space to user space and the
    /// vectors of the text state do not change within a string: they are computed once, and the displacements are added
    /// up in text space.
    /// </summary>
    private void ShowText(ref TextGraphicsState state, ReadOnlySpan<byte> bytes)
    {
        var font = state.Font;
        if (font is null)
            return;

        var matrix = state.TextMatrix * state.Ctm;
        var vertical = font.IsVertical;
        var (upX, upY) = matrix.TransformVector(0, state.FontSize);
        var size = Math.Sqrt((upX * upX) + (upY * upY));
        var (directionX, directionY) = vertical ? matrix.TransformVector(0, -1) : matrix.TransformVector(1, 0);
        var angle = Math.Atan2(directionY, directionX);

        // The vector across the writing direction the box of a glyph spans: up for horizontal writing, right for vertical writing
        var (acrossX, acrossY) = vertical ? matrix.TransformVector(state.FontSize * state.HorizontalScaling, 0) : (upX, upY);
        var x = 0d;
        var y = 0d;
        while (bytes.Length > 0)
        {
            Charge(1);
            var (code, length) = font.ReadCode(bytes);
            bytes = bytes[Math.Max(1, length)..];

            // The word spacing applies to the single-byte code 32 (9.3.3)
            var spacing = state.CharacterSpacing + (length == 1 && code == 32 ? state.WordSpacing : 0);
            double advanceX;
            double advanceY;
            double low;
            double high;
            if (vertical)
            {
                // The vertical displacement (negative downward) and the position vector of the CIDFont (9.7.4.3): the
                // glyph is centered on the vertical origin, the spacing is added to the vertical displacement (9.4.4)
                var (verticalAdvance, originX, _) = font.GetVerticalMetrics(code);
                advanceX = 0;
                advanceY = (verticalAdvance * state.FontSize) + spacing;
                low = -originX;
                high = font.GetWidth(code) - originX;
            }
            else
            {
                advanceX = ((font.GetWidth(code) * state.FontSize) + spacing) * state.HorizontalScaling;
                advanceY = 0;
                low = font.Descent;
                high = font.Ascent;
            }

            var (startX, startY) = matrix.Transform(x, y + state.Rise);
            var (endX, endY) = matrix.Transform(x + advanceX, y + advanceY);
            var glyph = new TextGlyph(ExpandLigatures(font.GetText(code)), startX, startY, endX, endY, size, angle, font);
            if (_bounds is null)
            {
                AddGlyph(glyph, default);
            }
            else
            {
                // The box: from the origin to the end of the glyph, raised by the text rise, and across the writing direction
                var (advanceVectorX, advanceVectorY) = matrix.TransformVector(advanceX, advanceY);
                AddGlyph(glyph, GetBounds(startX, startY, advanceVectorX, advanceVectorY, acrossX, acrossY, low, high));
            }

            x += advanceX;
            y += advanceY;
        }

        state.TextMatrix = PdfMatrix.CreateTranslation(x, y) * state.TextMatrix;
    }

    internal static PdfRectangle GetBounds(double x, double y, double advanceX, double advanceY, double acrossX, double acrossY, double low, double high)
    {
        var x1 = x + (acrossX * low);
        var y1 = y + (acrossY * low);
        var x2 = x + (acrossX * high);
        var y2 = y + (acrossY * high);
        var left = Math.Min(Math.Min(x1, x2), Math.Min(x1, x2) + advanceX);
        var right = Math.Max(Math.Max(x1, x2), Math.Max(x1, x2) + advanceX);
        var bottom = Math.Min(Math.Min(y1, y2), Math.Min(y1, y2) + advanceY);
        var top = Math.Max(Math.Max(y1, y2), Math.Max(y1, y2) + advanceY);
        return new PdfRectangle(left, bottom, right, top);
    }

    private static PdfRectangle Union(ReadOnlySpan<PdfRectangle> rectangles)
    {
        var (left, bottom, right, top) = (rectangles[0].Left, rectangles[0].Bottom, rectangles[0].Right, rectangles[0].Top);
        foreach (var rectangle in rectangles[1..])
        {
            left = Math.Min(left, rectangle.Left);
            bottom = Math.Min(bottom, rectangle.Bottom);
            right = Math.Max(right, rectangle.Right);
            top = Math.Max(top, rectangle.Top);
        }

        return new PdfRectangle(left, bottom, right, top);
    }

    /// <summary>Replaces the Latin ligatures of the Alphabetic Presentation Forms block by their letters, when the options ask for it.</summary>
    private string? ExpandLigatures(string? text)
    {
        if (!_expandLigatures || text is null || text.AsSpan().IndexOfAnyInRange('ﬀ', 'ﬆ') < 0)
            return text;

        var builder = new StringBuilder(text.Length + 4);
        foreach (var c in text)
        {
            builder.Append(c switch
            {
                'ﬀ' => "ff",
                'ﬁ' => "fi",
                'ﬂ' => "fl",
                'ﬃ' => "ffi",
                'ﬄ' => "ffl",
                'ﬅ' => "ſt",
                'ﬆ' => "st",
                _ => null,
            } ?? c.ToString());
        }

        return builder.ToString();
    }

    private void AddGlyph(TextGlyph glyph, PdfRectangle bounds)
    {
        if (_glyphs.Count >= MaxGlyphs)
            throw new PdfResourceLimitException($"The content of the page shows more than {MaxGlyphs} glyphs");

        _textLength += glyph.Text?.Length ?? 0;
        if (_textLength > MaxTextLength)
            throw new PdfResourceLimitException($"The text of the page is longer than {MaxTextLength} characters");

        _glyphs.Add(glyph);
        _bounds?.Add(bounds);
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

    /// <summary>
    /// Joins the glyphs: a new line when a glyph leaves the baseline, goes back or changes direction, a space when a gap
    /// is wider than a fraction of the font size. The lines, the words (the glyphs between spaces) and the boxes of the
    /// glyphs are made when the positions are extracted.
    /// </summary>
    /// <remarks>
    /// A line that has right-to-left text (Hebrew, Arabic...) does not end when its glyphs go back: the files write it
    /// in the order of the text, each glyph or word to the left of the previous one, or in the order of the display,
    /// from left to right. Its glyphs are sorted from left to right, then put in the order of the text by the Unicode
    /// Bidirectional Algorithm (<see cref="OrderRightToLeftLine"/>).
    /// </remarks>
    private (string Text, List<PdfTextLine>? Lines) BuildText(bool layout)
    {
        var builder = new StringBuilder();
        var lines = layout ? new List<PdfTextLine>() : null;
        var line = new List<int>();
        var lineIsRightToLeft = false;
        TextGlyph? previous = null;
        for (var i = 0; i < _glyphs.Count; i++)
        {
            var glyph = _glyphs[i];
            if (glyph.Text is null)
                continue;

            var rightToLeft = ContainsRightToLeft(glyph.Text);
            if (previous is { } last)
            {
                var (along, across) = GetOffset(last, glyph);
                var size = Math.Max(Math.Max(last.Size, glyph.Size), 0.001);
                // In the reading order of the layout, a wide gap also ends the line: it can be the gutter between two
                // columns, and the lines are joined again when no blank space of the page separates them
                var goesBack = along < -size * 2 && !(lineIsRightToLeft || rightToLeft);
                if (Math.Abs(across) > size * 0.5 || goesBack || Math.Abs(glyph.Angle - last.Angle) > 0.01 || (_layoutOrder && along > size * ColumnGap))
                {
                    AppendLine(builder, lines, line, lineIsRightToLeft);
                    builder.Append('\n');
                    line.Clear();
                    lineIsRightToLeft = false;
                }
            }

            line.Add(i);
            lineIsRightToLeft |= rightToLeft;
            previous = glyph;
        }

        AppendLine(builder, lines, line, lineIsRightToLeft);
        return (builder.ToString(), lines);
    }

    /// <summary>Gets the position of a glyph relative to the end of the previous one, along and across the baseline of the previous one.</summary>
    private static (double Along, double Across) GetOffset(TextGlyph last, TextGlyph glyph)
    {
        var cos = Math.Cos(last.Angle);
        var sin = Math.Sin(last.Angle);
        var dx = glyph.X - last.EndX;
        var dy = glyph.Y - last.EndY;
        return ((dx * cos) + (dy * sin), (-dx * sin) + (dy * cos));
    }

    /// <summary>Appends the text of a line, and makes its words and glyphs when the positions are extracted.</summary>
    private void AppendLine(StringBuilder builder, List<PdfTextLine>? lines, List<int> line, bool rightToLeft)
    {
        if (line.Count == 0)
            return;

        // The glyphs in the order of the text, and the spaces between them (-1)
        var items = rightToLeft && !line.Exists(index => _glyphs[index].Font?.IsVertical ?? false) ? OrderRightToLeftLine(line) : AddSpaces(line);
        var lineStart = builder.Length;
        var lineGlyphs = lines is null ? null : new List<PdfTextGlyph>();
        var words = new List<PdfTextWord>();
        var wordGlyphs = new List<PdfTextGlyph>();

        void EndWord()
        {
            if (wordGlyphs.Count > 0)
            {
                words.Add(new PdfTextWord(string.Concat(wordGlyphs.Select(glyph => glyph.Text)), [.. wordGlyphs]));
                wordGlyphs.Clear();
            }
        }

        foreach (var (index, text) in items)
        {
            builder.Append(text);
            if (lineGlyphs is null)
                continue;

            if (index < 0)
            {
                EndWord();
                continue;
            }

            var glyph = _glyphs[index];
            var item = new PdfTextGlyph(text, new PdfPoint(glyph.X, glyph.Y), new PdfPoint(glyph.EndX, glyph.EndY), _bounds![index], glyph.Size, glyph.Font?.FontName, glyph.Angle * 180 / Math.PI, glyph.Font?.IsVertical ?? false);
            lineGlyphs.Add(item);
            if (string.IsNullOrWhiteSpace(text))
            {
                EndWord();
            }
            else
            {
                wordGlyphs.Add(item);
            }
        }

        if (lines is not null)
        {
            EndWord();
            lines.Add(new PdfTextLine(builder.ToString(lineStart, builder.Length - lineStart), [.. words], [.. lineGlyphs!]));
        }
    }

    /// <summary>Keeps the glyphs of a line in the order of the content, with a space where the gap after a glyph is wider than a fraction of the font size.</summary>
    private List<(int Index, string Text)> AddSpaces(List<int> line)
    {
        var result = new List<(int Index, string Text)>(line.Count);
        for (var i = 0; i < line.Count; i++)
        {
            var glyph = _glyphs[line[i]];
            if (i > 0)
            {
                var last = _glyphs[line[i - 1]];
                var (along, _) = GetOffset(last, glyph);
                var size = Math.Max(Math.Max(last.Size, glyph.Size), 0.001);
                if (along > size * 0.15 && !EndsWithSpace(result[^1].Text) && glyph.Text is not [' ', ..])
                {
                    result.Add((-1, " "));
                }
            }

            result.Add((line[i], glyph.Text!));
        }

        return result;
    }

    /// <summary>
    /// Orders a line that has right-to-left text: its glyphs are sorted from left to right along the baseline, with a
    /// space where a gap is wider than a fraction of the font size, which gives the line as it is displayed. The rule L2
    /// of the Unicode Bidirectional Algorithm (UAX #9) applied to the display order gives back the order of the text, as
    /// it reverses the same runs; the brackets of the right-to-left runs get their mirrored character (rule L4). The
    /// direction of the paragraph is the direction of most of the strong characters of the line.
    /// </summary>
    private List<(int Index, string Text)> OrderRightToLeftLine(List<int> line)
    {
        var first = _glyphs[line[0]];
        var cos = Math.Cos(first.Angle);
        var sin = Math.Sin(first.Angle);
        double Start(TextGlyph glyph) => Math.Min((glyph.X * cos) + (glyph.Y * sin), (glyph.EndX * cos) + (glyph.EndY * sin));
        double End(TextGlyph glyph) => Math.Max((glyph.X * cos) + (glyph.Y * sin), (glyph.EndX * cos) + (glyph.EndY * sin));

        // The display order; the sort is stable, so the glyphs at the same position keep the order of the content
        var display = line.OrderBy(index => Start(_glyphs[index])).ToList();
        var items = new List<(int Index, string Text)>(display.Count);
        for (var i = 0; i < display.Count; i++)
        {
            var glyph = _glyphs[display[i]];
            if (i > 0)
            {
                var last = _glyphs[display[i - 1]];
                var size = Math.Max(Math.Max(last.Size, glyph.Size), 0.001);
                if (Start(glyph) - End(last) > size * 0.15 && !EndsWithSpace(items[^1].Text) && !string.IsNullOrWhiteSpace(glyph.Text) && !char.IsWhiteSpace(glyph.Text![0]))
                {
                    items.Add((-1, " "));
                }
            }

            items.Add((display[i], glyph.Text!));
        }

        // The levels of the display text, and the level of each item: the level of its first character
        var text = string.Concat(items.Select(item => item.Text));
        var rightToLeft = 0;
        var leftToRight = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            switch (UnicodeProperties.GetBidiClass(rune.Value))
            {
                case UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic:
                    rightToLeft++;
                    break;
                case UnicodeBidirectionalCategory.LeftToRight:
                    leftToRight++;
                    break;
            }
        }

        var levels = new byte[text.Length];
        UnicodeBidi.ResolveLevels(text, rightToLeft >= leftToRight ? UnicodeBidiDirection.RightToLeft : UnicodeBidiDirection.LeftToRight, levels);
        var itemLevels = new byte[items.Count];
        var position = 0;
        for (var i = 0; i < items.Count; i++)
        {
            itemLevels[i] = levels[position];
            position += items[i].Text.Length;
        }

        var order = new int[items.Count];
        UnicodeBidi.GetVisualOrder(itemLevels, order);
        var result = new List<(int Index, string Text)>(items.Count);
        foreach (var index in order)
        {
            var (glyph, itemText) = items[index];
            if ((itemLevels[index] & 1) != 0 && itemText.Length == 1 && UnicodeProperties.IsBidiMirrored(itemText[0]) && UnicodeProperties.TryGetBidiMirroringGlyph(itemText[0], out var mirrored) && mirrored <= char.MaxValue)
            {
                itemText = ((char)mirrored).ToString();
            }

            result.Add((glyph, itemText));
        }

        return result;
    }

    /// <summary>Indicates whether a text has a strong right-to-left character (bidirectional class R or AL).</summary>
    private static bool ContainsRightToLeft(string text)
    {
        foreach (var rune in text.EnumerateRunes())
        {
            if (rune.Value >= 0x590 && UnicodeProperties.GetBidiClass(rune.Value) is UnicodeBidirectionalCategory.RightToLeft or UnicodeBidirectionalCategory.RightToLeftArabic)
                return true;
        }

        return false;
    }

    private static bool EndsWithSpace(string text) => text.Length > 0 && char.IsWhiteSpace(text[^1]);


    [StructLayout(LayoutKind.Auto)]
    private readonly record struct TextGlyph(string? Text, double X, double Y, double EndX, double EndY, double Size, double Angle, PdfFontDecoder? Font);
}
