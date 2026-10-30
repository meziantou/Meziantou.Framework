using Meziantou.Framework.Imaging.Drawing;
using Meziantou.Framework.Imaging.Typography;

namespace Meziantou.Framework.Imaging.Internals;

/// <summary>
/// Lays out a text: cuts it into paragraphs and lines, shapes it, orders it for display and places its glyphs.
/// </summary>
/// <remarks>
/// <para>The steps, for each paragraph (the text between two mandatory line breaks):</para>
/// <list type="number">
/// <item><description>
/// <b>Direction.</b> The Unicode Bidirectional Algorithm (UAX #9) gives each character an embedding level. Its
/// paragraphs end at a paragraph separator (a line feed, a carriage return, a next line or U+2029): the paragraphs
/// that a vertical tab, a form feed or a line separator end are parts of one paragraph of the algorithm, which gives
/// them its direction.
/// </description></item>
/// <item><description>
/// <b>Items.</b> The paragraph is cut where the level, the script, the font or what a style changes for shaping
/// (its size, its features, its language, its spacing) changes. The font of a character and its marks is the first font of the stack of its style that has them. A change
/// of paint or of decoration does not cut an item: letters of two colors still join and kern.
/// </description></item>
/// <item><description><b>Shaping.</b> Each item is shaped (<see cref="TextShaper"/>); spacing is added.</description></item>
/// <item><description>
/// <b>Lines.</b> A line takes as many of the break opportunities of the Unicode line breaking algorithm (UAX #14) as
/// fit the width, then a hyphenation point, then, when the options allow it, any character. An item that a line
/// break cuts is shaped again on each side: a ligature or a kerning pair never spans two lines.
/// </description></item>
/// <item><description>
/// <b>Order.</b> The items of a line are put in display order (UAX #9, rules L1 and L2); the white space that ends a
/// wrapped line hangs outside its width.
/// </description></item>
/// </list>
/// <para>
/// Then the lines are stacked, aligned in the width of the layout and justified, the text that does not fit is cut and
/// marked with an ellipsis, and the decorations are computed from the positions of the glyphs.
/// </para>
/// </remarks>
[System.Diagnostics.CodeAnalysis.SuppressMessage("Design", "CA1001:Types that own disposable fields should be disposable", Justification = "An engine only lives in Create, which disposes what it rents")]
internal sealed class TextLayoutEngine
{
    private const int SoftHyphen = 0x00AD;

    // The line breaks that are paragraph separators of the bidirectional algorithm (class B, rule P1): a line feed, a
    // carriage return, a next line and a paragraph separator.
    private static readonly System.Buffers.SearchValues<char> BidiParagraphSeparators = System.Buffers.SearchValues.Create("\n\r\u0085\u2029");

    private static readonly uint Vert = ShapingFeature.ToTag("vert");
    private static readonly uint Vrt2 = ShapingFeature.ToTag("vrt2");
    private static readonly uint Vrtr = ShapingFeature.ToTag("vrtr");

    // The forms of a font that are half, a third and a quarter of an em wide
    private static readonly uint[] WidthFeatures = [ShapingFeature.ToTag("hwid"), ShapingFeature.ToTag("twid"), ShapingFeature.ToTag("qwid")];

    // The features of a horizontal line that have a counterpart for a vertical line
    private static readonly (uint Horizontal, uint Vertical)[] DirectionalFeatures =
    [
        (ShapingFeature.ToTag("kern"), ShapingFeature.ToTag("vkrn")),
        (ShapingFeature.ToTag("palt"), ShapingFeature.ToTag("vpal")),
        (ShapingFeature.ToTag("halt"), ShapingFeature.ToTag("vhal")),
        (ShapingFeature.ToTag("chws"), ShapingFeature.ToTag("vchw")),
        (ShapingFeature.ToTag("hkna"), ShapingFeature.ToTag("vkna")),
    ];

    /// <summary>The fewest characters of the text after a line that are shaped again for the next line.</summary>
    private const int MinShapingLength = 64;

    private readonly string _text;
    private readonly TextSpan[] _spans;
    private readonly TextLayoutOptions _options;

    // The hinted glyphs, when the text is hinted: a horizontal layout with TextLayoutOptions.Hinting.
    private readonly TextGlyphHinter? _hinter;
    private readonly ImageResourceLimits _limits;
    private readonly FontResourceLimits _fontLimits;
    private readonly GlyphBuffer _buffer;
    private readonly bool _wraps;

    // What every length of the styles is multiplied by: 1, or the factor a text is fitted to a box with
    private readonly double _scale;
    // The length lines are wrapped at and aligned in, and the room the lines must fit: the width and the height of
    // a horizontal layout, the height and the width of a vertical one
    private readonly double _maxWidth;
    private readonly double _maxHeight;

    // A vertical layout is laid out as a horizontal one, each line read from its top, then turned (see
    // VerticalLayoutData). Its upright glyphs are shaped for a vertical line; the others are on their side.
    private readonly bool _isVertical;
    private readonly TextOrientation _orientation;

    // A vertical line has a central baseline, in its middle, unless all of its text is on its side: it is then a
    // horizontal line turned, with the baseline of one.
    private readonly bool _hasCentralBaseline;
    private readonly Dictionary<(FontFace Face, int CodePoint, bool UsesRotatedForms), bool> _verticalForms = [];
    private readonly List<ShapingFeature> _features = [];
    private GlyphBuffer? _probeBuffer;
    private readonly List<(Font Font, ShapingCache Cache)> _shapingCaches = [];
    private readonly List<Item> _reclaimedItems = [];
    private GlyphScratch? _scratch;
    private readonly List<Font> _fonts = [];
    private readonly List<Line> _lines = [];
    private long _glyphCount;
    private bool _isTruncated;
    private bool _hasMissingGlyphs;

    // The paragraph of the bidirectional algorithm being laid out: the text up to the next paragraph separator. A
    // vertical tab, a form feed and a line separator end a line, not this paragraph.
    private int _bidiStart = -1;
    private int _bidiEnd;
    private byte[] _bidiLevels = [];
    private byte _bidiLevel;

    private TextLayoutEngine(string text, TextSpan[] spans, TextLayoutOptions options, double scale, double maxWidth, double maxHeight)
    {
        _text = text;
        _spans = spans;
        _options = options;
        _scale = scale;
        _isVertical = options.WritingMode != TextWritingMode.Horizontal;
        _orientation = options.Orientation;
        _hasCentralBaseline = _isVertical && _orientation != TextOrientation.Sideways;
        _maxWidth = _isVertical ? maxHeight : maxWidth;
        _maxHeight = _isVertical ? maxWidth : maxHeight;
        var primary = spans[0].Style.Fonts.Primary.Face;
        _limits = ImageFontEnvironment.GetConfiguration(primary).Limits;
        _fontLimits = primary.Data.Limits;
        _buffer = new GlyphBuffer(_fontLimits);
        _wraps = options.Wrapping == TextWrapping.Wrap && !double.IsPositiveInfinity(_maxWidth);
        if (options.Hinting != TextHinting.None && !_isVertical)
        {
            _hinter = new TextGlyphHinter(options.Hinting);
        }
    }

    /// <summary>Lays out a text whose spans follow each other and cover it; an empty text has one span of length 0.</summary>
    /// <exception cref="ImageResourceLimitException">The text, its glyphs or its lines exceed a limit of the configuration of the first font.</exception>
    public static TextLayoutData Create(string text, TextSpan[] spans, TextLayoutOptions options)
        => Create(text, spans, options, scale: 1, options.MaxWidth, options.MaxHeight);

    /// <summary>Lays out a text at a fraction of the sizes of its styles, in a box that is not the one of the options.</summary>
    /// <param name="text">The text.</param>
    /// <param name="spans">The spans of the text.</param>
    /// <param name="options">The options; their box is not used.</param>
    /// <param name="scale">What the sizes, the spacings, the indent and the line height are multiplied by.</param>
    /// <param name="maxWidth">The width of the box: what lines are wrapped and aligned at in a horizontal layout, the room of the lines of a vertical one.</param>
    /// <param name="maxHeight">The height of the box: the room of the lines of a horizontal layout, what lines are wrapped and aligned at in a vertical one.</param>
    /// <exception cref="ImageResourceLimitException">The text, its glyphs or its lines exceed a limit of the configuration of the first font.</exception>
    public static TextLayoutData Create(string text, TextSpan[] spans, TextLayoutOptions options, double scale, double maxWidth, double maxHeight)
    {
        var engine = new TextLayoutEngine(text, spans, options, scale, maxWidth, maxHeight);
        if (text.Length > engine._limits.MaxTextLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TextLength, engine._limits.MaxTextLength, text.Length);

        try
        {
            engine.Run();
            return engine.Finish();
        }
        finally
        {
            engine._hinter?.Dispose();
            engine._scratch?.Dispose();
            foreach (var (font, cache) in engine._shapingCaches)
            {
                font.ReturnShapingCache(cache);
            }
        }
    }

    private void Run()
    {
        // The paragraphs: the text between two mandatory breaks. The break opportunities of the whole text are found
        // once; a paragraph ends with the characters of its line break, which are not shaped.
        var breaks = new List<int>();
        var paragraphStart = 0;
        var strictness = _options.LineBreak switch
        {
            TextLineBreak.Strict => UnicodeLineBreakStrictness.Strict,
            TextLineBreak.Anywhere => UnicodeLineBreakStrictness.Anywhere,
            _ => UnicodeLineBreakStrictness.Normal,
        };
        var wordBreak = _options.WordBreak switch
        {
            TextWordBreak.BreakAll => UnicodeWordBreakBehavior.BreakAll,
            TextWordBreak.KeepAll => UnicodeWordBreakBehavior.KeepAll,
            _ => UnicodeWordBreakBehavior.Normal,
        };
        foreach (var lineBreak in Unicode.EnumerateLineBreaks(_text, _options.LineBreakDictionary, strictness, wordBreak))
        {
            if (!lineBreak.IsMandatory)
            {
                breaks.Add(lineBreak.Index);
                continue;
            }

            var end = lineBreak.Index;
            var contentEnd = end - GetLineBreakLength(_text.AsSpan(paragraphStart, end - paragraphStart));
            breaks.Add(contentEnd);
            if (!LayoutParagraph(new Paragraph(paragraphStart, contentEnd, end, breaks)))
                return;

            breaks = [];
            paragraphStart = end;

            // A line break at the end of the text starts an empty line.
            if (end == _text.Length && contentEnd != end)
            {
                LayoutParagraph(new Paragraph(end, end, end, [end]));
            }
        }

        if (_text.Length == 0)
        {
            LayoutParagraph(new Paragraph(0, 0, 0, [0]));
        }
    }

    /// <summary>Gets the length of the line break that ends a paragraph: one character, or a carriage return and a line feed.</summary>
    private static int GetLineBreakLength(ReadOnlySpan<char> paragraph)
    {
        if (paragraph.Length == 0)
            return 0;

        return paragraph[^1] switch
        {
            '\n' => paragraph.Length >= 2 && paragraph[^2] == '\r' ? 2 : 1,
            '\r' or '\u000B' or '\u000C' or '\u0085' or '\u2028' or '\u2029' => 1,
            _ => 0,
        };
    }

    /// <summary>
    /// Gives the level of the paragraph to the white space and the formatting characters that end the text of a
    /// paragraph, before its line break, as rule L1 does at the end of a line.
    /// </summary>
    private static void ResetTrailingWhiteSpaceLevels(ReadOnlySpan<char> content, byte level, Span<byte> levels)
    {
        var start = content.Length;
        while (start > 0 && Rune.DecodeLastFromUtf16(content[..start], out var rune, out var length) == System.Buffers.OperationStatus.Done
            && UnicodeProperties.GetBidiClass(rune.Value) is UnicodeBidirectionalCategory.WhiteSpace or UnicodeBidirectionalCategory.BoundaryNeutral
                or UnicodeBidirectionalCategory.FirstStrongIsolate or UnicodeBidirectionalCategory.LeftToRightIsolate or UnicodeBidirectionalCategory.RightToLeftIsolate or UnicodeBidirectionalCategory.PopDirectionalIsolate
                or UnicodeBidirectionalCategory.LeftToRightEmbedding or UnicodeBidirectionalCategory.RightToLeftEmbedding or UnicodeBidirectionalCategory.LeftToRightOverride or UnicodeBidirectionalCategory.RightToLeftOverride
                or UnicodeBidirectionalCategory.PopDirectionalFormat)
        {
            start -= length;
        }

        levels[start..content.Length].Fill(level);
    }

    /// <summary>Lays out a paragraph.</summary>
    /// <returns><see langword="false"/> when the layout is full: the rest of the text is not laid out.</returns>
    private bool LayoutParagraph(Paragraph paragraph)
    {
        var content = _text.AsSpan(paragraph.Start, paragraph.End - paragraph.Start);
        var direction = _options.Direction switch
        {
            TextDirection.LeftToRight => UnicodeBidiDirection.LeftToRight,
            TextDirection.RightToLeft => UnicodeBidiDirection.RightToLeft,
            _ => UnicodeBidiDirection.Auto,
        };
        // The levels come from the paragraph of the bidirectional algorithm the paragraph is part of: the text after a
        // line break that does not separate paragraphs keeps the direction of the text before it.
        if (_bidiStart < 0 || paragraph.Start > _bidiEnd)
        {
            var end = _text.AsSpan(paragraph.Start).IndexOfAny(BidiParagraphSeparators);
            _bidiStart = paragraph.Start;
            _bidiEnd = end < 0 ? _text.Length : paragraph.Start + end;
            _bidiLevels = new byte[_bidiEnd - _bidiStart];
            _bidiLevel = (byte)UnicodeBidi.ResolveLevels(_text.AsSpan(_bidiStart, _bidiEnd - _bidiStart), direction, _bidiLevels);
        }

        paragraph.Level = _bidiLevel;
        paragraph.Levels = _bidiLevels.AsSpan(paragraph.Start - _bidiStart, content.Length).ToArray();
        ResetTrailingWhiteSpaceLevels(content, paragraph.Level, paragraph.Levels);
        if (_isVertical && _orientation == TextOrientation.Upright)
        {
            // Upright text is laid out from the top, whatever its script
            paragraph.Level = 0;
            paragraph.Levels.AsSpan().Clear();
        }

        paragraph.Advances = new double[content.Length];
        Itemize(paragraph);

        // A line never ends inside a text set in the room of one upright character
        if (paragraph.CombinedEnds.Length != 0)
        {
            paragraph.Breaks.RemoveAll(position => IsInsideCombined(paragraph, position));
        }

        foreach (var item in paragraph.Items)
        {
            Shape(paragraph, item);
        }

        // A negative indent is a hanging indent: the first line starts at the edge and the lines after it are indented
        // by its magnitude. Every line stays in the box of the layout.
        var firstLineIndent = _options.FirstLineIndent * _scale;
        var from = paragraph.Start;
        var isFirst = true;
        do
        {
            var indent = Math.Max(isFirst ? firstLineIndent : -firstLineIndent, 0);
            var to = paragraph.End;
            var isHyphenated = false;
            if (_wraps && from < paragraph.End)
            {
                to = FindBreak(paragraph, from, _maxWidth - indent, out isHyphenated);
            }

            if (!AddLine(paragraph, from, to, indent, isHyphenated))
                return false;

            from = to;
            isFirst = false;
        }
        while (from < paragraph.End);

        return true;
    }

    /// <summary>Cuts a paragraph into items: the largest parts that have one level, one script, one font and one way of being shaped.</summary>
    private void Itemize(Paragraph paragraph)
    {
        var content = _text.AsSpan(paragraph.Start, paragraph.End - paragraph.Start);
        if (content.IsEmpty)
            return;

        var scripts = new UnicodeScript[content.Length];
        foreach (var run in Unicode.EnumerateScriptRuns(content))
        {
            scripts.AsSpan(run.Index, run.Length).Fill(run.Script);
        }

        // The texts set horizontally in the room of one upright character: each one is one item, of one font, one
        // script and one level, and is shaped in the direction of its first character.
        var combined = FindCombinedRanges(paragraph);
        paragraph.CombinedEnds = combined;
        var combinedRightToLeft = combined.Length == 0 ? [] : new bool[content.Length];

        // The font of each character: the one chosen for its grapheme cluster. In a vertical line, whether the
        // cluster is upright.
        var upright = _isVertical ? new bool[content.Length] : [];
        var fonts = new Font[content.Length];
        for (var i = 0; i < content.Length;)
        {
            if (combined.Length != 0 && combined[i] != 0)
            {
                var end = combined[i] - paragraph.Start;
                var combinedStyle = _spans[FindSpan(paragraph.Start + i)].Style;
                fonts.AsSpan(i, end - i).Fill(SelectFont(combinedStyle.Fonts, content[i..end]));
                var script = scripts[i];
                foreach (var candidate in scripts.AsSpan(i, end - i))
                {
                    if (candidate is not (UnicodeScript.Common or UnicodeScript.Inherited))
                    {
                        script = candidate;
                        break;
                    }
                }

                scripts.AsSpan(i, end - i).Fill(script);
                upright.AsSpan(i, end - i).Fill(value: true);
                combinedRightToLeft[i] = (paragraph.Levels[i] & 1) != 0;
                paragraph.Levels.AsSpan(i, end - i).Fill((byte)(paragraph.Levels[i] + (paragraph.Levels[i] & 1)));
                i = end;
                continue;
            }

            // A cluster that two spans share is cut where the span changes: each span has its own fonts.
            var length = Math.Max(Unicode.GetGraphemeClusterLength(content[i..]), 1);
            var span = _spans[FindSpan(paragraph.Start + i)];
            length = Math.Min(length, span.Start + span.Length - (paragraph.Start + i));
            fonts.AsSpan(i, length).Fill(SelectFont(span.Style.Fonts, content.Slice(i, length)));
            if (_isVertical && !span.IsPlaceholder && IsUpright(fonts[i], content.Slice(i, length), UsesRotatedForms(span.Style)))
            {
                // Upright text goes down the line whatever is around it: among right-to-left text, which goes
                // up, it keeps the order of the text, as a number does
                upright.AsSpan(i, length).Fill(value: true);
                foreach (ref var level in paragraph.Levels.AsSpan(i, length))
                {
                    level += (byte)(level & 1);
                }
            }

            i += length;
        }

        var start = 0;
        var current = FindSpan(paragraph.Start);
        for (var i = 1; i <= content.Length; i++)
        {
            var isEnd = i == content.Length;
            var span = current;
            var nextSpan = span;
            if (!isEnd)
            {
                while (paragraph.Start + i >= _spans[nextSpan].Start + _spans[nextSpan].Length)
                {
                    nextSpan++;
                }
            }

            // A combined text is one item, whatever it holds
            var isCombinedBoundary = !isEnd && combined.Length != 0 && combined[i] != combined[i - 1];
            var isInsideCombined = !isEnd && combined.Length != 0 && combined[i] != 0 && !isCombinedBoundary;
            if (isEnd
                || isCombinedBoundary
                || (!isInsideCombined && (content[i] == '\t' || content[i - 1] == '\t'))
                || paragraph.Levels[i] != paragraph.Levels[start]
                || scripts[i] != scripts[start]
                || !ReferenceEquals(fonts[i], fonts[start])
                || (_isVertical && upright[i] != upright[start])
                || (nextSpan != span && (_spans[span].IsPlaceholder || _spans[nextSpan].IsPlaceholder || !AreShapedTogether(_spans[span].Style, _spans[nextSpan].Style))))
            {
                var style = _spans[span].Style;
                var isCombined = combined.Length != 0 && combined[start] != 0;
                paragraph.Items.Add(new Item
                {
                    Start = paragraph.Start + start,
                    End = paragraph.Start + i,
                    Style = style,
                    Font = fonts[start],
                    Level = paragraph.Levels[start],
                    Script = scripts[start],
                    IsTabulation = content[start] == '\t' && !isCombined,
                    Placeholder = _spans[span].IsPlaceholder ? span : -1,
                    IsUpright = _isVertical && upright[start],
                    IsCombined = isCombined,
                    IsCombinedRightToLeft = isCombined && combinedRightToLeft[start],
                });
                start = i;
            }

            current = nextSpan;
        }
    }

    /// <summary>
    /// Determines whether a grapheme cluster is upright in a vertical line: by the orientation of the layout, or by
    /// the Vertical_Orientation property of its first character (UAX #50).
    /// </summary>
    /// <remarks>
    /// <para>
    /// A character the standard sets on its side unless the font has a vertical form for it (<c>Tr</c>: brackets, the
    /// long vowel mark) is upright when the vertical forms of the font replace its glyph, and on its side otherwise:
    /// its horizontal glyph, turned, is then the form a vertical line needs.
    /// </para>
    /// <para>
    /// With the rotated forms of the font (<c>vrt2</c>), which replace the vertical forms, a character the standard
    /// sets on its side is upright too when the font has a rotated glyph for it: the glyph is already turned.
    /// </para>
    /// </remarks>
    private bool IsUpright(Font font, ReadOnlySpan<char> cluster, bool usesRotatedForms)
    {
        if (_orientation != TextOrientation.Mixed)
            return _orientation == TextOrientation.Upright;

        if (Rune.DecodeFromUtf16(cluster, out var rune, out _) != System.Buffers.OperationStatus.Done)
            return false;

        switch (UnicodeProperties.GetVerticalOrientation(rune.Value))
        {
            case UnicodeVerticalOrientation.Upright or UnicodeVerticalOrientation.TransformedUpright:
                return true;

            case UnicodeVerticalOrientation.TransformedRotated:
                break;

            case UnicodeVerticalOrientation.Rotated when usesRotatedForms:
                break;

            default:
                return false;
        }

        var face = font.Face;
        if (_verticalForms.TryGetValue((face, rune.Value, usesRotatedForms), out var hasVerticalForm))
            return hasVerticalForm;

        // The character alone, shaped for a vertical line: its glyph is another one when the font has a form for it
        var glyph = face.MapCodePoint(rune.Value);
        if (glyph != 0 && face.Gsub is not null)
        {
            var buffer = _probeBuffer ??= new GlyphBuffer(_fontLimits);
            buffer.Clear();
            buffer.Add(rune.Value, 0);
            ReadOnlySpan<ShapingFeature> features = usesRotatedForms ? [new ShapingFeature(Vrt2, 1), new ShapingFeature(Vert, 0)] : [];
            TextShaper.Shape(buffer, face, UnicodeScripts.GetScript(rune.Value), [], isRightToLeft: false, features, isVertical: true, GetScratch(face), GetShapingCache(font));
            hasVerticalForm = buffer.Length == 1 && buffer.Info[0].Glyph != glyph;
        }

        _verticalForms.Add((face, rune.Value, usesRotatedForms), hasVerticalForm);
        return hasVerticalForm;
    }

    /// <summary>Determines whether a style uses the rotated forms of its fonts in vertical lines: the last setting of the feature <c>vrt2</c> turns it on.</summary>
    private static bool UsesRotatedForms(TextStyle style)
    {
        var value = 0;
        foreach (var feature in style.Features)
        {
            if (feature.TagValue == Vrt2)
            {
                value = feature.Value;
            }
        }

        return value != 0;
    }

    /// <summary>
    /// Finds the texts of a paragraph that are set horizontally in the room of one upright character of a vertical line
    /// (tate-chu-yoko): the text of each span of a style that is combined upright, and the short runs of digits the
    /// options combine.
    /// </summary>
    /// <returns>
    /// For each character of the paragraph, the end of the combined text it is in, or 0; empty when the paragraph has
    /// no combined text.
    /// </returns>
    private int[] FindCombinedRanges(Paragraph paragraph)
    {
        if (!_hasCentralBaseline)
            return [];

        var digits = _options.CombineUprightDigits;
        int[]? ends = null;
        for (var index = FindSpan(paragraph.Start); index < _spans.Length && _spans[index].Start < paragraph.End; index++)
        {
            var span = _spans[index];
            var start = Math.Max(span.Start, paragraph.Start);
            var end = Math.Min(span.Start + span.Length, paragraph.End);
            if (span.IsPlaceholder || start >= end)
                continue;

            if (span.Style.IsCombinedUpright)
            {
                ends ??= new int[paragraph.End - paragraph.Start];
                ends.AsSpan(start - paragraph.Start, end - start).Fill(end);
                continue;
            }

            if (digits == 0)
                continue;

            for (var i = start; i < end;)
            {
                if (!char.IsAsciiDigit(_text[i]))
                {
                    i++;
                    continue;
                }

                var runEnd = i + 1;
                while (runEnd < end && char.IsAsciiDigit(_text[runEnd]))
                {
                    runEnd++;
                }

                // A run of digits that continues in another span is not combined, nor one that is too long
                var isWhole = (i == paragraph.Start || !char.IsAsciiDigit(_text[i - 1])) && (runEnd == paragraph.End || !char.IsAsciiDigit(_text[runEnd]));
                if (isWhole && runEnd - i <= digits)
                {
                    ends ??= new int[paragraph.End - paragraph.Start];
                    ends.AsSpan(i - paragraph.Start, runEnd - i).Fill(runEnd);
                }

                i = runEnd;
            }
        }

        return ends ?? [];
    }

    /// <summary>Determines whether a position of a paragraph is inside a text set in the room of one upright character: a line never ends there.</summary>
    private static bool IsInsideCombined(Paragraph paragraph, int position)
        => paragraph.CombinedEnds.Length != 0 && position > paragraph.Start && position < paragraph.End && paragraph.CombinedEnds[position - 1 - paragraph.Start] > position;

    /// <summary>Gets the end of the unit of text at a position: its grapheme cluster, or the text set in the room of one upright character that starts there.</summary>
    private static int GetUnitEnd(Paragraph paragraph, ReadOnlySpan<char> text, int position)
    {
        var index = position - paragraph.Start;
        if ((uint)index < (uint)paragraph.CombinedEnds.Length && paragraph.CombinedEnds[index] != 0)
            return Math.Min(paragraph.CombinedEnds[index], text.Length);

        return position + Math.Max(Unicode.GetGraphemeClusterLength(text[position..]), 1);
    }

    /// <summary>Gets the shaping cache of a font for the layout: the one the font kept from a layout before.</summary>
    private ShapingCache GetShapingCache(Font font)
    {
        foreach (var (cached, cache) in _shapingCaches)
        {
            if (ReferenceEquals(cached, font))
                return cache;
        }

        var rented = font.RentShapingCache();
        _shapingCaches.Add((font, rented));
        return rented;
    }

    private GlyphScratch GetScratch(FontFace face) => _scratch ??= new GlyphScratch(face.Data.Environment.CreateAllocator("TextLayout"));

    /// <summary>Determines whether the texts of two styles can be shaped as one: they only differ by what is painted.</summary>
    private static bool AreShapedTogether(TextStyle first, TextStyle second)
    {
        return ReferenceEquals(first, second)
            || (first.Fonts.FontSpan.SequenceEqual(second.Fonts.FontSpan)
                && ReferenceEquals(first.Fonts.InstalledFallback, second.Fonts.InstalledFallback)
                && first.Size == second.Size
                && first.LetterSpacing == second.LetterSpacing
                && first.WordSpacing == second.WordSpacing
                && first.Synthesis == second.Synthesis
                && first.LanguageTags.AsSpan().SequenceEqual(second.LanguageTags)
                && first.Features.SequenceEqual(second.Features));
    }

    /// <summary>
    /// Chooses the font of a character and what follows it in its cluster: the first font of the stack that has a glyph
    /// for each of them, alone or composed with the one before; else the first font that has the first character; else
    /// the primary font, whose missing glyph is drawn.
    /// </summary>
    /// <remarks>
    /// <para>
    /// An emoji is first looked for in the fonts of the kind it is presented with (<see cref="GetPresentation"/>): the
    /// fonts that have color glyphs for the emoji presentation, the other ones for the text presentation. The stack is
    /// then searched in its order, as for any character.
    /// </para>
    /// <para>
    /// A stack with installed fallback looks for a cluster no font of the stack has among the installed fonts
    /// (<see cref="InstalledFontFallback"/>), before the fonts that only have its first character.
    /// </para>
    /// </remarks>
    private static Font SelectFont(FontStack stack, ReadOnlySpan<char> cluster)
    {
        var fonts = stack.FontSpan;
        var fallback = stack.InstalledFallback;
        if (fonts.Length == 1 && fallback is null)
            return fonts[0];

        var presentation = GetPresentation(cluster);
        if (presentation != EmojiPresentation.None)
        {
            for (var i = 0; i < fonts.Length; i++)
            {
                if (fonts[i].HasColorGlyphs == (presentation == EmojiPresentation.Emoji) && HasCluster(fonts[i].Face, cluster, out _))
                    return fonts[i];
            }
        }

        var withFirst = -1;
        for (var i = 0; i < fonts.Length; i++)
        {
            if (HasCluster(fonts[i].Face, cluster, out var hasFirst))
                return fonts[i];

            if (hasFirst && withFirst < 0)
            {
                withFirst = i;
            }
        }

        if (fallback is not null)
        {
            if (fallback.Find(cluster, presentation == EmojiPresentation.Emoji, out var installedWithFirst) is { } installed)
                return installed;

            if (withFirst < 0 && installedWithFirst is not null)
                return installedWithFirst;
        }

        return fonts[Math.Max(withFirst, 0)];
    }

    /// <summary>Determines whether a font has a glyph for each character of a cluster, alone or composed with the one before.</summary>
    /// <param name="face">The font.</param>
    /// <param name="cluster">The cluster.</param>
    /// <param name="hasFirst">Whether the font has the first character of the cluster that needs a glyph.</param>
    internal static bool HasCluster(FontFace face, ReadOnlySpan<char> cluster, out bool hasFirst)
    {
        hasFirst = false;
        var hasAll = true;
        var previous = -1;
        var isFirst = true;
        foreach (var rune in cluster.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (!NeedsGlyph(codePoint))
                continue;

            var hasGlyph = face.MapCodePoint(codePoint) != 0
                || (previous >= 0 && UnicodeNormalization.TryComposeCanonical(previous, codePoint, out var composite) && face.MapCodePoint(composite) != 0);
            hasFirst |= isFirst && hasGlyph;
            hasAll &= hasGlyph;
            previous = codePoint;
            isFirst = false;
        }

        return hasAll;
    }

    /// <summary>Determines how the emoji a cluster starts with is presented: with colors or as text.</summary>
    /// <remarks>
    /// <para>
    /// Unicode Technical Standard #51, "Presentation style": an emoji character is drawn as an emoji, with colors, or
    /// as text, like a letter. A variation selector after it says which (U+FE0F for the emoji presentation, U+FE0E
    /// for the text presentation); without one, the character has a default presentation (its Emoji_Presentation
    /// property), and the sequences that are emoji (a character with a skin tone modifier, characters joined by a zero
    /// width joiner) are drawn as emoji.
    /// </para>
    /// <para>
    /// A character whose default presentation is text and that no selector follows has no preference here: the copyright
    /// sign and the digits are emoji characters, and are drawn with the font of the text around them.
    /// </para>
    /// </remarks>
    private static EmojiPresentation GetPresentation(ReadOnlySpan<char> cluster)
    {
        const int TextSelector = 0xFE0E;
        const int EmojiSelector = 0xFE0F;
        const int ZeroWidthJoiner = 0x200D;
        if (Rune.DecodeFromUtf16(cluster, out var first, out var consumed) != System.Buffers.OperationStatus.Done || !UnicodeEmoji.IsEmoji(first))
            return EmojiPresentation.None;

        var isFirst = true;
        var isSequence = false;
        foreach (var rune in cluster[consumed..].EnumerateRunes())
        {
            // The selector of the first character decides; the other characters of a sequence have their own.
            if (isFirst && rune.Value == TextSelector)
                return EmojiPresentation.Text;

            isSequence |= rune.Value is EmojiSelector or ZeroWidthJoiner || UnicodeEmoji.IsEmojiModifier(rune);
            isFirst = false;
        }

        return isSequence || UnicodeEmoji.HasEmojiPresentation(first) ? EmojiPresentation.Emoji : EmojiPresentation.None;
    }

    /// <summary>Shapes an item and records the advance of each of its characters.</summary>
    private void Shape(Paragraph paragraph, Item item, string? suffix = null)
    {
        item.IsPending = false;
        item.NeedsShaping = false;
        item.GlyphCount = 0;
        ReclaimGlyphs(paragraph, item.End - item.Start + (suffix?.Length ?? 0));
        item.GlyphStart = paragraph.Glyphs.Count;
        paragraph.Advances.AsSpan(item.Start - paragraph.Start, item.End - item.Start).Clear();
        if (item.IsTabulation)
            return;

        if (item.Placeholder >= 0)
        {
            // What the caller draws there has no glyph: it takes its width, or its height in a vertical line
            var placeholder = _spans[item.Placeholder];
            paragraph.Advances[item.Start - paragraph.Start] = (_hasCentralBaseline ? placeholder.PlaceholderHeight : placeholder.PlaceholderWidth) * _scale;
            return;
        }

        if (item.IsCombined)
        {
            ShapeCombined(paragraph, item);
            return;
        }

        FillBuffer(item, suffix);
        if (_buffer.Length == 0)
            return;

        var style = item.Style;
        var isRightToLeft = (item.Level & 1) != 0;
        var features = GetFeatures(style, !_isVertical ? ShapingOrientation.Horizontal : item.IsUpright ? ShapingOrientation.Upright : ShapingOrientation.Sideways);
        TextShaper.Shape(_buffer, item.Font.Face, item.Script, style.LanguageTags, isRightToLeft, features, item.IsUpright, item.IsUpright ? GetScratch(item.Font.Face) : null, GetShapingCache(item.Font));
        var size = GetSize(style);
        var scale = size / item.Font.UnitsPerEm;
        var info = _buffer.Info;
        var positions = _buffer.Positions;
        for (var i = 0; i < info.Length; i++)
        {
            // An upright glyph of a vertical line advances down the line, and is drawn from its vertical origin: in
            // the coordinates of the line, what the font gives along its y axis is along the line, backward, and
            // what it gives along its x axis is across the line, toward the side the top of the line is on.
            var glyph = new ShapedGlyph
            {
                Glyph = info[i].Glyph,
                Cluster = info[i].Cluster,
                Advance = (item.IsUpright ? -positions[i].YAdvance : positions[i].XAdvance) * scale,
                XOffset = (item.IsUpright ? -positions[i].YOffset : positions[i].XOffset) * scale,
                YOffset = (item.IsUpright ? positions[i].XOffset : positions[i].YOffset) * scale,
                IsInvisible = info[i].IsIgnorable,
                WidthScale = 1,
            };

            // A glyph hinted with the instructions of its font advances by its hinted advance, a whole number of
            // pixels, plus what the positioning of the text adds, rounded; its offsets are rounded too.
            if (_hinter is not null && _hinter.TryGetAdvance(item.Font, size, glyph.Glyph, out var hintedAdvance))
            {
                glyph.Advance = hintedAdvance + Math.Round((positions[i].XAdvance - item.Font.Face.GetAdvanceWidth(glyph.Glyph)) * scale, MidpointRounding.AwayFromZero);
                glyph.XOffset = Math.Round(glyph.XOffset, MidpointRounding.AwayFromZero);
                glyph.YOffset = Math.Round(glyph.YOffset, MidpointRounding.AwayFromZero);
            }

            paragraph.Glyphs.Add(glyph);
        }

        item.GlyphCount = info.Length;
        var glyphs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Glyphs).Slice(item.GlyphStart, item.GlyphCount);
        if ((style.Synthesis & FontSynthesis.Bold) != FontSynthesis.None)
        {
            Embolden(glyphs, GetSize(style) * GlyphRun.BoldWidth, isRightToLeft);
        }

        if (style.LetterSpacing != 0 || style.WordSpacing != 0)
        {
            AddSpacing(glyphs, style, isRightToLeft);
        }

        // An item without character, which only holds what a line adds, has no advance to record.
        if (item.End == item.Start)
            return;

        foreach (ref readonly var glyph in glyphs)
        {
            paragraph.Advances[Math.Clamp(glyph.Cluster, item.Start, item.End - 1) - paragraph.Start] += glyph.Advance;
        }
    }

    /// <summary>
    /// Makes room for the glyphs of an item that is shaped: an item shaped again leaves its former glyphs in the list of
    /// the paragraph, so before the list grows, the glyphs that no item of the paragraph refers to any more are dropped
    /// when they are most of it. The glyphs of the other items keep their order.
    /// </summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="count">The number of glyphs about to be added.</param>
    private void ReclaimGlyphs(Paragraph paragraph, int count)
    {
        var glyphs = paragraph.Glyphs;
        if (glyphs.Count + (long)count <= glyphs.Capacity)
            return;

        var live = 0;
        var items = _reclaimedItems;
        items.Clear();
        foreach (var item in paragraph.Items)
        {
            if (item.GlyphCount > 0)
            {
                live += item.GlyphCount;
                items.Add(item);
            }
        }

        if (live * 2L > glyphs.Count)
            return;

        // The glyphs of an item never overlap those of another one: moving them down in the order of their start
        // never overwrites glyphs that are still to be moved.
        items.Sort((first, second) => first.GlyphStart.CompareTo(second.GlyphStart));
        var storage = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(glyphs);
        var next = 0;
        foreach (var item in items)
        {
            storage.Slice(item.GlyphStart, item.GlyphCount).CopyTo(storage[next..]);
            item.GlyphStart = next;
            next += item.GlyphCount;
        }

        glyphs.RemoveRange(next, glyphs.Count - next);
        items.Clear();
    }

    /// <summary>Puts the characters of an item in the buffer of the shaper, with what a line adds after its last character.</summary>
    private void FillBuffer(Item item, string? suffix)
    {
        _buffer.Clear();
        var text = _text.AsSpan(item.Start, item.End - item.Start);
        var lastCluster = item.Start;
        for (var i = 0; i < text.Length;)
        {
            Rune.DecodeFromUtf16(text[i..], out var rune, out var consumed);
            if (!IsControl(rune.Value))
            {
                lastCluster = item.Start + i;
                _buffer.Add(rune.Value, lastCluster);
            }

            i += consumed;
        }

        // What a line adds after its last character (a hyphen, an ellipsis) belongs to the cluster of that character.
        if (suffix is not null)
        {
            foreach (var rune in suffix.EnumerateRunes())
            {
                _buffer.Add(rune.Value, lastCluster);
            }
        }
    }

    /// <summary>
    /// Gets the features a run is shaped with: those of its style and, in a vertical line, those of its orientation.
    /// </summary>
    /// <remarks>
    /// In a vertical line, the features of a style that have a counterpart in the other direction are applied as
    /// that counterpart where the glyphs go the other way: <c>kern</c> is <c>vkrn</c> for upright glyphs, and
    /// <c>vkrn</c> is <c>kern</c> for the glyphs of a horizontal run. The forms of upright glyphs (<c>vert</c>,
    /// <c>vrt2</c>) are not applied to the glyphs of a horizontal run, and the forms of turned glyphs (<c>vrtr</c>)
    /// are only applied to the glyphs that are turned on their side: by default, unless the style uses the rotated
    /// forms of the font (<c>vrt2</c>), which replace both the vertical forms and the turning of the glyphs.
    /// </remarks>
    private ReadOnlySpan<ShapingFeature> GetFeatures(TextStyle style, ShapingOrientation orientation)
    {
        _features.Clear();
        if (orientation == ShapingOrientation.Horizontal)
        {
            foreach (var feature in style.Features)
            {
                _features.Add(new ShapingFeature(feature.TagValue, feature.Value));
            }

            return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_features);
        }

        var isUpright = orientation == ShapingOrientation.Upright;
        var usesRotatedForms = UsesRotatedForms(style);
        if (orientation == ShapingOrientation.Sideways && !usesRotatedForms)
        {
            _features.Add(new ShapingFeature(Vrtr, 1));
        }

        foreach (var feature in style.Features)
        {
            var tag = feature.TagValue;
            if (isUpright ? tag == Vrtr : tag == Vert || tag == Vrt2 || (orientation == ShapingOrientation.Combined && tag == Vrtr))
                continue;

            foreach (var (horizontal, vertical) in DirectionalFeatures)
            {
                if (tag == (isUpright ? horizontal : vertical))
                {
                    tag = isUpright ? vertical : horizontal;
                    break;
                }
            }

            _features.Add(new ShapingFeature(tag, feature.Value));
        }

        if (isUpright && usesRotatedForms)
        {
            _features.Add(new ShapingFeature(Vert, 0));
        }

        return System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_features);
    }

    /// <summary>
    /// Shapes a text that is set horizontally in the room of one upright character of a vertical line
    /// (tate-chu-yoko): its glyphs are those of a horizontal line, centered across the line and on the middle of one
    /// em down the line, and they are one cluster that advances by one em.
    /// </summary>
    /// <remarks>
    /// A text wider than one em takes the forms its font has for half, a third or a quarter of an em when it has two,
    /// three or four characters, and is narrowed to one em if it is still too wide. In the coordinates of the line,
    /// what is along the baseline of the glyphs is across the line, toward its top side, and what is above it is
    /// toward the top of the line.
    /// </remarks>
    private void ShapeCombined(Paragraph paragraph, Item item)
    {
        FillBuffer(item, suffix: null);
        if (_buffer.Length == 0)
            return;

        var style = item.Style;
        var face = item.Font.Face;
        var size = GetSize(style);
        var scale = size / item.Font.UnitsPerEm;
        var characterCount = _buffer.Length;
        var features = GetFeatures(style, ShapingOrientation.Combined);
        TextShaper.Shape(_buffer, face, item.Script, style.LanguageTags, item.IsCombinedRightToLeft, features, isVertical: false, scratch: null, GetShapingCache(item.Font));
        var width = GetBufferWidth() * scale;
        if (width > size && characterCount is >= 2 and <= 4)
        {
            _features.Add(new ShapingFeature(WidthFeatures[characterCount - 2], 1));
            FillBuffer(item, suffix: null);
            TextShaper.Shape(_buffer, face, item.Script, style.LanguageTags, item.IsCombinedRightToLeft, System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_features), isVertical: false, scratch: null, GetShapingCache(item.Font));
            width = GetBufferWidth() * scale;
        }

        var widthScale = width > size ? size / width : 1;
        var central = GetCentral(item.Font, size, item.Script);
        var info = _buffer.Info;
        var positions = _buffer.Positions;
        var pen = 0.0;
        for (var i = 0; i < info.Length; i++)
        {
            paragraph.Glyphs.Add(new ShapedGlyph
            {
                Glyph = info[i].Glyph,
                Cluster = item.Start,
                Advance = i == info.Length - 1 ? size : 0,
                XOffset = (size / 2) + central - (positions[i].YOffset * scale),
                YOffset = ((pen + positions[i].XOffset) * scale * widthScale) - (width * widthScale / 2),
                IsInvisible = info[i].IsIgnorable,
                WidthScale = widthScale,
            });
            pen += positions[i].XAdvance;
        }

        item.GlyphCount = info.Length;
        paragraph.Advances[item.Start - paragraph.Start] = size;

        double GetBufferWidth()
        {
            var advance = 0;
            foreach (ref readonly var position in _buffer.Positions)
            {
                advance += position.XAdvance;
            }

            return advance;
        }
    }

    /// <summary>
    /// Gets how far above the baseline of a font its middle is, in a line with a central baseline: the middle of the
    /// ideographic em box the <c>BASE</c> table of the font gives for the script, or else the middle of its ascent
    /// and its descent.
    /// </summary>
    private static double GetCentral(Font font, double size, UnicodeScript script)
    {
        Span<uint> scripts = stackalloc uint[OpenTypeScript.MaxTagCount];
        scripts = scripts[..OpenTypeScript.GetTags(script, scripts)];
        if (font.Face.TryGetIdeographicCenter(isVertical: false, scripts, out var center))
            return center * size / font.UnitsPerEm;

        var metrics = font.GetMetrics(size);
        return (metrics.Ascent - metrics.Descent) / 2;
    }

    /// <summary>
    /// Makes room for the glyphs a synthetic bold thickens: a glyph that takes room takes the width of the bold more,
    /// and is drawn in the middle of it. A glyph that takes no room is a mark of the glyph it is drawn on, which is the
    /// one before it in the text: it moves with that glyph, whose advance it follows in a left-to-right run and
    /// precedes in a right-to-left run, where the glyphs are in display order.
    /// </summary>
    private static void Embolden(Span<ShapedGlyph> glyphs, double width, bool isRightToLeft)
    {
        foreach (ref var glyph in glyphs)
        {
            if (glyph.Advance != 0)
            {
                glyph.Advance += width;
                glyph.XOffset += width / 2;
            }
            else
            {
                glyph.XOffset += isRightToLeft ? width / 2 : -(width / 2);
            }
        }
    }

    /// <summary>
    /// Adds the letter spacing after each cluster and the word spacing to each space. The space is added on the side
    /// the text continues to: after the last glyph of a cluster in a left-to-right run; in a right-to-left run, where
    /// the glyphs are in display order, before its first glyph, which is moved with it.
    /// </summary>
    private void AddSpacing(Span<ShapedGlyph> glyphs, TextStyle style, bool isRightToLeft)
    {
        for (var start = 0; start < glyphs.Length;)
        {
            var end = start + 1;
            while (end < glyphs.Length && glyphs[end].Cluster == glyphs[start].Cluster)
            {
                end++;
            }

            // An ellipsis on an empty line at the end of the text has the cluster of a character that is not there.
            var character = glyphs[start].Cluster < _text.Length ? _text[glyphs[start].Cluster] : '\0';
            var space = (style.LetterSpacing + (character is ' ' or '\u00A0' ? style.WordSpacing : 0)) * _scale;
            if (isRightToLeft)
            {
                glyphs[start].Advance += space;
                glyphs[start].XOffset += space;
            }
            else
            {
                glyphs[end - 1].Advance += space;
            }

            start = end;
        }
    }

    /// <summary>Finds where the line that starts at a character ends, so that it fits a width.</summary>
    private int FindBreak(Paragraph paragraph, int from, double available, out bool isHyphenated)
    {
        isHyphenated = false;
        var withHyphens = _options.Hyphenation != TextHyphenation.None;
        var width = 0.0;
        var contentWidth = 0.0;
        var previous = from;
        var lastFit = -1;
        var lastFitIsHyphenated = false;
        var breaks = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Breaks);
        foreach (var candidate in breaks[FindFirstBreakAfter(breaks, from)..])
        {
            // A soft hyphen is a place to break only when words are hyphenated, and the line then ends with a hyphen.
            var isSoftHyphen = candidate < paragraph.End && _text[candidate - 1] == SoftHyphen;
            if (isSoftHyphen && !withHyphens)
                continue;

            width = Measure(paragraph, previous, candidate, width, ref contentWidth);
            previous = candidate;
            var lineWidth = contentWidth + (isSoftHyphen ? MeasureHyphen(paragraph, candidate) : 0);
            if (lineWidth <= available)
            {
                lastFit = candidate;
                lastFitIsHyphenated = isSoftHyphen;
                continue;
            }

            // A hyphen that does not fit is not a place to break: the word continues.
            if (isSoftHyphen)
                continue;

            // The text up to this opportunity does not fit. A word can be hyphenated between the last opportunity that
            // fits and this one.
            if (_options.Hyphenation == TextHyphenation.Automatic)
            {
                var hyphenation = FindHyphenation(paragraph, from, Math.Max(lastFit, from), candidate, available);
                if (hyphenation > from)
                {
                    isHyphenated = true;
                    return hyphenation;
                }
            }

            if (lastFit > from)
                break;

            // Not even the first word fits: it is cut where the options allow, or left to overflow.
            if (_options.OverflowWrap == TextOverflowWrap.Anywhere)
                return FindClusterBreak(paragraph, from, candidate, available);

            return candidate;
        }

        isHyphenated = lastFitIsHyphenated;
        return lastFit > from ? lastFit : paragraph.End;
    }

    /// <summary>Adds the advances of the characters of a range to a width; a tabulation goes to the next stop.</summary>
    private double Measure(Paragraph paragraph, int from, int to, double width)
    {
        for (var i = from; i < to; i++)
        {
            width += _text[i] == '\t' ? GetTabulationAdvance(_spans[FindSpan(i)].Style, width) : paragraph.Advances[i - paragraph.Start];
        }

        return width;
    }

    /// <summary>
    /// Adds the advances of the characters of a range to a width, and keeps the width at the end of its last character
    /// that is not white space: the white space that ends a wrapped line hangs. A tabulation has no stored advance, so
    /// its width is only known from where it is.
    /// </summary>
    private double Measure(Paragraph paragraph, int from, int to, double width, ref double contentWidth)
    {
        for (var i = from; i < to; i++)
        {
            width = Measure(paragraph, i, i + 1, width);
            if (!IsBreakingSpace(_text[i]) || IsInsideCombined(paragraph, i))
            {
                contentWidth = width;
            }
        }

        return width;
    }

    private double GetTabulationAdvance(TextStyle style, double position)
    {
        var font = style.Fonts.Primary;
        var space = font.Face.MapCodePoint(' ');
        var tabulation = _options.TabSize * (space == 0 ? font.UnitsPerEm / 4.0 : font.Face.GetAdvanceWidth(space)) * (GetSize(style) / font.UnitsPerEm);
        return tabulation > 0 ? ((Math.Floor(position / tabulation) + 1) * tabulation) - position : 0;
    }

    /// <summary>Gets the width of the hyphen a line would end with after a character.</summary>
    private double MeasureHyphen(Paragraph paragraph, int position)
    {
        var item = FindItem(paragraph, position - 1);
        if (item is null)
            return 0;

        var face = item.Font.Face;
        var scale = GetSize(item.Style) / item.Font.UnitsPerEm;
        var width = 0.0;
        foreach (var rune in GetHyphen(item).EnumerateRunes())
        {
            width += (GetAdvance(item, face.MapCodePoint(rune.Value)) * scale) + GetEmboldenWidth(item.Style);
        }

        return width;
    }

    /// <summary>Gets what a synthetic bold adds to the advance of a glyph of a style.</summary>
    private double GetEmboldenWidth(TextStyle style) => (style.Synthesis & FontSynthesis.Bold) != FontSynthesis.None ? GetSize(style) * GlyphRun.BoldWidth : 0;

    /// <summary>Gets the advance of a glyph along the line of an item, in font units: its height when the item is upright in a vertical line.</summary>
    private static int GetAdvance(Item item, int glyph) => item.IsUpright ? item.Font.Face.GetAdvanceHeight(glyph) : item.Font.Face.GetAdvanceWidth(glyph);

    /// <summary>Gets the size the fonts of a style have in the layout.</summary>
    private double GetSize(TextStyle style) => style.Size * _scale;

    private string GetHyphen(Item item) => _options.HyphenCharacter ?? (item.Font.Face.MapCodePoint(0x2010) != 0 ? "\u2010" : "-");

    private string GetEllipsis(Item item) => _options.Ellipsis ?? (item.Font.Face.MapCodePoint(0x2026) != 0 ? "\u2026" : "...");

    /// <summary>
    /// Finds the last place a word can be hyphenated so that the line fits, between the last break opportunity that fits
    /// and the first one that does not.
    /// </summary>
    private int FindHyphenation(Paragraph paragraph, int from, int start, int end, double available)
    {
        var best = -1;
        var segment = _text.AsSpan(start, end - start);
        Span<int> positions = stackalloc int[64];
        foreach (var range in Unicode.EnumerateWordBoundaries(segment))
        {
            var word = segment[range];
            if (word.Length < 2 || word.Length > positions.Length || !char.IsLetter(word[0]))
                continue;

            var wordStart = start + range.Start.GetOffset(segment.Length);
            if (_spans[FindSpan(wordStart)].Style.Hyphenator is not { } hyphenator)
                continue;

            var count = hyphenator.Hyphenate(word, positions);
            for (var i = 0; i < count; i++)
            {
                var position = wordStart + positions[i];
                if (position > best && !IsInsideCombined(paragraph, position) && Measure(paragraph, from, position, 0) + MeasureHyphen(paragraph, position) <= available)
                {
                    best = position;
                }
            }
        }

        return best;
    }

    /// <summary>Finds the last cluster boundary before a position that fits a width; a line has at least one cluster.</summary>
    private int FindClusterBreak(Paragraph paragraph, int from, int end, double available)
    {
        var best = -1;
        var width = 0.0;
        var position = from;
        var text = _text.AsSpan(0, end);
        while (position < end)
        {
            var next = GetUnitEnd(paragraph, text, position);
            width = Measure(paragraph, position, next, width);
            if (width > available && best > from)
                break;

            best = next;
            position = next;
            if (width > available)
                break;
        }

        return best;
    }

    /// <summary>Builds the line of a range of a paragraph and adds it to the layout.</summary>
    /// <returns><see langword="false"/> when the layout is full.</returns>
    private bool AddLine(Paragraph paragraph, int from, int to, double indent, bool isHyphenated)
    {
        var isWrapped = to < paragraph.End;
        var line = BuildLine(paragraph, from, to, indent, isHyphenated ? LineEnd.Hyphen : LineEnd.None, isWrapped);
        var maxLines = Math.Min(_options.MaxLines, _limits.MaxTextLines);
        var isOverflowing = _lines.Count > 0 && GetHeight() + line.Height > _maxHeight;
        if (_lines.Count >= maxLines || isOverflowing)
        {
            // The line does not fit. A count of lines the caller asks for and a height cut the text; the limit of the
            // configuration is an error.
            if (!isOverflowing && _lines.Count >= _limits.MaxTextLines && _options.MaxLines > _limits.MaxTextLines)
                throw new ImageResourceLimitException(ImageResourceLimitKind.TextLines, _limits.MaxTextLines, (long)_lines.Count + 1);

            _isTruncated = true;
            if (_options.Trimming != TextTrimming.None)
            {
                // The last line that fits ends with the ellipsis.
                var last = _lines[^1];
                _lines[^1] = Trim(last.Paragraph, last.From, last.To, last.Indent, last.Y);
            }

            return false;
        }

        // A line that is wider than the layout, because lines are not wrapped or a word does not fit, is trimmed too.
        if (_options.Trimming != TextTrimming.None && line.Width + indent > _maxWidth)
        {
            _isTruncated = true;
            line = Trim(paragraph, from, to, indent, GetHeight());
        }

        line.Y = GetHeight();
        _lines.Add(line);
        return true;
    }

    private double GetHeight() => _lines.Count == 0 ? 0 : _lines[^1].Y + _lines[^1].Height;

    /// <summary>Builds a line that ends with the ellipsis after the last character, or the last word, that leaves room for it.</summary>
    private Line Trim(Paragraph paragraph, int from, int to, double indent, double y)
    {
        var available = _maxWidth - indent;
        var best = from;
        if (double.IsPositiveInfinity(available))
        {
            best = to;
        }
        else
        {
            var position = from;
            var width = 0.0;
            var contentWidth = 0.0;
            var text = _text.AsSpan(0, to);
            while (position < to)
            {
                var next = _options.Trimming == TextTrimming.Word
                    ? NextBreak(paragraph, position, to)
                    : GetUnitEnd(paragraph, text, position);
                width = Measure(paragraph, position, next, width, ref contentWidth);
                var item = FindItem(paragraph, next - 1) ?? FindItem(paragraph, from);
                var ellipsis = item is null ? 0 : MeasureText(item, GetEllipsis(item));
                if (contentWidth + ellipsis > available)
                    break;

                best = next;
                position = next;
            }
        }

        // The spaces before the ellipsis are dropped: it follows the last word.
        while (best > from && IsBreakingSpace(_text[best - 1]) && !IsInsideCombined(paragraph, best - 1))
        {
            best--;
        }

        var line = BuildLine(paragraph, from, best, indent, LineEnd.Ellipsis, isWrapped: false);
        line.To = to;
        line.IsLastOfParagraph = to >= paragraph.End;
        line.Y = y;
        return line;

        static int NextBreak(Paragraph paragraph, int position, int to)
        {
            var breaks = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Breaks);
            var index = FindFirstBreakAfter(breaks, position);
            return index < breaks.Length ? Math.Min(breaks[index], to) : to;
        }
    }

    private double MeasureText(Item item, string text)
    {
        var face = item.Font.Face;
        var scale = GetSize(item.Style) / item.Font.UnitsPerEm;
        var width = 0.0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += (GetAdvance(item, face.MapCodePoint(rune.Value)) * scale) + GetEmboldenWidth(item.Style);
        }

        return width;
    }

    /// <summary>Builds the line of a range of a paragraph: its glyphs in display order, from the start of its text.</summary>
    private Line BuildLine(Paragraph paragraph, int from, int to, double indent, LineEnd end, bool isWrapped)
    {
        // An item the line ends inside is cut in two, and each part is shaped again: the part in the line once it has
        // its final extent, below.
        SplitAt(paragraph, to, from);
        SplitAt(paragraph, from, int.MaxValue);

        // The white space that ends a wrapped line hangs: it is not part of the width, and it follows the direction of
        // the paragraph whatever the direction of the text before it (rule L1).
        var contentEnd = to;
        if (isWrapped && end == LineEnd.None)
        {
            while (contentEnd > from && IsBreakingSpace(_text[contentEnd - 1]) && !IsInsideCombined(paragraph, contentEnd - 1))
            {
                contentEnd--;
            }

            SplitAt(paragraph, contentEnd, from);
        }

        // The text after the line is shaped from its start, for the next line to be found; a part of the line that
        // was shaped for an earlier line, or not yet, is shaped with the rest of its run: a line is shaped as a whole.
        // The items before the line are not looked at.
        var first = FindFirstItemFrom(paragraph, from);
        for (var i = first; i < paragraph.Items.Count; i++)
        {
            var item = paragraph.Items[i];
            if (item.Start == to && item.IsPending)
            {
                ShapeStart(paragraph, i);
                break;
            }

            if (item.Start < from || item.End > to || item.Start >= item.End)
                continue;

            var isMerged = false;
            while (i + 1 < paragraph.Items.Count && paragraph.Items[i + 1] is var next && next.Start == item.End && next.End <= to && next.Start != contentEnd && next.Start < next.End && IsSameRun(item, next))
            {
                item.End = next.End;
                paragraph.Items.RemoveAt(i + 1);
                isMerged = true;
            }

            if (isMerged || item.IsPending || item.NeedsShaping)
            {
                Shape(paragraph, item);
            }
        }

        var items = new List<Item>();
        for (var i = first; i < paragraph.Items.Count && paragraph.Items[i].Start < to; i++)
        {
            var item = paragraph.Items[i];
            if (item.Start >= from && item.End <= to && item.Start < item.End)
            {
                items.Add(item);
            }
        }

        // What the line adds after its last character is shaped with it. A line without character borrows the style of
        // the place it is at.
        if (end != LineEnd.None)
        {
            if (items.Count == 0)
            {
                var style = _spans[FindSpan(Math.Min(from, Math.Max(_text.Length - 1, 0)))].Style;
                items.Add(new Item { Start = from, End = from, Style = style, Font = style.Fonts.Primary, Level = paragraph.Level, Script = UnicodeScript.Common });
            }

            var last = items[^1];
            var suffix = end == LineEnd.Hyphen ? GetHyphen(last) : GetEllipsis(last);
            if (last.IsCombined)
            {
                // What the line adds follows a text set in the room of one upright character: it is not part of it
                last = new Item { Start = last.End, End = last.End, Style = last.Style, Font = last.Font, Level = last.Level, Script = UnicodeScript.Common, IsUpright = IsUpright(last.Font, suffix, UsesRotatedForms(last.Style)) };
                items.Add(last);
            }

            Shape(paragraph, last, suffix);
        }

        var line = new Line
        {
            Paragraph = paragraph,
            From = from,
            To = to,
            Indent = indent,
            IsRightToLeft = (paragraph.Level & 1) != 0,
            IsLastOfParagraph = to >= paragraph.End,
        };

        // The advance of each tabulation, in the order of the text.
        var position = 0.0;
        Span<double> tabulations = items.Count <= 64 ? stackalloc double[items.Count] : new double[items.Count];
        Span<byte> levels = items.Count <= 64 ? stackalloc byte[items.Count] : new byte[items.Count];
        Span<int> order = items.Count <= 64 ? stackalloc int[items.Count] : new int[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[i];
            levels[i] = item.Start >= contentEnd ? paragraph.Level : item.Level;
            if (item.IsTabulation)
            {
                tabulations[i] = GetTabulationAdvance(item.Style, position);
                position += tabulations[i];
            }
            else
            {
                position = Measure(paragraph, item.Start, item.End, position);
            }
        }

        UnicodeBidi.GetVisualOrder(levels, order);
        var pen = 0.0;
        var hanging = 0.0;
        var glyphs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Glyphs);

        // A tabulation and a placeholder are one glyph of the line each.
        var glyphCount = 0;
        foreach (var item in items)
        {
            glyphCount += item.IsTabulation || item.Placeholder >= 0 ? 1 : item.GlyphCount;
        }

        line.Glyphs.Capacity = glyphCount;
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[order[i]];
            var font = GetFontIndex(item.Font);
            var isHanging = item.Start >= contentEnd && item.Start < item.End;
            var central = IncludeMetrics(line, item.Font, GetSize(item.Style), item.Script);
            var baselineShift = item.IsUpright ? 0 : central;
            if (item.IsTabulation)
            {
                line.Glyphs.Add(new PlacedGlyph { Glyph = -1, Cluster = item.Start, PenX = pen, Advance = tabulations[order[i]], Font = font, Size = GetSize(item.Style), Span = FindSpan(item.Start), IsInvisible = true, IsHanging = isHanging, IsRightToLeft = (levels[order[i]] & 1) != 0, Central = central });
                pen += tabulations[order[i]];
                hanging += isHanging ? tabulations[order[i]] : 0;
                continue;
            }

            if (item.Placeholder >= 0)
            {
                // The line is as tall as what stands on its baseline, and as deep as what goes below it
                var placeholder = _spans[item.Placeholder];
                var advance = (_hasCentralBaseline ? placeholder.PlaceholderHeight : placeholder.PlaceholderWidth) * _scale;
                if (_hasCentralBaseline)
                {
                    // Centered on the line, which is as wide as it needs
                    line.Ascent = Math.Max(line.Ascent, placeholder.PlaceholderWidth * _scale / 2);
                    line.Descent = Math.Max(line.Descent, placeholder.PlaceholderWidth * _scale / 2);
                }
                else
                {
                    line.Ascent = Math.Max(line.Ascent, placeholder.PlaceholderBaseline * _scale);
                    line.Descent = Math.Max(line.Descent, (placeholder.PlaceholderHeight - placeholder.PlaceholderBaseline) * _scale);
                }

                line.Glyphs.Add(new PlacedGlyph { Glyph = -1, Cluster = item.Start, PenX = pen, Advance = advance, Font = font, Size = GetSize(item.Style), Span = item.Placeholder, IsInvisible = true, IsPlaceholder = true, IsRightToLeft = (levels[order[i]] & 1) != 0 });
                pen += advance;
                continue;
            }

            foreach (ref readonly var glyph in glyphs.Slice(item.GlyphStart, item.GlyphCount))
            {
                var character = _text.Length == 0 ? '\0' : _text[Math.Min(glyph.Cluster, _text.Length - 1)];
                _hasMissingGlyphs |= glyph.Glyph == 0 && !glyph.IsInvisible;
                line.Glyphs.Add(new PlacedGlyph
                {
                    Glyph = glyph.Glyph,
                    Cluster = glyph.Cluster,
                    PenX = pen,
                    XOffset = glyph.XOffset,
                    YOffset = glyph.YOffset,
                    Advance = glyph.Advance,
                    Font = font,
                    Size = GetSize(item.Style),
                    Span = FindSpan(Math.Min(glyph.Cluster, Math.Max(_text.Length - 1, 0))),
                    IsInvisible = glyph.IsInvisible,
                    IsExpandable = !isHanging && !item.IsCombined && character is ' ' or '\u00A0',
                    IsHanging = isHanging,
                    IsRightToLeft = (levels[order[i]] & 1) != 0,
                    IsUpright = item.IsUpright,
                    BaselineShift = baselineShift,
                    Central = central,
                    WidthScale = glyph.WidthScale,
                });
                pen += glyph.Advance;
                hanging += isHanging ? glyph.Advance : 0;
            }
        }

        if (items.Count == 0)
        {
            // An empty line is as tall as the font of the place it is at.
            var style = _spans[FindSpan(Math.Min(from, Math.Max(_text.Length - 1, 0)))].Style;
            IncludeMetrics(line, style.Fonts.Primary, GetSize(style), UnicodeScript.Common);
        }

        // The hanging white space of a right-to-left paragraph is on the left of the line: the text starts after it.
        line.Width = pen - hanging;
        if (line.IsRightToLeft && hanging != 0)
        {
            foreach (ref var glyph in System.Runtime.InteropServices.CollectionsMarshal.AsSpan(line.Glyphs))
            {
                glyph.PenX -= hanging;
            }
        }

        var natural = line.Ascent + line.Descent;
        line.Height = _options.LineHeight * _scale ?? natural;
        line.Baseline = line.Ascent + ((line.Height - natural) / 2);
        _glyphCount += line.Glyphs.Count;
        return line;
    }

    /// <summary>
    /// Adds the font of an item to the metrics of a line: a line is as tall as the tallest of its fonts above the
    /// baseline plus the deepest below, each with half of its line gap.
    /// </summary>
    /// <remarks>
    /// A vertical line has a central baseline: it is as wide as the widest of its fonts is tall, half on each side
    /// of that baseline. Upright glyphs are centered on it by their vertical origins; the text that is on its side
    /// has the middle of its font on it (<see cref="GetCentral"/>), so that its own baseline is that far from the
    /// central baseline, toward the top of the line. A vertical line whose text is all on its side is a horizontal
    /// line turned: its baseline is the one of its text.
    /// </remarks>
    /// <returns>
    /// In a line with a central baseline, how far above its baseline the font has its middle: how far below the
    /// central baseline the text that is on its side has its own. Otherwise, 0.
    /// </returns>
    private double IncludeMetrics(Line line, Font font, double size, UnicodeScript script)
    {
        var metrics = font.GetMetrics(size);
        if (_hasCentralBaseline)
        {
            var half = (metrics.Ascent + metrics.Descent + metrics.LineGap) / 2;
            line.Ascent = Math.Max(line.Ascent, half);
            line.Descent = Math.Max(line.Descent, half);
            return GetCentral(font, size, script);
        }

        line.Ascent = Math.Max(line.Ascent, metrics.Ascent + (metrics.LineGap / 2));
        line.Descent = Math.Max(line.Descent, metrics.Descent + (metrics.LineGap / 2));
        return 0;
    }

    /// <summary>Cuts the item that contains a position in two, and shapes each part again.</summary>
    /// <param name="paragraph">The paragraph.</param>
    /// <param name="position">The position.</param>
    /// <param name="lineStart">
    /// The start of the line being built, when the position is not after its end: a part before the position that is
    /// in the line is shaped once the line has cut its items (<see cref="Item.NeedsShaping"/>), not each time it is cut.
    /// </param>
    private void SplitAt(Paragraph paragraph, int position, int lineStart)
    {
        for (var i = FindFirstItemEndingAfter(paragraph, position); i < paragraph.Items.Count && paragraph.Items[i].Start < position; i++)
        {
            var item = paragraph.Items[i];
            if (position <= item.Start || position >= item.End)
                continue;

            // The start of the second part is measured with the advances the item would have had if it had been
            // shaped when it was cut last.
            if (item.NeedsShaping && ReadsAdvancesWhenShapedFrom(paragraph, i, position))
            {
                Shape(paragraph, item);
            }

            var second = new Item { Start = position, End = item.End, Style = item.Style, Font = item.Font, Level = item.Level, Script = item.Script, IsTabulation = item.IsTabulation, Placeholder = item.Placeholder, IsUpright = item.IsUpright, IsCombined = item.IsCombined, IsCombinedRightToLeft = item.IsCombinedRightToLeft };
            item.End = position;
            paragraph.Items.Insert(i + 1, second);
            if (item.Start >= lineStart)
            {
                item.IsPending = false;
                item.NeedsShaping = true;
            }
            else
            {
                Shape(paragraph, item);
            }

            ShapeStart(paragraph, i + 1);
            return;
        }
    }

    /// <summary>
    /// Determines whether shaping the start of the part of an item after a position (<see cref="ShapeStart"/>) reads
    /// the advances of its characters: when the part, with the text left for later after it, is long enough to be
    /// cut (<see cref="FindShapingEnd"/>).
    /// </summary>
    private bool ReadsAdvancesWhenShapedFrom(Paragraph paragraph, int index, int position)
    {
        var item = paragraph.Items[index];
        if (!_wraps || item.IsTabulation)
            return false;

        var end = item.End;
        for (var i = index + 1; i < paragraph.Items.Count && paragraph.Items[i] is { IsPending: true } next && next.Start == end && IsSameRun(item, next); i++)
        {
            end = next.End;
        }

        return end - position >= 2 * MinShapingLength;
    }

    /// <summary>
    /// Shapes the start of an item whose text is the one of the lines to come: far enough for the next line to be
    /// found with the advances its characters have at the start of a line. The rest of the item keeps the advances
    /// of the last time it was shaped, and is shaped again when a line gets to it.
    /// </summary>
    /// <remarks>
    /// Shaping the whole rest of a paragraph after each of its lines would make the time of a layout grow with the
    /// square of the length of the paragraph.
    /// </remarks>
    private void ShapeStart(Paragraph paragraph, int index)
    {
        // The rest of the text that was left for later is part of the item again.
        var item = paragraph.Items[index];
        while (index + 1 < paragraph.Items.Count && paragraph.Items[index + 1] is { IsPending: true } next && next.Start == item.End && IsSameRun(item, next))
        {
            item.End = next.End;
            paragraph.Items.RemoveAt(index + 1);
        }

        var end = FindShapingEnd(paragraph, item);
        if (end < item.End)
        {
            paragraph.Items.Insert(index + 1, new Item { Start = end, End = item.End, Style = item.Style, Font = item.Font, Level = item.Level, Script = item.Script, IsPending = true, IsUpright = item.IsUpright, IsCombined = item.IsCombined, IsCombinedRightToLeft = item.IsCombinedRightToLeft });
            item.End = end;
        }

        Shape(paragraph, item);
    }

    private static bool IsSameRun(Item first, Item second)
    {
        return ReferenceEquals(first.Style, second.Style) && ReferenceEquals(first.Font, second.Font) && first.Level == second.Level && first.Script == second.Script
            && !first.IsTabulation && !second.IsTabulation && first.Placeholder < 0 && second.Placeholder < 0 && first.IsUpright == second.IsUpright
            && !first.IsCombined && !second.IsCombined;
    }

    /// <summary>
    /// Finds where the part of an item that is shaped for the next line ends: after a space, where a line can end,
    /// once the text is twice as wide as a line and at least <see cref="MinShapingLength"/> characters long.
    /// </summary>
    private int FindShapingEnd(Paragraph paragraph, Item item)
    {
        if (!_wraps || item.IsTabulation || item.IsCombined || item.End - item.Start < 2 * MinShapingLength)
            return item.End;

        // The advances are the ones of the last time these characters were shaped, in a longer text.
        var target = 2 * Math.Max(_maxWidth, 0);
        var width = 0.0;
        var position = item.Start;
        for (; position < item.End && (width < target || position - item.Start < MinShapingLength); position++)
        {
            width += paragraph.Advances[position - paragraph.Start];
        }

        // A place a line can end at, after a space: the text is not shaped differently around it.
        var breaks = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Breaks);
        var first = breaks.BinarySearch(position);
        for (var i = first < 0 ? ~first : first; i < breaks.Length && breaks[i] < item.End - MinShapingLength; i++)
        {
            if (_text[breaks[i] - 1] == ' ')
                return breaks[i];
        }

        return item.End;
    }

    private static Item? FindItem(Paragraph paragraph, int position)
    {
        for (var i = FindFirstItemEndingAfter(paragraph, position); i < paragraph.Items.Count && paragraph.Items[i].Start <= position; i++)
        {
            var item = paragraph.Items[i];
            if (position >= item.Start && position < item.End)
                return item;
        }

        return null;
    }

    // The items of a paragraph follow each other in the text: their starts and their ends never decrease.

    /// <summary>Finds the first item of a paragraph that ends after a position, or the number of items.</summary>
    private static int FindFirstItemEndingAfter(Paragraph paragraph, int position)
    {
        var items = paragraph.Items;
        var low = 0;
        var high = items.Count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (items[middle].End <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Finds the first item of a paragraph that starts at or after a position, or the number of items.</summary>
    private static int FindFirstItemFrom(Paragraph paragraph, int position)
    {
        var items = paragraph.Items;
        var low = 0;
        var high = items.Count;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (items[middle].Start < position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Finds the first place a line can end at that is after a position, or the number of places.</summary>
    private static int FindFirstBreakAfter(ReadOnlySpan<int> breaks, int position)
    {
        var low = 0;
        var high = breaks.Length;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (breaks[middle] <= position)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    /// <summary>Finds the span of a character.</summary>
    private int FindSpan(int position)
    {
        var low = 0;
        var high = _spans.Length - 1;
        while (low < high)
        {
            var middle = (low + high) >>> 1;
            if (position >= _spans[middle].Start + _spans[middle].Length)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        return low;
    }

    private int GetFontIndex(Font font)
    {
        var index = _fonts.IndexOf(font);
        if (index < 0)
        {
            index = _fonts.Count;
            _fonts.Add(font);
        }

        return index;
    }

    /// <summary>Determines whether a character of a cluster needs a glyph of the font: neither a control nor a default ignorable character.</summary>
    internal static bool NeedsGlyph(int codePoint) => !IsControl(codePoint) && !UnicodeProperties.IsDefaultIgnorable(codePoint);

    private static bool IsControl(int codePoint) => codePoint is < 0x20 or (>= 0x7F and <= 0x9F);

    /// <summary>Determines whether a character is white space a line can end with: every space but the ones that keep the words around them together.</summary>
    private static bool IsBreakingSpace(char character) => character is ' ' or '\t' or '\u1680' or (>= '\u2000' and <= '\u2006') or (>= '\u2008' and <= '\u200A') or '\u205F' or '\u3000';

    /// <summary>Stacks, aligns and justifies the lines, and creates the data of the layout.</summary>
    private TextLayoutData Finish()
    {
        var naturalWidth = 0.0;
        foreach (var line in _lines)
        {
            naturalWidth = Math.Max(naturalWidth, line.Width + line.Indent);
        }

        // Lines are aligned in the width they are wrapped at, or in the width of the widest one.
        var box = double.IsPositiveInfinity(_maxWidth) ? naturalWidth : _maxWidth;

        // Most lists have about one entry per glyph of the lines: they are given that capacity, so that filling them
        // does not grow them again and again (the limit on the glyphs is checked below).
        var glyphCapacity = (int)Math.Min(_glyphCount, _limits.MaxTextGlyphs + 1L);
        var glyphs = new List<int>(glyphCapacity);
        var positions = new List<double>(glyphCapacity * 2);
        var anchors = new List<double>(glyphCapacity);
        var runs = new List<GlyphRun>();
        var brushes = new List<Brush?>();
        var decorations = new List<DecorationRectangle>();
        var backgrounds = new List<DecorationRectangle>();
        var outlines = new List<Pen>();
        var placeholderCount = 0;
        foreach (var span in _spans)
        {
            placeholderCount += span.IsPlaceholder ? 1 : 0;
        }

        // The rectangle of each placeholder, in the order of the text; empty for one the layout does not hold
        var placeholders = placeholderCount == 0 ? [] : new RectangleD[placeholderCount];
        var hitBoxes = new List<TextHitBox>(glyphCapacity);
        var hitBoxStarts = new int[_lines.Count + 1];
        var boundaries = new List<int>(_text.Length + _lines.Count);
        var clusters = new List<int>();
        var textLines = new TextLine[_lines.Count];

        // A vertical layout is laid out here as a horizontal one, then turned line by line: what is at (u, v) of a
        // line, along it and across the lines, is drawn at (right - (v - top), u), where right is the right edge
        // the line has on the image. The first line is the rightmost one, or the leftmost one.
        var isLeftToRight = _options.WritingMode == TextWritingMode.VerticalLeftToRight;
        var blockSize = GetHeight();
        var lines = _isVertical ? new TextLine[_lines.Count] : textLines;
        var lineRights = new double[_isVertical ? _lines.Count : 0];
        var linePositions = new List<double>();
        var lineDecorations = new List<DecorationRectangle>();
        var lineBackgrounds = new List<DecorationRectangle>();
        for (var lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            var line = _lines[lineIndex];
            var placed = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(line.Glyphs);
            hitBoxStarts[lineIndex] = hitBoxes.Count;
            var lineRight = isLeftToRight ? line.Y + line.Height : blockSize - line.Y;
            var firstDecoration = decorations.Count;
            var firstBackground = backgrounds.Count;

            // Justification widens the spaces of a line so that it fills the width; not the last line of a paragraph,
            // which ends where its text ends.
            var free = box - line.Indent - line.Width;
            if (_options.Alignment == TextAlignment.Justify && !line.IsLastOfParagraph && free > 0)
            {
                var count = 0;
                foreach (ref readonly var glyph in placed)
                {
                    count += glyph.IsExpandable ? 1 : 0;
                }

                if (count > 0)
                {
                    var shift = 0.0;
                    var extra = free / count;
                    foreach (ref var glyph in placed)
                    {
                        glyph.PenX += shift;
                        if (glyph.IsExpandable)
                        {
                            glyph.Advance += extra;
                            shift += extra;
                        }
                    }

                    line.Width += free;
                    free = 0;
                }
            }

            // The side a line starts from is the left of a left-to-right paragraph and the right of a right-to-left one.
            var alignment = _options.Alignment == TextAlignment.Justify ? TextAlignment.Start : _options.Alignment;
            var x = alignment switch
            {
                TextAlignment.Center => line.IsRightToLeft ? free / 2 : line.Indent + (free / 2),
                TextAlignment.End => line.IsRightToLeft ? 0 : line.Indent + free,
                _ => line.IsRightToLeft ? free : line.Indent,
            };

            // Text hinted with the instructions of its fonts starts each line on a whole pixel.
            if (_hinter is not null && _options.Hinting == TextHinting.Full)
            {
                x = Math.Round(x, MidpointRounding.AwayFromZero);
            }

            var baseline = line.Y + line.Baseline;
            var fullEnd = line.IsLastOfParagraph ? line.Paragraph.FullEnd : line.To;
            lines[lineIndex] = new TextLine(line.From, fullEnd - line.From, x, line.Y, line.Width, line.Height, line.Baseline, line.IsRightToLeft);
            if (_isVertical)
            {
                // The line on the image: its box, and its baseline from its left
                lineRights[lineIndex] = lineRight;
                textLines[lineIndex] = new TextLine(line.From, fullEnd - line.From, lineRight - line.Height, x, line.Height, line.Width, line.Height - line.Baseline, line.IsRightToLeft);
            }

            // The glyphs, in display order, and the decorations of the spans they belong to. The glyphs of a cluster
            // follow each other: the middle of their advances is where the cluster is placed when a text follows a path.
            var decoration = default(DecorationSegment);
            var background = default(DecorationSegment);
            var clusterEnd = 0;
            var anchor = 0.0;
            for (var placedIndex = 0; placedIndex < placed.Length; placedIndex++)
            {
                ref readonly var glyph = ref placed[placedIndex];
                if (placedIndex == clusterEnd)
                {
                    clusterEnd = placedIndex + 1;
                    while (clusterEnd < placed.Length && placed[clusterEnd].Cluster == glyph.Cluster && placed[clusterEnd].Font == glyph.Font && placed[clusterEnd].Glyph >= 0 && glyph.Glyph >= 0)
                    {
                        clusterEnd++;
                    }

                    anchor = x + ((glyph.PenX + placed[clusterEnd - 1].PenX + placed[clusterEnd - 1].Advance) / 2);
                }

                var style = _spans[glyph.Span].Style;
                if (glyph.Glyph >= 0 && !glyph.IsInvisible)
                {
                    var brush = GetBrushIndex(brushes, style.Brush);
                    var outline = style.Outline is null ? -1 : GetOutlineIndex(outlines, style.Outline);
                    var isSideways = _isVertical && !glyph.IsUpright;
                    if (runs.Count == 0 || runs[^1].Font != glyph.Font || runs[^1].Size != glyph.Size || runs[^1].Brush != brush || runs[^1].Palette != style.ColorPaletteIndex || runs[^1].Synthesis != style.Synthesis || runs[^1].Outline != outline || runs[^1].IsSideways != isSideways || runs[^1].WidthScale != glyph.WidthScale)
                    {
                        runs.Add(new GlyphRun(glyph.Font, glyph.Size, brush, glyphs.Count, 0, style.ColorPaletteIndex, style.Synthesis, outline, isSideways, glyph.WidthScale));
                    }

                    runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
                    glyphs.Add(glyph.Glyph);

                    // A slanted glyph that is drawn above or below the baseline, such as a mark on its letter, moves
                    // with the slant of the line. The offsets of an upright glyph of a vertical line are not those
                    // of a baseline: they are kept as they are.
                    var along = x + glyph.PenX + glyph.XOffset + (glyph.IsUpright ? 0 : runs[^1].Shear * glyph.YOffset);
                    var across = baseline + glyph.BaselineShift - glyph.YOffset;
                    if (_isVertical)
                    {
                        linePositions.Add(along);
                        linePositions.Add(across);
                        positions.Add(lineRight - (across - line.Y));
                        positions.Add(along);
                    }
                    else
                    {
                        // A hinted glyph is drawn from a whole pixel: its baseline, and with the instructions of
                        // its font its origin.
                        if (_hinter is not null)
                        {
                            across = Math.Round(across, MidpointRounding.AwayFromZero);
                            if (_options.Hinting == TextHinting.Full)
                            {
                                along = Math.Round(along, MidpointRounding.AwayFromZero);
                            }

                            _hinter.Add(_fonts[glyph.Font], glyph.Size, glyph.Glyph);
                        }

                        positions.Add(along);
                        positions.Add(across);
                    }

                    anchors.Add(anchor);
                }

                var start = x + glyph.PenX;
                if (glyph.IsPlaceholder)
                {
                    // In a vertical line, the placeholder is centered on the line and takes its height of it. In one
                    // whose text is all on its side, it is turned with the line it is on.
                    var placeholder = _spans[glyph.Span];
                    var width = placeholder.PlaceholderWidth * _scale;
                    var height = placeholder.PlaceholderHeight * _scale;
                    var top = baseline - (placeholder.PlaceholderBaseline * _scale);
                    placeholders[GetPlaceholderIndex(glyph.Span)] = _hasCentralBaseline
                        ? new RectangleD(lineRight - (baseline - line.Y) - (width / 2), start, width, glyph.Advance)
                        : _isVertical
                            ? new RectangleD(lineRight - (top - line.Y) - height, start, height, glyph.Advance)
                            : new RectangleD(start, top, glyph.Advance, height);
                }

                // The background of a style is one rectangle as long as the style continues on the line, behind the
                // white space between its words but not behind the one that hangs
                var hasBackground = !glyph.IsHanging && style.BackgroundBrush is not null;
                if (!hasBackground || !ReferenceEquals(style, background.Style) || start != background.End)
                {
                    AddBackground(backgrounds, brushes, background, baseline);
                    background = new DecorationSegment { Style = hasBackground ? style : null, Start = start, End = start };
                }

                background.End = start + glyph.Advance;

                // A decoration is one rectangle as long as the same style continues on the line.
                var kind = glyph.IsHanging ? TextDecorations.None : style.Decorations;
                if (kind == TextDecorations.None || kind != decoration.Kind || !ReferenceEquals(style, decoration.Style) || glyph.Font != decoration.Font || glyph.IsUpright != decoration.IsUpright || glyph.Central != decoration.Central || start != decoration.End)
                {
                    AddDecorations(decorations, brushes, decoration, baseline);
                    decoration = new DecorationSegment { Kind = kind, Style = style, Font = glyph.Font, IsUpright = glyph.IsUpright, Central = glyph.Central, Start = start, End = start };
                }

                decoration.End = start + glyph.Advance;
            }

            AddDecorations(decorations, brushes, decoration, baseline);
            AddBackground(backgrounds, brushes, background, baseline);
            AddHitBoxes(line, placed, x, hitBoxes, boundaries, clusters);
            if (_isVertical)
            {
                Turn(decorations, firstDecoration, lineDecorations, lineRight, line.Y);
                Turn(backgrounds, firstBackground, lineBackgrounds, lineRight, line.Y);
            }
        }

        hitBoxStarts[^1] = hitBoxes.Count;
        if (glyphs.Count > _limits.MaxTextGlyphs)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TextGlyphs, _limits.MaxTextGlyphs, glyphs.Count);

        return new TextLayoutData
        {
            Fonts = [.. _fonts],
            HintedOutlines = _hinter?.Build(),
            Brushes = [.. brushes],
            Runs = [.. runs],
            Glyphs = [.. glyphs],
            Positions = [.. positions],
            Anchors = [.. anchors],
            Decorations = [.. decorations],
            Backgrounds = [.. backgrounds],
            Outlines = [.. outlines],
            Placeholders = placeholders,
            Scale = _scale,
            Lines = textLines,
            Width = _isVertical ? blockSize : naturalWidth,
            Height = _isVertical ? naturalWidth : blockSize,
            Vertical = !_isVertical ? null : new VerticalLayoutData
            {
                Lines = lines,
                LineRights = lineRights,
                Positions = [.. linePositions],
                Decorations = [.. lineDecorations],
                Backgrounds = [.. lineBackgrounds],
                IsLeftToRight = isLeftToRight,
                InlineSize = naturalWidth,
            },
            IsTruncated = _isTruncated,
            HasMissingGlyphs = _hasMissingGlyphs,
            TextLength = _text.Length,
            HitBoxes = [.. hitBoxes],
            HitBoxStarts = hitBoxStarts,
            CharacterBoundaries = [.. boundaries],
        };
    }

    /// <summary>
    /// Turns the rectangles of a vertical line that were added in the coordinates of the line: they are kept as they
    /// are for what follows the line, and replaced by what they are on the image.
    /// </summary>
    private static void Turn(List<DecorationRectangle> rectangles, int first, List<DecorationRectangle> lineRectangles, double lineRight, double lineTop)
    {
        for (var i = first; i < rectangles.Count; i++)
        {
            var rectangle = rectangles[i];
            lineRectangles.Add(rectangle);
            rectangles[i] = new DecorationRectangle(lineRight - (rectangle.Y - lineTop) - rectangle.Height, rectangle.X, rectangle.Height, rectangle.Width, rectangle.Brush);
        }
    }

    /// <summary>
    /// Adds the clusters of a line, from its left: the characters of each one and the space its glyphs take, from where
    /// the pen is before the first one to where it is after the last one.
    /// </summary>
    /// <remarks>
    /// A cluster has the characters from its first one to the first one of the next cluster of the line, in the order
    /// of the text, or to the end of the line. What a line adds to its text (a hyphen, an ellipsis) has no character,
    /// and is not a place of the text.
    /// </remarks>
    private void AddHitBoxes(Line line, ReadOnlySpan<PlacedGlyph> placed, double x, List<TextHitBox> hitBoxes, List<int> boundaries, List<int> clusters)
    {
        // The first characters of the clusters, in the order of the text
        clusters.Clear();
        foreach (ref readonly var glyph in placed)
        {
            clusters.Add(glyph.Cluster);
        }

        clusters.Sort();
        for (var first = 0; first < placed.Length;)
        {
            ref readonly var glyph = ref placed[first];
            var left = glyph.PenX;
            var right = glyph.PenX + glyph.Advance;
            var last = first + 1;
            for (; last < placed.Length && placed[last].Cluster == glyph.Cluster; last++)
            {
                left = Math.Min(left, placed[last].PenX);
                right = Math.Max(right, placed[last].PenX + placed[last].Advance);
            }

            first = last;

            // The first cluster that starts after this one, or the end of the line
            var index = clusters.BinarySearch(glyph.Cluster + 1);
            index = index < 0 ? ~index : index;
            while (index > 0 && index < clusters.Count && clusters[index - 1] > glyph.Cluster)
            {
                index--;
            }

            var end = Math.Min(index < clusters.Count ? clusters[index] : line.To, line.To);
            if (end <= glyph.Cluster)
                continue;

            // The characters of the cluster: the width of a ligature is shared between them
            var text = _text.AsSpan(glyph.Cluster, end - glyph.Cluster);
            var count = 0;
            var firstBoundary = boundaries.Count;
            for (var position = 0; position < text.Length;)
            {
                position += Math.Max(Unicode.GetGraphemeClusterLength(text[position..]), 1);
                boundaries.Add(position);
                count++;
            }

            if (count == 1)
            {
                boundaries.RemoveAt(boundaries.Count - 1);
                firstBoundary = -1;
            }

            hitBoxes.Add(new TextHitBox(glyph.Cluster, text.Length, x + left, x + right, glyph.IsRightToLeft, count, firstBoundary));
        }
    }

    /// <summary>Adds the rectangles of the decorations of a part of a line.</summary>
    private void AddDecorations(List<DecorationRectangle> decorations, List<Brush?> brushes, in DecorationSegment segment, double baseline)
    {
        if (segment.Kind == TextDecorations.None || segment.Style is null || !(segment.End > segment.Start))
            return;

        // The font gives the top of each line from the baseline and its thickness; the line over the text is at the ascent.
        var metrics = _fonts[segment.Font].GetMetrics(GetSize(segment.Style));
        var brush = GetBrushIndex(brushes, segment.Style.DecorationBrush ?? segment.Style.Brush);
        var width = segment.End - segment.Start;
        if (_hasCentralBaseline)
        {
            // The baseline of a vertical line is its central one: the baseline the font has when it is on its side
            // is as far from it as the middle of the font is above that baseline, toward the top of the line. The
            // lines of upright text are at the same places, beside its glyphs, but the line through them, which is
            // in the middle of the line.
            var central = baseline;
            baseline += segment.Central;
            if (segment.IsUpright && (segment.Kind & TextDecorations.Strikethrough) != TextDecorations.None && metrics.StrikethroughThickness > 0)
            {
                decorations.Add(new DecorationRectangle(segment.Start, central - (metrics.StrikethroughThickness / 2), width, metrics.StrikethroughThickness, brush));
            }
        }

        if ((segment.Kind & TextDecorations.Underline) != TextDecorations.None && metrics.UnderlineThickness > 0)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline + metrics.UnderlinePosition, width, metrics.UnderlineThickness, brush));
        }

        if ((segment.Kind & TextDecorations.Strikethrough) != TextDecorations.None && metrics.StrikethroughThickness > 0 && !segment.IsUpright)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline - metrics.StrikethroughPosition, width, metrics.StrikethroughThickness, brush));
        }

        if ((segment.Kind & TextDecorations.Overline) != TextDecorations.None && metrics.UnderlineThickness > 0)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline - metrics.Ascent, width, metrics.UnderlineThickness, brush));
        }
    }

    /// <summary>Adds the rectangle of the background of a part of a line: as tall as the primary font of its style, from its ascent to its descent.</summary>
    private void AddBackground(List<DecorationRectangle> backgrounds, List<Brush?> brushes, in DecorationSegment segment, double baseline)
    {
        if (segment.Style?.BackgroundBrush is not { } paint || !(segment.End > segment.Start))
            return;

        // Around the central baseline of a vertical line, which the middle of the font is on
        var metrics = segment.Style.Fonts.Primary.GetMetrics(GetSize(segment.Style));
        var top = _hasCentralBaseline ? baseline - ((metrics.Ascent + metrics.Descent) / 2) : baseline - metrics.Ascent;
        backgrounds.Add(new DecorationRectangle(segment.Start, top, segment.End - segment.Start, metrics.Ascent + metrics.Descent, GetBrushIndex(brushes, paint)));
    }

    /// <summary>Gets the rank of a placeholder among the placeholders of the text, from the index of its span.</summary>
    private int GetPlaceholderIndex(int span)
    {
        var index = 0;
        for (var i = 0; i < span; i++)
        {
            index += _spans[i].IsPlaceholder ? 1 : 0;
        }

        return index;
    }

    private static int GetOutlineIndex(List<Pen> outlines, Pen outline)
    {
        for (var i = 0; i < outlines.Count; i++)
        {
            if (ReferenceEquals(outlines[i], outline))
                return i;
        }

        outlines.Add(outline);
        return outlines.Count - 1;
    }

    private static int GetBrushIndex(List<Brush?> brushes, Brush? brush)
    {
        for (var i = 0; i < brushes.Count; i++)
        {
            if (ReferenceEquals(brushes[i], brush))
                return i;
        }

        brushes.Add(brush);
        return brushes.Count - 1;
    }

    private enum EmojiPresentation
    {
        /// <summary>The cluster is no emoji, or an emoji character that is text by default and that nothing makes an emoji.</summary>
        None,

        /// <summary>The emoji is drawn as text: a text presentation selector follows it.</summary>
        Text,

        /// <summary>The emoji is drawn as an emoji: by default, by its selector, or because it is an emoji sequence.</summary>
        Emoji,
    }

    /// <summary>How the glyphs of a run go.</summary>
    private enum ShapingOrientation
    {
        /// <summary>A run of a horizontal line.</summary>
        Horizontal,

        /// <summary>Upright glyphs of a vertical line, one under the other.</summary>
        Upright,

        /// <summary>A horizontal run turned on its side in a vertical line.</summary>
        Sideways,

        /// <summary>A horizontal run set upright in the room of one character of a vertical line.</summary>
        Combined,
    }

    private enum LineEnd
    {
        None,
        Hyphen,
        Ellipsis,
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private struct ShapedGlyph
    {
        public int Glyph;
        public int Cluster;
        public double Advance;
        public double XOffset;
        public double YOffset;
        public bool IsInvisible;
        public double WidthScale;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private struct DecorationSegment
    {
        public TextDecorations Kind;
        public TextStyle? Style;
        public int Font;
        public bool IsUpright;
        public double Central;
        public double Start;
        public double End;
    }

    private sealed class Item
    {
        public int Start;
        public int End;
        public required TextStyle Style;
        public required Font Font;
        public byte Level;
        public UnicodeScript Script;
        public bool IsTabulation;

        /// <summary>The span of the placeholder the item is, or -1: one character that takes the room of what the caller draws there.</summary>
        public int Placeholder = -1;

        /// <summary>The glyphs of the item are upright in a vertical line, and shaped for it; the others are on their side, shaped as in a horizontal line.</summary>
        public bool IsUpright;

        /// <summary>
        /// The item is a text set horizontally in the room of one upright character of a vertical line
        /// (tate-chu-yoko): it is upright, and shaped as a horizontal line.
        /// </summary>
        public bool IsCombined;

        /// <summary>The combined text is shaped from right to left: its first character is right to left.</summary>
        public bool IsCombinedRightToLeft;

        /// <summary>
        /// The item is text of lines to come that was not shaped again when the line before it was made: it has no
        /// glyph, and its characters have the advances of the last time they were shaped.
        /// </summary>
        public bool IsPending;

        /// <summary>
        /// The item was cut since it was last shaped, and is in the line being built: its glyphs and the advances of
        /// its characters are those of its extent before the cut until the line shapes it.
        /// </summary>
        public bool NeedsShaping;
        public int GlyphStart;
        public int GlyphCount;
    }

    private sealed class Paragraph(int start, int end, int fullEnd, List<int> breaks)
    {
        public int Start { get; } = start;

        /// <summary>Gets the end of the text of the paragraph, before the characters of its line break.</summary>
        public int End { get; } = end;

        public int FullEnd { get; } = fullEnd;

        /// <summary>Gets the places a line can end at, in increasing order; the last one is <see cref="End"/>.</summary>
        public List<int> Breaks { get; } = breaks;

        public byte Level { get; set; }

        public byte[] Levels { get; set; } = [];

        /// <summary>Gets the advance of each character: the sum of the advances of the glyphs of the cluster that starts at it.</summary>
        public double[] Advances { get; set; } = [];

        public List<Item> Items { get; } = [];

        /// <summary>
        /// Gets the glyphs of the items, in the order they were shaped. The list starts with room for about one glyph
        /// per character, up to the text a few lines need: only the start of the text after a line is shaped.
        /// </summary>
        public List<ShapedGlyph> Glyphs { get; } = new(Math.Min(end - start, 4096) + 16);

        /// <summary>
        /// Gets or sets, for each character, the end of the text set in the room of one upright character it is in,
        /// or 0; empty when the paragraph has no such text.
        /// </summary>
        public int[] CombinedEnds { get; set; } = [];
    }

    private sealed class Line
    {
        public required Paragraph Paragraph;
        public int From;
        public int To;
        public double Indent;
        public double Y;
        public double Width;
        public double Height;
        public double Baseline;
        public double Ascent;
        public double Descent;
        public bool IsRightToLeft;
        public bool IsLastOfParagraph;
        public List<PlacedGlyph> Glyphs { get; } = [];
    }
}
