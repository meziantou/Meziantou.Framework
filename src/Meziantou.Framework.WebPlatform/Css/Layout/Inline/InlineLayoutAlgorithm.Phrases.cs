using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The virtual expandable separators of word-space-transform: auto-phrase (https://drafts.csswg.org/css-text-4/#valdef-word-space-transform-auto-phrase):
// "If the content language is known and the user agent supports linguistic analysis for this language, the user agent must
// detect phrase boundaries. If a word-separator character, other space separator, or U+200B ZERO WIDTH SPACE character does
// not already occur at that boundary, then the UA must insert a virtual expandable separator", which word-space-transform
// replaces by a space. The phrases of Japanese are the ones of word-break: auto-phrase (JapanesePhraseBreaker); the phrases of
// Thai, Lao, Burmese and Khmer are their words (the dictionary break engines of ICU, LineBreaker.GetWordBreakEngine), as in the WPT tests.
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>
    /// The offsets in the text of the runs before which a virtual expandable separator is inserted (in increasing order), computed
    /// when the first run of <c>word-space-transform: auto-phrase</c> is collected; null when there is none.
    /// </summary>
    private Dictionary<TextRun, List<int>>? _phraseSeparators;
    private bool _phraseSeparatorsComputed;

    /// <summary>
    /// Returns the offsets of the text of a run (<c>word-space-transform: auto-phrase</c>) before which a virtual expandable
    /// separator is inserted, or null.
    /// </summary>
    private List<int>? GetPhraseSeparators(TextRun run)
    {
        if (!_phraseSeparatorsComputed)
        {
            _phraseSeparatorsComputed = true;
            FindPhraseSeparators();
        }

        return _phraseSeparators is not null && _phraseSeparators.TryGetValue(run, out var offsets) ? offsets : null;
    }

    /// <summary>
    /// Finds the phrase boundaries of the text of the formatting context: the text of the runs is concatenated as the white space
    /// processing does (the segment breaks removed between the East Asian characters, the atomic inlines and the forced line
    /// breaks end the phrases), and the boundaries inside the runs of kanji and kana (Japanese) and of the Southeast Asian letters
    /// of a script with a word list (Thai, Lao, Burmese, Khmer) whose run has <c>word-space-transform: auto-phrase</c> and the
    /// content language of the script are kept.
    /// </summary>
    private void FindPhraseSeparators()
    {
        var text = new List<char>();
        var runs = new List<TextRun>();
        var positions = new List<(int Run, int Offset)>();
        Collect(_block);

        var span = CollectionsMarshal.AsSpan(text);
        var codePoints = new List<int>();
        var boundaries = Array.Empty<int>();
        var start = 0;
        while (start < span.Length)
        {
            var codePoint = GraphemeClusters.ReadCodePoint(span, start, out var length);
            if (JapanesePhraseBreaker.IsPhraseCharacter(codePoint))
            {
                // A run of kanji and kana: the boundaries between its code points
                codePoints.Clear();
                var indexes = new List<int>();
                var end = start;
                while (end < span.Length)
                {
                    var value = GraphemeClusters.ReadCodePoint(span, end, out var codePointLength);
                    if (!JapanesePhraseBreaker.IsPhraseCharacter(value))
                        break;

                    codePoints.Add(value);
                    indexes.Add(end);
                    end += codePointLength;
                }

                var found = new bool[codePoints.Count];
                JapanesePhraseBreaker.FindBoundaries(CollectionsMarshal.AsSpan(codePoints), found);
                for (var i = 1; i < found.Length; i++)
                {
                    if (found[i])
                    {
                        Add(indexes[i], ContentLanguage.Japanese);
                    }
                }

                start = end;
            }
            else if (LineBreaker.GetWordBreakEngine(codePoint) is { } engine)
            {
                // A run of the letters of a Southeast Asian script: its word boundaries
                var end = start;
                while (end < span.Length && engine.Handles(GraphemeClusters.ReadCodePoint(span, end, out var codePointLength)))
                {
                    end += codePointLength;
                }

                if (boundaries.Length < end - start)
                {
                    boundaries = new int[end - start];
                }

                var count = engine.FindBreaks(span[start..end], boundaries);
                var language = engine.DictionaryKind switch
                {
                    IcuWordDictionaryKind.Thai => ContentLanguage.Thai,
                    IcuWordDictionaryKind.Lao => ContentLanguage.Lao,
                    IcuWordDictionaryKind.Burmese => ContentLanguage.Burmese,
                    _ => ContentLanguage.Khmer,
                };

                foreach (var boundary in boundaries.AsSpan(0, count))
                {
                    Add(start + boundary, language);
                }

                start = end;
            }
            else
            {
                start += length;
            }
        }

        // The separator is inserted when the character after the boundary has word-space-transform: auto-phrase and the language
        void Add(int index, ContentLanguage language)
        {
            var (runIndex, offset) = positions[index];
            if (runIndex < 0)
                return;

            var run = runs[runIndex];
            if (!run.Style.Layout.Text.WordSpaceTransformAutoPhrase || GetContentLanguage(run.Style.Element) != language)
                return;

            _phraseSeparators ??= new(ReferenceEqualityComparer.Instance);
            if (!_phraseSeparators.TryGetValue(run, out var offsets))
            {
                offsets = [];
                _phraseSeparators.Add(run, offsets);
            }

            offsets.Add(offset);
        }

        void Collect(ContainerBox container)
        {
            foreach (var child in container.Children)
            {
                switch (child)
                {
                    case TextRun { Kind: TextRunKind.LineBreak }:
                        Append('\n');
                        break;

                    case TextRun { Kind: TextRunKind.WordBreakOpportunity }:
                        Append((char)0x200B);
                        break;

                    case TextRun run:
                        AppendRun(run);
                        break;

                    case ContainerBox { IsOutsideMarker: true } or ContainerBox { IsOutOfFlow: true }:
                        break;

                    case ContainerBox { IsInlineBox: true } box when box.Display.Inside != DisplayInside.Ruby || box.Display.IsInternalRubyBox:
                        Collect(box);
                        break;

                    default:
                        // An atomic inline or a ruby container ends the phrases
                        Append(BidiControls.ObjectReplacementCharacter);
                        break;
                }
            }
        }

        void Append(char c)
        {
            text.Add(c);
            positions.Add((-1, 0));
        }

        void AppendRun(TextRun run)
        {
            var runIndex = runs.Count;
            runs.Add(run);
            var data = run.Text;
            var collapses = run.Style.Layout.WhiteSpaceCollapse is WhiteSpaceCollapse.Collapse or WhiteSpaceCollapse.Discard;
            for (var i = 0; i < data.Length; i++)
            {
                var c = data[i];
                if (collapses && c is ' ' or '\t' or '\r' or '\n')
                {
                    // A sequence of white space is a space, or nothing when it is a segment break that the white space processing
                    // removes (https://drafts.csswg.org/css-text-4/#line-break-transform)
                    var end = i + 1;
                    var hasSegmentBreak = c == '\n';
                    while (end < data.Length && data[end] is ' ' or '\t' or '\r' or '\n')
                    {
                        hasSegmentBreak |= data[end] == '\n';
                        end++;
                    }

                    var before = GetCodePointBeforeSegmentBreak(CollectionsMarshal.AsSpan(text));
                    var after = GetCodePointAfterSegmentBreak(data[end..]);
                    if (!hasSegmentBreak || !IsSegmentBreakRemoved(run, before, after))
                    {
                        text.Add(' ');
                        positions.Add((runIndex, i));
                    }

                    i = end - 1;
                    continue;
                }

                text.Add(c);
                positions.Add((runIndex, i));
            }
        }
    }

    /// <summary>
    /// Inserts a virtual expandable separator of <c>word-space-transform: auto-phrase</c> before the next character of a run,
    /// replaced by a space (U+0020 or U+3000). Before the first character of the run, it is in the nearest common ancestor of the
    /// characters around it: before the inline boxes that start there.
    /// </summary>
    private void InsertPhraseSeparator(TextRun run, char separator, int domOffset, bool atRunStart, InlineItemFlags runFlags)
    {
        if (!atRunStart || _currentRubyColumn >= 0)
        {
            AppendChar(separator, domOffset);
            _inWord = false;
            _afterCollapsibleSpace = false;
            return;
        }

        var index = _items.Count;
        while (index > 0 && _items[index - 1].Kind == InlineItemKind.OpenTag && _items[index - 1].Start == _length)
        {
            index--;
        }

        // (a generated text of the container, whose text decorations and background are the ones of the container)
        var container = index < _items.Count ? _items[index].Box.Parent ?? run.Parent! : run.Parent!;
        var style = container.Style;
        var text = separator.ToString();
        var separatorRun = new TextRun(style, container.Element, pseudoElement: null, TextRunKind.Generated, node: null, text, 0, text.Length) { Parent = container };
        var flags = GetWrapFlags(style) | (runFlags & (InlineItemFlags.CollapsesSpaces | InlineItemFlags.HangsSpaces));
        _hasNoWrap |= (flags & InlineItemFlags.NoWrap) != InlineItemFlags.None;
        var start = _length;
        AppendChar(separator, 0);
        _items.Insert(index, new InlineItem
        {
            Kind = InlineItemKind.Text,
            Start = start,
            End = _length,
            Box = separatorRun,
            Container = container,
            Style = style,
            DomStart = 0,
            DomEnd = text.Length,
            Flags = flags,
        });

        // (the inline boxes that start after it)
        var items = CollectionsMarshal.AsSpan(_items);
        for (var i = index + 1; i < items.Length; i++)
        {
            items[i].Start = _length;
            items[i].End = _length;
        }

        _inWord = false;
        _afterCollapsibleSpace = false;
    }
}
