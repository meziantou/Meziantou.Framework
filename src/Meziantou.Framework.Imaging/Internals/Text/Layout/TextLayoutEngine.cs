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
/// <b>Direction.</b> The Unicode Bidirectional Algorithm (UAX #9) gives each character an embedding level.
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
internal sealed class TextLayoutEngine
{
    private const int SoftHyphen = 0x00AD;

    /// <summary>The fewest characters of the text after a line that are shaped again for the next line.</summary>
    private const int MinShapingLength = 64;

    private readonly string _text;
    private readonly TextSpan[] _spans;
    private readonly TextLayoutOptions _options;
    private readonly ImageResourceLimits _limits;
    private readonly GlyphBuffer _buffer;
    private readonly bool _wraps;
    private readonly List<Font> _fonts = [];
    private readonly List<Line> _lines = [];
    private long _glyphCount;
    private bool _isTruncated;
    private bool _hasMissingGlyphs;

    private TextLayoutEngine(string text, TextSpan[] spans, TextLayoutOptions options)
    {
        _text = text;
        _spans = spans;
        _options = options;
        _limits = spans[0].Style.Fonts.Primary.Face.Data.Limits;
        _buffer = new GlyphBuffer(_limits);
        _wraps = options.Wrapping == TextWrapping.Wrap && !double.IsPositiveInfinity(options.MaxWidth);
    }

    /// <summary>Lays out a text whose spans follow each other and cover it; an empty text has one span of length 0.</summary>
    /// <exception cref="ImageResourceLimitException">The text, its glyphs or its lines exceed a limit of the configuration of the first font.</exception>
    public static TextLayoutData Create(string text, TextSpan[] spans, TextLayoutOptions options)
    {
        var engine = new TextLayoutEngine(text, spans, options);
        if (text.Length > engine._limits.MaxTextLength)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TextLength, engine._limits.MaxTextLength, text.Length);

        engine.Run();
        return engine.Finish();
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
        paragraph.Levels = new byte[content.Length];
        paragraph.Level = (byte)UnicodeBidi.ResolveLevels(content, direction, paragraph.Levels);
        paragraph.Advances = new double[content.Length];
        Itemize(paragraph);
        foreach (var item in paragraph.Items)
        {
            Shape(paragraph, item);
        }

        var from = paragraph.Start;
        var isFirst = true;
        do
        {
            var indent = isFirst ? _options.FirstLineIndent : 0;
            var to = paragraph.End;
            var isHyphenated = false;
            if (_wraps && from < paragraph.End)
            {
                to = FindBreak(paragraph, from, _options.MaxWidth - indent, out isHyphenated);
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

        // The font of each character: the one chosen for its grapheme cluster.
        var fonts = new int[content.Length];
        for (var i = 0; i < content.Length;)
        {
            // A cluster that two spans share is cut where the span changes: each span has its own fonts.
            var length = Math.Max(Unicode.GetGraphemeClusterLength(content[i..]), 1);
            var span = _spans[FindSpan(paragraph.Start + i)];
            length = Math.Min(length, span.Start + span.Length - (paragraph.Start + i));
            fonts.AsSpan(i, length).Fill(SelectFont(span.Style.Fonts, content.Slice(i, length)));
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

            if (isEnd
                || content[i] == '\t'
                || content[i - 1] == '\t'
                || paragraph.Levels[i] != paragraph.Levels[start]
                || scripts[i] != scripts[start]
                || fonts[i] != fonts[start]
                || (nextSpan != span && !AreShapedTogether(_spans[span].Style, _spans[nextSpan].Style)))
            {
                var style = _spans[span].Style;
                paragraph.Items.Add(new Item
                {
                    Start = paragraph.Start + start,
                    End = paragraph.Start + i,
                    Style = style,
                    Font = style.Fonts.FontSpan[fonts[start]],
                    Level = paragraph.Levels[start],
                    Script = scripts[start],
                    IsTabulation = content[start] == '\t',
                });
                start = i;
            }

            current = nextSpan;
        }
    }

    /// <summary>Determines whether the texts of two styles can be shaped as one: they only differ by what is painted.</summary>
    private static bool AreShapedTogether(TextStyle first, TextStyle second)
    {
        return ReferenceEquals(first, second)
            || (first.Fonts.FontSpan.SequenceEqual(second.Fonts.FontSpan)
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
    /// An emoji is first looked for in the fonts of the kind it is presented with (<see cref="GetPresentation"/>): the
    /// fonts that have color glyphs for the emoji presentation, the other ones for the text presentation. The stack is
    /// then searched in its order, as for any character.
    /// </remarks>
    private static int SelectFont(FontStack stack, ReadOnlySpan<char> cluster)
    {
        var fonts = stack.FontSpan;
        if (fonts.Length == 1)
            return 0;

        var presentation = GetPresentation(cluster);
        if (presentation != EmojiPresentation.None)
        {
            for (var i = 0; i < fonts.Length; i++)
            {
                if (fonts[i].HasColorGlyphs == (presentation == EmojiPresentation.Emoji) && HasCluster(fonts[i].Face, cluster, out _))
                    return i;
            }
        }

        var withFirst = -1;
        for (var i = 0; i < fonts.Length; i++)
        {
            if (HasCluster(fonts[i].Face, cluster, out var hasFirst))
                return i;

            if (hasFirst && withFirst < 0)
            {
                withFirst = i;
            }
        }

        return Math.Max(withFirst, 0);
    }

    /// <summary>Determines whether a font has a glyph for each character of a cluster, alone or composed with the one before.</summary>
    /// <param name="face">The font.</param>
    /// <param name="cluster">The cluster.</param>
    /// <param name="hasFirst">Whether the font has the first character of the cluster that needs a glyph.</param>
    private static bool HasCluster(FontFace face, ReadOnlySpan<char> cluster, out bool hasFirst)
    {
        hasFirst = false;
        var hasAll = true;
        var previous = -1;
        var isFirst = true;
        foreach (var rune in cluster.EnumerateRunes())
        {
            var codePoint = rune.Value;
            if (IsControl(codePoint) || UnicodeProperties.IsDefaultIgnorable(codePoint))
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
        item.GlyphStart = paragraph.Glyphs.Count;
        item.GlyphCount = 0;
        paragraph.Advances.AsSpan(item.Start - paragraph.Start, item.End - item.Start).Clear();
        if (item.IsTabulation)
            return;

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

        if (_buffer.Length == 0)
            return;

        var style = item.Style;
        var isRightToLeft = (item.Level & 1) != 0;
        Span<ShapingFeature> features = style.Features.Count <= 16 ? stackalloc ShapingFeature[style.Features.Count] : new ShapingFeature[style.Features.Count];
        for (var i = 0; i < features.Length; i++)
        {
            features[i] = new ShapingFeature(style.Features[i].TagValue, style.Features[i].Value);
        }

        TextShaper.Shape(_buffer, item.Font.Face, item.Script, style.LanguageTags, isRightToLeft, features);
        var scale = style.Size / item.Font.UnitsPerEm;
        var info = _buffer.Info;
        var positions = _buffer.Positions;
        for (var i = 0; i < info.Length; i++)
        {
            paragraph.Glyphs.Add(new ShapedGlyph
            {
                Glyph = info[i].Glyph,
                Cluster = info[i].Cluster,
                Advance = positions[i].XAdvance * scale,
                XOffset = positions[i].XOffset * scale,
                YOffset = positions[i].YOffset * scale,
                IsInvisible = info[i].IsIgnorable,
            });
        }

        item.GlyphCount = info.Length;
        var glyphs = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(paragraph.Glyphs).Slice(item.GlyphStart, item.GlyphCount);
        if ((style.Synthesis & FontSynthesis.Bold) != FontSynthesis.None)
        {
            Embolden(glyphs, style.Size * GlyphRun.BoldWidth, isRightToLeft);
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
            var space = style.LetterSpacing + (character is ' ' or '\u00A0' ? style.WordSpacing : 0);
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
        var previous = from;
        var lastFit = -1;
        var lastFitIsHyphenated = false;
        foreach (var candidate in paragraph.Breaks)
        {
            if (candidate <= from)
                continue;

            // A soft hyphen is a place to break only when words are hyphenated, and the line then ends with a hyphen.
            var isSoftHyphen = candidate < paragraph.End && _text[candidate - 1] == SoftHyphen;
            if (isSoftHyphen && !withHyphens)
                continue;

            width = Measure(paragraph, previous, candidate, width);
            previous = candidate;
            var lineWidth = width - MeasureTrailingSpaces(paragraph, from, candidate) + (isSoftHyphen ? MeasureHyphen(paragraph, candidate) : 0);
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

    /// <summary>Gets the advance of the white space that ends a range: at the end of a wrapped line, it hangs.</summary>
    private double MeasureTrailingSpaces(Paragraph paragraph, int from, int to)
    {
        var width = 0.0;
        for (var i = to - 1; i >= from && IsBreakingSpace(_text[i]); i--)
        {
            width += paragraph.Advances[i - paragraph.Start];
        }

        return width;
    }

    private double GetTabulationAdvance(TextStyle style, double position)
    {
        var font = style.Fonts.Primary;
        var space = font.Face.MapCodePoint(' ');
        var tabulation = _options.TabSize * (space == 0 ? font.UnitsPerEm / 4.0 : font.Face.GetAdvanceWidth(space)) * (style.Size / font.UnitsPerEm);
        return tabulation > 0 ? ((Math.Floor(position / tabulation) + 1) * tabulation) - position : 0;
    }

    /// <summary>Gets the width of the hyphen a line would end with after a character.</summary>
    private double MeasureHyphen(Paragraph paragraph, int position)
    {
        var item = FindItem(paragraph, position - 1);
        if (item is null)
            return 0;

        var face = item.Font.Face;
        var scale = item.Style.Size / item.Font.UnitsPerEm;
        var width = 0.0;
        foreach (var rune in GetHyphen(item).EnumerateRunes())
        {
            width += (face.GetAdvanceWidth(face.MapCodePoint(rune.Value)) * scale) + GetEmboldenWidth(item.Style);
        }

        return width;
    }

    /// <summary>Gets what a synthetic bold adds to the advance of a glyph of a style.</summary>
    private static double GetEmboldenWidth(TextStyle style) => (style.Synthesis & FontSynthesis.Bold) != FontSynthesis.None ? style.Size * GlyphRun.BoldWidth : 0;

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
                if (position > best && Measure(paragraph, from, position, 0) + MeasureHyphen(paragraph, position) <= available)
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
            var next = position + Math.Max(Unicode.GetGraphemeClusterLength(text[position..]), 1);
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
        var isOverflowing = _lines.Count > 0 && GetHeight() + line.Height > _options.MaxHeight;
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
        if (_options.Trimming != TextTrimming.None && line.Width + indent > _options.MaxWidth)
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
        var available = _options.MaxWidth - indent;
        var best = from;
        if (double.IsPositiveInfinity(available))
        {
            best = to;
        }
        else
        {
            var position = from;
            var width = 0.0;
            var text = _text.AsSpan(0, to);
            while (position < to)
            {
                var next = _options.Trimming == TextTrimming.Word
                    ? NextBreak(paragraph, position, to)
                    : position + Math.Max(Unicode.GetGraphemeClusterLength(text[position..]), 1);
                width = Measure(paragraph, position, next, width);
                var item = FindItem(paragraph, next - 1) ?? FindItem(paragraph, from);
                var ellipsis = item is null ? 0 : MeasureText(item, GetEllipsis(item));
                if (width - MeasureTrailingSpaces(paragraph, from, next) + ellipsis > available)
                    break;

                best = next;
                position = next;
            }
        }

        // The spaces before the ellipsis are dropped: it follows the last word.
        while (best > from && IsBreakingSpace(_text[best - 1]))
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
            foreach (var candidate in paragraph.Breaks)
            {
                if (candidate > position)
                    return Math.Min(candidate, to);
            }

            return to;
        }
    }

    private static double MeasureText(Item item, string text)
    {
        var face = item.Font.Face;
        var scale = item.Style.Size / item.Font.UnitsPerEm;
        var width = 0.0;
        foreach (var rune in text.EnumerateRunes())
        {
            width += (face.GetAdvanceWidth(face.MapCodePoint(rune.Value)) * scale) + GetEmboldenWidth(item.Style);
        }

        return width;
    }

    /// <summary>Builds the line of a range of a paragraph: its glyphs in display order, from the start of its text.</summary>
    private Line BuildLine(Paragraph paragraph, int from, int to, double indent, LineEnd end, bool isWrapped)
    {
        // An item the line ends inside is cut in two, and each part is shaped again.
        SplitAt(paragraph, to);
        SplitAt(paragraph, from);

        // The white space that ends a wrapped line hangs: it is not part of the width, and it follows the direction of
        // the paragraph whatever the direction of the text before it (rule L1).
        var contentEnd = to;
        if (isWrapped && end == LineEnd.None)
        {
            while (contentEnd > from && IsBreakingSpace(_text[contentEnd - 1]))
            {
                contentEnd--;
            }

            SplitAt(paragraph, contentEnd);
        }

        // The text after the line is shaped from its start, for the next line to be found; a part of the line that
        // was shaped for an earlier line, or not yet, is shaped with the rest of its run: a line is shaped as a whole.
        for (var i = 0; i < paragraph.Items.Count; i++)
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

            if (isMerged || item.IsPending)
            {
                Shape(paragraph, item);
            }
        }

        var items = new List<Item>();
        foreach (var item in paragraph.Items)
        {
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
            Shape(paragraph, last, end == LineEnd.Hyphen ? GetHyphen(last) : GetEllipsis(last));
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
        for (var i = 0; i < items.Count; i++)
        {
            var item = items[order[i]];
            var font = GetFontIndex(item.Font);
            var isHanging = item.Start >= contentEnd && item.Start < item.End;
            IncludeMetrics(line, item.Font, item.Style.Size);
            if (item.IsTabulation)
            {
                line.Glyphs.Add(new PlacedGlyph { Glyph = -1, Cluster = item.Start, PenX = pen, Advance = tabulations[order[i]], Font = font, Size = item.Style.Size, Span = FindSpan(item.Start), IsInvisible = true, IsHanging = isHanging });
                pen += tabulations[order[i]];
                hanging += isHanging ? tabulations[order[i]] : 0;
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
                    Size = item.Style.Size,
                    Span = FindSpan(Math.Min(glyph.Cluster, Math.Max(_text.Length - 1, 0))),
                    IsInvisible = glyph.IsInvisible,
                    IsExpandable = !isHanging && character is ' ' or '\u00A0',
                    IsHanging = isHanging,
                });
                pen += glyph.Advance;
                hanging += isHanging ? glyph.Advance : 0;
            }
        }

        if (items.Count == 0)
        {
            // An empty line is as tall as the font of the place it is at.
            var style = _spans[FindSpan(Math.Min(from, Math.Max(_text.Length - 1, 0)))].Style;
            IncludeMetrics(line, style.Fonts.Primary, style.Size);
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
        line.Height = _options.LineHeight ?? natural;
        line.Baseline = line.Ascent + ((line.Height - natural) / 2);
        _glyphCount += line.Glyphs.Count;
        return line;
    }

    /// <summary>
    /// Adds the font of an item to the metrics of a line: a line is as tall as the tallest of its fonts above the
    /// baseline plus the deepest below, each with half of its line gap.
    /// </summary>
    private static void IncludeMetrics(Line line, Font font, double size)
    {
        var metrics = font.GetMetrics(size);
        line.Ascent = Math.Max(line.Ascent, metrics.Ascent + (metrics.LineGap / 2));
        line.Descent = Math.Max(line.Descent, metrics.Descent + (metrics.LineGap / 2));
    }

    /// <summary>Cuts the item that contains a position in two, and shapes each part again.</summary>
    private void SplitAt(Paragraph paragraph, int position)
    {
        for (var i = 0; i < paragraph.Items.Count; i++)
        {
            var item = paragraph.Items[i];
            if (position <= item.Start || position >= item.End)
                continue;

            var second = new Item { Start = position, End = item.End, Style = item.Style, Font = item.Font, Level = item.Level, Script = item.Script, IsTabulation = item.IsTabulation };
            item.End = position;
            paragraph.Items.Insert(i + 1, second);
            Shape(paragraph, item);
            ShapeStart(paragraph, i + 1);
            return;
        }
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
            paragraph.Items.Insert(index + 1, new Item { Start = end, End = item.End, Style = item.Style, Font = item.Font, Level = item.Level, Script = item.Script, IsPending = true });
            item.End = end;
        }

        Shape(paragraph, item);
    }

    private static bool IsSameRun(Item first, Item second)
    {
        return ReferenceEquals(first.Style, second.Style) && ReferenceEquals(first.Font, second.Font) && first.Level == second.Level && first.Script == second.Script
            && !first.IsTabulation && !second.IsTabulation;
    }

    /// <summary>
    /// Finds where the part of an item that is shaped for the next line ends: after a space, where a line can end,
    /// once the text is twice as wide as a line and at least <see cref="MinShapingLength"/> characters long.
    /// </summary>
    private int FindShapingEnd(Paragraph paragraph, Item item)
    {
        if (!_wraps || item.IsTabulation || item.End - item.Start < 2 * MinShapingLength)
            return item.End;

        // The advances are the ones of the last time these characters were shaped, in a longer text.
        var target = 2 * Math.Max(_options.MaxWidth, 0);
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
        foreach (var item in paragraph.Items)
        {
            if (position >= item.Start && position < item.End)
                return item;
        }

        return null;
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

    /// <summary>Determines whether a character is a control character, which has no glyph and takes no space.</summary>
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
        var box = double.IsPositiveInfinity(_options.MaxWidth) ? naturalWidth : _options.MaxWidth;
        var glyphs = new List<int>();
        var positions = new List<double>();
        var anchors = new List<double>();
        var runs = new List<GlyphRun>();
        var brushes = new List<Brush?>();
        var decorations = new List<DecorationRectangle>();
        var textLines = new TextLine[_lines.Count];
        for (var lineIndex = 0; lineIndex < _lines.Count; lineIndex++)
        {
            var line = _lines[lineIndex];
            var placed = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(line.Glyphs);

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

            var baseline = line.Y + line.Baseline;
            var fullEnd = line.IsLastOfParagraph ? line.Paragraph.FullEnd : line.To;
            textLines[lineIndex] = new TextLine(line.From, fullEnd - line.From, x, line.Y, line.Width, line.Height, line.Baseline, line.IsRightToLeft);

            // The glyphs, in display order, and the decorations of the spans they belong to. The glyphs of a cluster
            // follow each other: the middle of their advances is where the cluster is placed when a text follows a path.
            var decoration = default(DecorationSegment);
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
                    if (runs.Count == 0 || runs[^1].Font != glyph.Font || runs[^1].Size != glyph.Size || runs[^1].Brush != brush || runs[^1].Palette != style.ColorPaletteIndex || runs[^1].Synthesis != style.Synthesis)
                    {
                        runs.Add(new GlyphRun(glyph.Font, glyph.Size, brush, glyphs.Count, 0, style.ColorPaletteIndex, style.Synthesis));
                    }

                    runs[^1] = runs[^1] with { Count = runs[^1].Count + 1 };
                    glyphs.Add(glyph.Glyph);

                    // A slanted glyph that is drawn above or below the baseline, such as a mark on its letter, moves
                    // with the slant of the line.
                    positions.Add(x + glyph.PenX + glyph.XOffset + (runs[^1].Shear * glyph.YOffset));
                    positions.Add(baseline - glyph.YOffset);
                    anchors.Add(anchor);
                }

                // A decoration is one rectangle as long as the same style continues on the line.
                var kind = glyph.IsHanging ? TextDecorations.None : style.Decorations;
                var start = x + glyph.PenX;
                if (kind == TextDecorations.None || kind != decoration.Kind || !ReferenceEquals(style, decoration.Style) || glyph.Font != decoration.Font || start != decoration.End)
                {
                    AddDecorations(decorations, brushes, decoration, baseline);
                    decoration = new DecorationSegment { Kind = kind, Style = style, Font = glyph.Font, Start = start, End = start };
                }

                decoration.End = start + glyph.Advance;
            }

            AddDecorations(decorations, brushes, decoration, baseline);
        }

        if (glyphs.Count > _limits.MaxTextGlyphs)
            throw new ImageResourceLimitException(ImageResourceLimitKind.TextGlyphs, _limits.MaxTextGlyphs, glyphs.Count);

        return new TextLayoutData
        {
            Fonts = [.. _fonts],
            Brushes = [.. brushes],
            Runs = [.. runs],
            Glyphs = [.. glyphs],
            Positions = [.. positions],
            Anchors = [.. anchors],
            Decorations = [.. decorations],
            Lines = textLines,
            Width = naturalWidth,
            Height = GetHeight(),
            IsTruncated = _isTruncated,
            HasMissingGlyphs = _hasMissingGlyphs,
        };
    }

    /// <summary>Adds the rectangles of the decorations of a part of a line.</summary>
    private void AddDecorations(List<DecorationRectangle> decorations, List<Brush?> brushes, in DecorationSegment segment, double baseline)
    {
        if (segment.Kind == TextDecorations.None || segment.Style is null || !(segment.End > segment.Start))
            return;

        // The font gives the top of each line from the baseline and its thickness; the line over the text is at the ascent.
        var metrics = _fonts[segment.Font].GetMetrics(segment.Style.Size);
        var brush = GetBrushIndex(brushes, segment.Style.DecorationBrush ?? segment.Style.Brush);
        var width = segment.End - segment.Start;
        if ((segment.Kind & TextDecorations.Underline) != TextDecorations.None && metrics.UnderlineThickness > 0)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline + metrics.UnderlinePosition, width, metrics.UnderlineThickness, brush));
        }

        if ((segment.Kind & TextDecorations.Strikethrough) != TextDecorations.None && metrics.StrikethroughThickness > 0)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline - metrics.StrikethroughPosition, width, metrics.StrikethroughThickness, brush));
        }

        if ((segment.Kind & TextDecorations.Overline) != TextDecorations.None && metrics.UnderlineThickness > 0)
        {
            decorations.Add(new DecorationRectangle(segment.Start, baseline - metrics.Ascent, width, metrics.UnderlineThickness, brush));
        }
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
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Auto)]
    private struct DecorationSegment
    {
        public TextDecorations Kind;
        public TextStyle? Style;
        public int Font;
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

        /// <summary>
        /// The item is text of lines to come that was not shaped again when the line before it was made: it has no
        /// glyph, and its characters have the advances of the last time they were shaped.
        /// </summary>
        public bool IsPending;
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

        public List<ShapedGlyph> Glyphs { get; } = [];
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
