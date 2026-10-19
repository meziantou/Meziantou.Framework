using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Css.Selectors;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The collection of the items: the box tree of the block container is flattened into the text of the formatting context,
// processed as described by https://drafts.csswg.org/css-text-4/#white-space-phase-1 (phase I of the white space processing,
// the segment break transformation) and https://drafts.csswg.org/css-text-4/#text-transform-property, with the bidi control
// codes of https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection. Then the line break opportunities
// and the bidi levels of the text are computed, and the text items are shaped.
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>Whether the last character of the text is a collapsible space, or the text is empty or ends with a forced line break: a collapsible space that follows is removed.</summary>
    private bool _afterCollapsibleSpace;

    /// <summary>The bidi control codes of the override of the block container (https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection), or empty.</summary>
    private string _blockBidiStart = "";
    private string _blockBidiEnd = "";

    /// <summary>The positions of the text after which a collapsible space that wraps was collapsed with the space before it (in order).</summary>
    private readonly List<int> _wrappingCollapsedSpaces = [];

    /// <summary>Whether the last character of the text is a word character (<c>text-transform: capitalize</c>).</summary>
    private bool _inWord;

    /// <summary>
    /// Whether the white space at the start of the next text is discarded (<c>white-space-trim</c>,
    /// https://drafts.csswg.org/css-text-4/#white-space-trim): <see cref="WhiteSpaceDiscard.All"/> after the start of a box with
    /// <c>discard-inner</c>, <see cref="WhiteSpaceDiscard.Collapsible"/> after the end of a box with <c>discard-after</c>.
    /// </summary>
    private WhiteSpaceDiscard _discardLeadingWhiteSpace;
    private UnicodeBidi _blockUnicodeBidi;

    /// <summary>
    /// The positions of the text of the expandable separators replaced by <c>word-space-transform</c> (in order): the ones
    /// that turn out to immediately precede a forced line break are restored (see <see cref="RestoreTrailingSeparators"/>).
    /// </summary>
    private readonly List<int> _replacedSeparators = [];

    /// <summary>Whether an atomic inline is in a box whose <c>letter-spacing</c> is not zero (see <see cref="ApplyAtomicLetterSpacing"/>).</summary>
    private bool _hasLetterSpacedAtomics;

    /// <summary>The white space discarded by <c>white-space-trim</c>.</summary>
    private enum WhiteSpaceDiscard : byte
    {
        None,
        Collapsible,
        All,
    }

    /// <summary>Flattens the content of the block container into <see cref="_items"/> and prepares the line breaking.</summary>
    private void CollectItems()
    {
        _items.Clear();
        ClearRuby();
        _length = 0;
        _phraseSeparators = null;
        _phraseSeparatorsComputed = false;
        _wrappingCollapsedSpaces.Clear();
        _replacedSeparators.Clear();
        _hasLetterSpacedAtomics = false;
        _hasMandatoryBreakCharacters = false;
        _afterCollapsibleSpace = true;
        _inWord = false;
        _openBidiBoxes.Clear();

        var layout = _block.Style.Layout;
        var discardInner = (layout.Text.WhiteSpaceTrim & WhiteSpaceTrim.DiscardInner) != WhiteSpaceTrim.None;
        _discardLeadingWhiteSpace = discardInner ? WhiteSpaceDiscard.All : WhiteSpaceDiscard.None;
        var unicodeBidi = GetBlockUnicodeBidi(_block);
        _blockUnicodeBidi = unicodeBidi;
        var isRightToLeft = layout.Direction == Direction.Rtl;
        _needsBidi = isRightToLeft || unicodeBidi == UnicodeBidi.Plaintext;
        _hasNoWrap = layout.TextWrapMode == TextWrapMode.NoWrap;
        _hasClonedEdges = false;

        // https://drafts.csswg.org/css-writing-modes-4/#bidi-control-codes-injection: the override of a block container
        var blockStart = BidiControls.GetBlockContainerStart(unicodeBidi, isRightToLeft);
        var blockEnd = BidiControls.GetBlockContainerEnd(unicodeBidi);
        _blockBidiStart = blockStart;
        _blockBidiEnd = blockEnd;
        if (blockStart.Length > 0)
        {
            AddControl(_block, blockStart);
        }

        CollectChildren(_block);
        if (discardInner)
        {
            DiscardTrailingWhiteSpace(collapsibleOnly: false);
        }

        _discardLeadingWhiteSpace = WhiteSpaceDiscard.None;
        RestoreTrailingSeparators();
        if (blockEnd.Length > 0)
        {
            AddControl(_block, blockEnd);
        }

        if (_hasLetterSpacedAtomics)
        {
            ApplyAtomicLetterSpacing();
        }

        // The break opportunities are computed when a line does not fit (see BreakLine)
        _opportunitiesComputed = false;
        if (_needsBidi)
        {
            ResolveBidi(unicodeBidi, isRightToLeft);
        }

        ShapeItems();
    }

    private void CollectChildren(ContainerBox container)
    {
        foreach (var child in container.Children)
        {
            CollectChild(container, child);
        }
    }

    /// <summary>Collects the items of a child of a box of the formatting context (the block container or an inline box).</summary>
    private void CollectChild(ContainerBox container, LayoutBox child)
    {
        if (child is TextRun run)
        {
            CollectText(run);
            return;
        }

        var box = (ContainerBox)child;
        if (box.IsOutsideMarker)
            return;

        if (box.IsOutOfFlow)
        {
            // The floats are laid out now (their containing block is the block container) and placed when the line breaking
            // meets them, the absolutely positioned boxes by their containing block (their static position is recorded when the
            // line is built). The intrinsic sizes use the contributions of the floats.
            if (PositionedLayout.IsAbsolutelyPositioned(box))
            {
                AddItem(new InlineItem { Kind = InlineItemKind.OutOfFlow, Start = _length, End = _length, Box = box, Container = container, Style = box.Style });
                return;
            }

            var (floatFragment, floatMinContent, floatInlineSize) = box.InitialLetter is null ? LayoutAtomicOrFloat(box) : LayoutInitialLetter(box);
            AddItem(new InlineItem
            {
                Kind = InlineItemKind.OutOfFlow,
                Start = _length,
                End = _length,
                Box = box,
                Container = container,
                Style = box.Style,
                Fragment = floatFragment,
                InlineSize = floatInlineSize,
                MinContentInlineSize = floatMinContent,
                Flags = InlineItemFlags.Float,
            });
            return;
        }

        if (box.IsInlineBox)
        {
            // https://drafts.csswg.org/css-ruby-1/#ruby-layout: the bases and the annotations of a ruby container are paired in
            // ruby columns
            if (box.Display.Inside == DisplayInside.Ruby && !box.Display.IsInternalRubyBox)
            {
                CollectRuby(box);
                return;
            }

            OpenInlineBox(box);
            CollectChildren(box);
            CloseInlineBox(box);
            return;
        }

        // An atomic inline (https://drafts.csswg.org/css-display/#atomic-inline) is laid out as a unit, before the line breaking
        var (fragment, minContentInlineSize, inlineSize) = LayoutAtomicOrFloat(box);
        var start = _length;
        AppendChar(BidiControls.ObjectReplacementCharacter, -1);
        AddItem(new InlineItem
        {
            Kind = InlineItemKind.Atomic,
            Start = start,
            End = _length,
            Box = box,
            Container = container,
            Style = box.Style,
            Fragment = fragment,
            InlineSize = inlineSize,
            MinContentInlineSize = minContentInlineSize,
            Flags = GetWrapFlags(container.Style),
        });

        _hasLetterSpacedAtomics |= container.Style.Layout.Text.LetterSpacing != 0;
        _afterCollapsibleSpace = false;
        _inWord = false;
        _discardLeadingWhiteSpace = WhiteSpaceDiscard.None;
    }

    /// <summary>
    /// Adds the letter spacing of the runs of atomic inlines to the inline size of their last atomic inline
    /// (https://drafts.csswg.org/css-text-4/#letter-spacing-property): "For the purpose of letter-spacing, each consecutive run
    /// of atomic inlines (such as images and inline blocks) is treated as a single typographic character unit". As for the
    /// characters, the spacing of the box that contains the atomic inline is added after the unit (on its inline-end side in a
    /// left-to-right line), and it is removed at the end of a line. The boundaries of the inline boxes, the out-of-flow boxes and
    /// the invisible formatting characters between two atomic inlines do not end a run.
    /// </summary>
    private void ApplyAtomicLetterSpacing()
    {
        var items = System.Runtime.InteropServices.CollectionsMarshal.AsSpan(_items);
        for (var i = 0; i < items.Length; i++)
        {
            ref var item = ref items[i];
            if (item.Kind != InlineItemKind.Atomic)
                continue;

            var spacing = item.Container.Style.Layout.Text.LetterSpacing;
            if (spacing == 0)
                continue;

            var next = i + 1;
            while (next < items.Length && items[next].Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.OutOfFlow or InlineItemKind.Control)
            {
                next++;
            }

            if (next < items.Length && items[next].Kind == InlineItemKind.Atomic)
                continue;

            item.InlineSize += spacing;
            item.MinContentInlineSize += spacing;
            item.Flags |= InlineItemFlags.LetterSpaced;
        }
    }

    /// <summary>
    /// Lays out an atomic inline or a float in the content box of the block container, and returns its fragment and the inline
    /// size of its margin box; when the intrinsic sizes are computed, returns its min-content and max-content contributions
    /// instead (https://drafts.csswg.org/css-sizing-3/#intrinsic-contribution), without fragment.
    /// </summary>
    private (BoxFragment? Fragment, double MinContentInlineSize, double InlineSize) LayoutAtomicOrFloat(ContainerBox box)
    {
        if (_measuring)
        {
            var contribution = IntrinsicSizing.GetContribution(_context, box, _writingDirection, _childSpace.PercentageBlockSize, quirksPercentageBlockSize: _childSpace.QuirksPercentageBlockSize);
            return (null, contribution.MinContent, contribution.MaxContent);
        }

        var fragment = _context.LayoutBox(box, _childSpace);
        var margin = _writingDirection.ToLogical(fragment.Margin);
        var inlineSize = margin.InlineStart + _writingDirection.ToLogical(fragment.Size).Inline + margin.InlineEnd;
        return (fragment, inlineSize, inlineSize);
    }

    /// <summary>
    /// The start of an inline box: its inline-start margin, border and padding (on the first part of a box split around blocks,
    /// https://drafts.csswg.org/css2/#anonymous-block-level), and the bidi control codes of its <c>unicode-bidi</c>.
    /// </summary>
    private void OpenInlineBox(ContainerBox box)
    {
        var layout = box.Style.Layout;

        // https://drafts.csswg.org/css-text-4/#white-space-trim: "discard-before: collapse all collapsible whitespace immediately
        // before the start of the element"; "discard-inner: discard all whitespace at the beginning and end of the element"
        var whiteSpaceTrim = layout.Text.WhiteSpaceTrim;
        if (whiteSpaceTrim != WhiteSpaceTrim.None && box.PreviousContinuation is null)
        {
            if ((whiteSpaceTrim & WhiteSpaceTrim.DiscardBefore) != WhiteSpaceTrim.None)
            {
                DiscardTrailingWhiteSpace(collapsibleOnly: true);
            }

            if ((whiteSpaceTrim & WhiteSpaceTrim.DiscardInner) != WhiteSpaceTrim.None)
            {
                _discardLeadingWhiteSpace = WhiteSpaceDiscard.All;
            }
        }

        // (with box-decoration-break: clone, every fragment of the box has its edges: the parts of a box split around blocks too)
        var clones = ClonesInlineEdges(box);
        var (margin, borderAndPadding) = box.PreviousContinuation is null || clones ? GetInlineEdgeParts(box, start: true) : default;
        AddItem(new InlineItem { Kind = InlineItemKind.OpenTag, Start = _length, End = _length, Box = box, Container = box, Style = box.Style, InlineSize = margin + borderAndPadding, Flags = (margin != 0 || borderAndPadding != 0 ? InlineItemFlags.HasEdges : InlineItemFlags.None) | (clones ? InlineItemFlags.ClonesEdges : InlineItemFlags.None) });
        _hasClonedEdges |= clones;
        _hasNoWrap |= layout.TextWrapMode == TextWrapMode.NoWrap;
        var unicodeBidi = GetUnicodeBidi(box);
        if (unicodeBidi != UnicodeBidi.Normal)
        {
            _needsBidi = true;
            AddControl(box, BidiControls.GetInlineStart(unicodeBidi, layout.Direction == Direction.Rtl));
            _openBidiBoxes.Add(box);
        }
    }

    private void CloseInlineBox(ContainerBox box)
    {
        var layout = box.Style.Layout;
        var unicodeBidi = GetUnicodeBidi(box);
        if (unicodeBidi != UnicodeBidi.Normal)
        {
            AddControl(box, BidiControls.GetInlineEnd(unicodeBidi));
            _openBidiBoxes.RemoveAt(_openBidiBoxes.Count - 1);
        }

        var whiteSpaceTrim = layout.Text.WhiteSpaceTrim;
        if (whiteSpaceTrim != WhiteSpaceTrim.None && box.Continuation is null)
        {
            if ((whiteSpaceTrim & WhiteSpaceTrim.DiscardInner) != WhiteSpaceTrim.None)
            {
                DiscardTrailingWhiteSpace(collapsibleOnly: false);
            }

            // "discard-after: collapse all collapsible whitespace immediately after the end of the element"
            if ((whiteSpaceTrim & WhiteSpaceTrim.DiscardAfter) != WhiteSpaceTrim.None)
            {
                _discardLeadingWhiteSpace = WhiteSpaceDiscard.Collapsible;
            }
        }

        var clones = ClonesInlineEdges(box);
        var (margin, borderAndPadding) = box.Continuation is null || clones ? GetInlineEdgeParts(box, start: false) : default;
        AddItem(new InlineItem { Kind = InlineItemKind.CloseTag, Start = _length, End = _length, Box = box, Container = box, Style = box.Style, InlineSize = margin + borderAndPadding, Flags = (margin != 0 || borderAndPadding != 0 ? InlineItemFlags.HasEdges : InlineItemFlags.None) | (clones ? InlineItemFlags.ClonesEdges : InlineItemFlags.None) });
    }

    /// <summary>
    /// Whether the inline edges of an inline box are repeated on each of its fragments (<c>box-decoration-break: clone</c>,
    /// https://drafts.csswg.org/css-break-4/#break-decoration: "Each box fragment is independently wrapped with the border,
    /// padding, and margin").
    /// </summary>
    private static bool ClonesInlineEdges(ContainerBox box)
    {
        var layout = box.Style.Layout;
        return layout.MayHaveBoxEdges && layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
    }

    /// <summary>
    /// The inline edges that <c>box-decoration-break: clone</c> repeats on a line that starts at an item, for the inline boxes that
    /// are open there (they started on a previous line): the sum of their inline-start edges, at the start of the line, and of
    /// their inline-end edges, at the end of the line when they are still open there.
    /// </summary>
    private (double Start, double End) GetClonedEdgesAtLineStart(int itemIndex)
    {
        var items = Items;
        if (itemIndex >= items.Length)
            return default;

        // The box of an open tag starts on the line: its own tag has its inline-start edges
        ref readonly var item = ref items[itemIndex];
        var start = 0d;
        var end = 0d;
        for (var box = item.Kind == InlineItemKind.OpenTag ? item.Container.Parent : item.Container; box is not null && !ReferenceEquals(box, _block); box = box.Parent)
        {
            if (ClonesInlineEdges(box))
            {
                var (startMargin, startBorderAndPadding) = GetInlineEdgeParts(box, start: true);
                var (endMargin, endBorderAndPadding) = GetInlineEdgeParts(box, start: false);
                start += startMargin + startBorderAndPadding;
                end += endMargin + endBorderAndPadding;
            }
        }

        return (start, end);
    }

    /// <summary>
    /// Discards the white space at the end of the text collected so far (<c>white-space-trim</c>): the spaces, the tabs and the
    /// preserved segment breaks (only the collapsible spaces when <paramref name="collapsibleOnly"/>), up to an atomic inline or
    /// the edges of an inline box.
    /// </summary>
    private void DiscardTrailingWhiteSpace(bool collapsibleOnly)
    {
        for (var index = _items.Count - 1; index >= 0; index--)
        {
            var item = _items[index];
            switch (item.Kind)
            {
                case InlineItemKind.OpenTag or InlineItemKind.CloseTag when item.InlineSize == 0:
                case InlineItemKind.OutOfFlow:
                    continue;

                case InlineItemKind.ForcedBreak when !collapsibleOnly && item.End == _length && item.Box is TextRun { Kind: TextRunKind.Text }:
                    _length = item.Start;
                    _items.RemoveAt(index);
                    continue;

                case InlineItemKind.Text when item.End == _length:
                    if (collapsibleOnly && !item.HasFlag(InlineItemFlags.CollapsesSpaces))
                        return;

                    var end = item.End;
                    while (end > item.Start && _text[end - 1] is ' ' or '\t')
                    {
                        end--;
                    }

                    _length = end;
                    if (end > item.Start)
                    {
                        item.End = end;
                        _items[index] = item;
                        return;
                    }

                    _items.RemoveAt(index);
                    _afterCollapsibleSpace = true;
                    continue;

                default:
                    return;
            }
        }
    }

    /// <summary>
    /// Returns the margin, and the sum of the border and padding, of the inline-start (or inline-end) side of an inline box, in
    /// its own direction (https://drafts.csswg.org/css2/#inline-width): the percentages refer to the inline size of the
    /// containing block.
    /// </summary>
    private (double Margin, double BorderAndPadding) GetInlineEdgeParts(ContainerBox box, bool start)
    {
        if (!box.Style.Layout.MayHaveBoxEdges)
            return default;

        var boxModel = box.Style.Layout.Box;
        var side = start ? WritingDirection.FromStyle(box.Style.Layout).InlineStart : WritingDirection.FromStyle(box.Style.Layout).InlineEnd;
        var margin = boxModel.Margin.Get(side).Resolve(_percentageBasis);
        var padding = Math.Max(0, boxModel.Padding.Get(side).Resolve(_percentageBasis));
        return (margin, boxModel.BorderWidth.Get(side) + padding);
    }

    /// <summary>Adds bidi control codes (or the U+200B of a <c>wbr</c>), which are not displayed.</summary>
    private void AddControl(ContainerBox box, string codes)
    {
        var start = _length;
        foreach (var c in codes)
        {
            AppendChar(c, -1);
        }

        AddItem(new InlineItem { Kind = InlineItemKind.Control, Start = start, End = _length, Box = box, Container = box, Style = box.Style });
    }

    /// <summary>
    /// Adds a forced line break. The bidi control codes of the open inline boxes are closed before it and reopened after it,
    /// since a forced line break ends the bidi paragraph (https://drafts.csswg.org/css-writing-modes-4/#bidi-paragraph-breaks).
    /// </summary>
    private void AddForcedBreak(TextRun run, int domOffset)
    {
        RestoreTrailingSeparators();
        for (var i = _openBidiBoxes.Count - 1; i >= 0; i--)
        {
            AddControl(_openBidiBoxes[i], BidiControls.GetInlineEnd(GetUnicodeBidi(_openBidiBoxes[i])));
        }

        // The override of the block container applies to each of its bidi paragraphs
        if (_blockBidiEnd.Length > 0)
        {
            AddControl(_block, _blockBidiEnd);
        }

        var start = _length;
        AppendChar('\n', domOffset);
        AddItem(new InlineItem { Kind = InlineItemKind.ForcedBreak, Start = start, End = _length, Box = run, Container = run.Parent!, Style = run.Style, DomStart = domOffset, DomEnd = domOffset + 1 });
        if (_blockBidiStart.Length > 0)
        {
            AddControl(_block, _blockBidiStart);
        }

        foreach (var box in _openBidiBoxes)
        {
            AddControl(box, BidiControls.GetInlineStart(GetUnicodeBidi(box), box.Style.Layout.Direction == Direction.Rtl));
        }

        _afterCollapsibleSpace = true;
        _inWord = false;
        _discardLeadingWhiteSpace = WhiteSpaceDiscard.None;
    }

    /// <summary>
    /// Whether the text collected so far is empty or ends with a forced line break, ignoring the inline box boundaries, the bidi
    /// control codes and the expandable separators that were not replaced: <c>word-space-transform</c> "must not replace
    /// expandable separators immediately preceding or following a forced line break (ignoring any intervening inline box
    /// boundaries, and associated margin/border/padding)" (https://drafts.csswg.org/css-text-4/#word-space-transform). The start
    /// and the end of the block container are handled as the forced line breaks (WPT css/css-text/word-space-transform/word-space-transform-010.html,
    /// word-space-transform-012.html), so that no space is inserted at the edges of the lines.
    /// </summary>
    private bool IsAfterForcedBreak()
    {
        for (var i = _length - 1; i >= 0; i--)
        {
            var c = _text[i];
            if (c == '\n')
                return true;

            if (c != (char)0x200B && !IsBidiControl(c))
                return false;
        }

        return true;
    }

    private static bool IsBidiControl(char c) => c is >= (char)0x202A and <= (char)0x202E or >= (char)0x2066 and <= (char)0x2069 or (char)0x200E or (char)0x200F or (char)0x061C;

    /// <summary>
    /// Restores the expandable separators replaced by <c>word-space-transform</c> at the end of the text collected so far (see
    /// <see cref="IsAfterForcedBreak"/>): a forced line break or the end of the block container follows them.
    /// </summary>
    private void RestoreTrailingSeparators()
    {
        var separators = _replacedSeparators;
        if (separators.Count == 0)
            return;

        for (var i = _length - 1; i >= 0; i--)
        {
            var c = _text[i];
            if (c == (char)0x200B || IsBidiControl(c))
                continue;

            if (separators.Count == 0 || separators[^1] != i)
                return;

            separators.RemoveAt(separators.Count - 1);
            _text[i] = (char)0x200B;

            // The space of a wbr element is an item of its own: the U+200B is a control again
            for (var index = _items.Count - 1; index >= 0 && _items[index].End > i; index--)
            {
                var item = _items[index];
                if (item.Kind == InlineItemKind.Text && item.Start == i && item.End == i + 1 && item.Box is TextRun { Kind: TextRunKind.WordBreakOpportunity })
                {
                    item.Kind = InlineItemKind.Control;
                    item.Flags = InlineItemFlags.None;
                    _items[index] = item;
                    break;
                }
            }
        }
    }

    /// <summary>
    /// Adds the text of a run after the white space processing (phase I, https://drafts.csswg.org/css-text-4/#white-space-phase-1)
    /// and the text transformation (https://drafts.csswg.org/css-text-4/#text-transform-property).
    /// </summary>
    private void CollectText(TextRun run)
    {
        switch (run.Kind)
        {
            case TextRunKind.LineBreak:
                // https://html.spec.whatwg.org/multipage/rendering.html#phrasing-content-3: a br element is a forced line break
                AddForcedBreak(run, run.Start);
                return;

            case TextRunKind.WordBreakOpportunity:
                // https://drafts.csswg.org/css-text-4/#word-space-transform: a wbr element is an expandable separator, replaced
                // as the U+200B of the texts are (the text of its run)
                if (run.Style.Layout.Text.WordSpaceTransform != WordSpaceTransform.None && !IsAfterForcedBreak())
                    break;

                // A wbr element is a line break opportunity (U+200B), without advance
                var start = _length;
                AppendChar((char)0x200B, run.Start);
                AddItem(new InlineItem { Kind = InlineItemKind.Control, Start = start, End = _length, Box = run, Container = run.Parent!, Style = run.Style });
                return;
        }

        var style = run.Style;
        var layout = style.Layout;
        var collapse = layout.WhiteSpaceCollapse;

        // https://drafts.csswg.org/css-ruby-1/#white-space: "convert all forced line breaks inside ruby annotations [...] as defined
        // for collapsible segment breaks"; as in Gecko, the preserved segment breaks of the bases too (a ruby column is not broken)
        var suppressBreaks = _rubyDepth > 0;
        if (suppressBreaks && collapse == WhiteSpaceCollapse.PreserveBreaks)
        {
            collapse = WhiteSpaceCollapse.Collapse;
        }

        // https://drafts.csswg.org/css-writing-modes-4/#text-combine-layout: the text combined upright is composed "ignoring
        // letter-spacing and any forced line breaks [...] similar to the contents of an inline-block box with a horizontal
        // writing mode": its forced line breaks are spaces, and the collapsible white space at its start and at its end is
        // removed, as at the start and at the end of a line
        var isCombined = IsCombinedUpright(style);
        if (isCombined)
        {
            suppressBreaks = true;
            if (collapse == WhiteSpaceCollapse.PreserveBreaks)
            {
                collapse = WhiteSpaceCollapse.Collapse;
            }
        }

        var transform = TextTransformer.ResolveMathAuto(layout.TextTransform, run.Text);
        var language = transform != TextTransform.None ? TextTransformer.GetCaseLanguage(run.Element) : CaseLanguage.None;
        var flags = GetWrapFlags(style);
        if (isCombined)
        {
            flags |= InlineItemFlags.CombinedUpright;
        }

        _hasNoWrap |= flags != InlineItemFlags.None;
        switch (collapse)
        {
            case WhiteSpaceCollapse.Collapse or WhiteSpaceCollapse.Discard or WhiteSpaceCollapse.PreserveBreaks:
                flags |= InlineItemFlags.CollapsesSpaces;
                break;
            case WhiteSpaceCollapse.Preserve or WhiteSpaceCollapse.PreserveSpaces when layout.TextWrapMode == TextWrapMode.Wrap:
                // https://drafts.csswg.org/css-text-4/#white-space-phase-2: the preserved spaces at the end of a line hang
                flags |= InlineItemFlags.HangsSpaces;
                break;
        }

        var wordSpace = layout.Text.WordSpaceTransform;
        var text = run.Text;
        var baseOffset = run.Start;
        var itemStart = _length;
        var domStart = baseOffset;
        var first = 0;
        if (_discardLeadingWhiteSpace != WhiteSpaceDiscard.None)
        {
            // white-space-trim: the white space at the start of a box with discard-inner, or after a box with discard-after
            if (_discardLeadingWhiteSpace == WhiteSpaceDiscard.All || (flags & InlineItemFlags.CollapsesSpaces) != InlineItemFlags.None)
            {
                while (first < text.Length && text[first] is ' ' or '\t' or '\n' or '\r')
                {
                    first++;
                }
            }

            if (first < text.Length)
            {
                _discardLeadingWhiteSpace = WhiteSpaceDiscard.None;
            }

            domStart = baseOffset + first;
        }

        if (isCombined && (flags & InlineItemFlags.CollapsesSpaces) != InlineItemFlags.None)
        {
            while (first < text.Length && text[first] is ' ' or '\t' or '\n' or '\r')
            {
                first++;
            }

            if (first == text.Length)
            {
                // A composition that only has white space is an empty square of 1em, which is not removed at the end of a line (as
                // in Blink), unless the white space collapses with the collapsible space before it
                if (text.Length > 0 && !_afterCollapsibleSpace)
                {
                    AppendChar(' ', baseOffset);
                    _inWord = false;
                    _afterCollapsibleSpace = true;
                    AddTextItem(run, itemStart, baseOffset, run.End, flags & ~InlineItemFlags.CollapsesSpaces);
                }

                return;
            }

            domStart = baseOffset + first;
        }

        // The virtual expandable separators at the phrase boundaries of word-space-transform: auto-phrase
        var phraseSeparators = layout.Text.WordSpaceTransformAutoPhrase ? GetPhraseSeparators(run) : null;
        var nextPhraseSeparator = 0;
        for (var i = first; i < text.Length; i++)
        {
            var c = text[i];
            if (phraseSeparators is not null && nextPhraseSeparator < phraseSeparators.Count && phraseSeparators[nextPhraseSeparator] <= i)
            {
                if (phraseSeparators[nextPhraseSeparator] == i)
                {
                    // (before the first character of the run, the separator is an item of its own)
                    var atRunStart = _length == itemStart;
                    InsertPhraseSeparator(run, wordSpace == WordSpaceTransform.Space ? ' ' : (char)0x3000, baseOffset + i, atRunStart, flags);
                    if (atRunStart)
                    {
                        itemStart = _length;
                    }
                }

                nextPhraseSeparator++;
            }

            if (c == (char)0x200B && wordSpace != WordSpaceTransform.None && !IsAfterForcedBreak())
            {
                // https://drafts.csswg.org/css-text-4/#word-space-transform: "Expandable separators within the child text of
                // this element are replaced by U+0020 SPACE" (or U+3000). The transformation is applied "after Phase I:
                // Collapsing and Transformation" (https://drafts.csswg.org/css-text-4/#text-transform-order), so the space
                // does not collapse with the collapsible spaces around it, and before the text transformation (full-width
                // makes it a U+3000)
                _replacedSeparators.Add(_length);
                AppendTextChar(wordSpace == WordSpaceTransform.Space ? " " : "\U00003000", 0, baseOffset + i, transform, language);
                _inWord = false;
                continue;
            }

            if (collapse is WhiteSpaceCollapse.Collapse or WhiteSpaceCollapse.Discard or WhiteSpaceCollapse.PreserveBreaks)
            {
                if (c is ' ' or '\t' or '\r')
                {
                    // The spaces and tabs before a segment break are removed; a sequence of spaces collapses to one space
                    var next = i + 1;
                    while (next < text.Length && text[next] is ' ' or '\t' or '\r')
                    {
                        next++;
                    }

                    if (collapse != WhiteSpaceCollapse.Discard && (next >= text.Length || text[next] != '\n'))
                    {
                        AppendCollapsibleSpace(baseOffset + i, transform, language, wraps: (flags & InlineItemFlags.NoWrap) == InlineItemFlags.None);
                    }

                    i = next - 1;
                    continue;
                }

                if (c == '\n')
                {
                    // The spaces and tabs after a segment break are removed, and a segment break that follows another one too
                    // (https://drafts.csswg.org/css-text-4/#line-break-transform)
                    var next = i + 1;
                    while (next < text.Length && text[next] is ' ' or '\t' or '\r' or '\n')
                    {
                        if (text[next] == '\n' && collapse == WhiteSpaceCollapse.PreserveBreaks)
                            break;

                        next++;
                    }

                    if (collapse == WhiteSpaceCollapse.PreserveBreaks)
                    {
                        // The preserved segment breaks are forced line breaks
                        AddTextItem(run, itemStart, domStart, baseOffset + i, flags);
                        AddForcedBreak(run, baseOffset + i);
                        itemStart = _length;
                        domStart = baseOffset + i + 1;
                    }
                    else if (collapse == WhiteSpaceCollapse.Collapse)
                    {
                        // The segment break is removed between some East Asian characters, else it is a space
                        var before = GetCodePointBeforeSegmentBreak(_text.AsSpan(0, _length));
                        var after = GetCodePointAfterSegmentBreak(text[next..]);
                        if (!IsSegmentBreakRemoved(run, before, after))
                        {
                            AppendCollapsibleSpace(baseOffset + i, transform, language, wraps: (flags & InlineItemFlags.NoWrap) == InlineItemFlags.None);
                        }
                    }

                    i = next - 1;
                    continue;
                }
            }
            else
            {
                switch (c)
                {
                    case '\n' when collapse != WhiteSpaceCollapse.PreserveSpaces && !suppressBreaks:
                        // https://drafts.csswg.org/css-text-4/#preserved-newline: a preserved segment break is a forced line break
                        AddTextItem(run, itemStart, domStart, baseOffset + i, flags);
                        AddForcedBreak(run, baseOffset + i);
                        itemStart = _length;
                        domStart = baseOffset + i + 1;
                        continue;

                    case '\n' or '\r':
                        // preserve-spaces: the segment breaks are spaces; the carriage returns are spaces
                        AppendTextChar(" ", 0, baseOffset + i, transform, language);
                        continue;

                    case '\t':
                        // https://drafts.csswg.org/css-text-4/#white-space-phase-2: a preserved tab advances to the next tab stop, an
                        // item of its own whose advance depends on its position on the line (it is shaped as a space)
                        AddTextItem(run, itemStart, domStart, baseOffset + i, flags);
                        AppendChar(c, baseOffset + i);
                        AddTextItem(run, _length - 1, baseOffset + i, baseOffset + i + 1, flags | InlineItemFlags.Tab);
                        itemStart = _length;
                        domStart = baseOffset + i + 1;
                        _inWord = false;
                        _afterCollapsibleSpace = false;
                        continue;

                    case ' ':
                        AppendTextChar(" ", 0, baseOffset + i, transform, language);
                        _inWord = false;
                        continue;
                }
            }

            // (a character may be transformed with the characters that follow it, e.g. a Greek letter with its accents)
            i += AppendTextChar(text, i, baseOffset + i, transform, language) - 1;
        }

        if (isCombined && (flags & InlineItemFlags.CollapsesSpaces) != InlineItemFlags.None)
        {
            // The collapsible space at the end of the composition; the spaces inside of it are not removed at the end of a line
            // (the collapsible spaces that follow the composition collapse with it)
            if (_length > itemStart && _afterCollapsibleSpace && _text[_length - 1] == ' ')
            {
                _length--;
            }

            flags &= ~InlineItemFlags.CollapsesSpaces;
        }

        AddTextItem(run, itemStart, domStart, run.End, flags);
    }

    /// <summary>
    /// Whether a collapsible segment break of a run between two characters is removed by the segment break transformation
    /// (https://drafts.csswg.org/css-text-4/#line-break-transform): the rules that depend on the writing system apply when the
    /// content writing system of the run is Chinese, Japanese or Yi.
    /// </summary>
    private bool IsSegmentBreakRemoved(TextRun run, int before, int after)
        => LineBreaker.IsSegmentBreakRemoved(before, after)
            || (LineBreaker.IsSegmentBreakRemovedInChineseJapaneseOrYi(before, after) && GetContentLanguage(run.Style.Element) is ContentLanguage.Chinese or ContentLanguage.Japanese or ContentLanguage.Yi);

    /// <summary>The character before a segment break (the default ignorable characters other than U+200B are skipped, as in Firefox), or 0.</summary>
    private static int GetCodePointBeforeSegmentBreak(ReadOnlySpan<char> text)
    {
        var codePoint = 0;
        while (!text.IsEmpty)
        {
            codePoint = GetCodePointBefore(text);
            if (codePoint == 0x200B || !UnicodeProperties.IsDefaultIgnorable(codePoint))
                break;

            text = text[..^(codePoint > 0xFFFF ? 2 : 1)];
        }

        return codePoint;
    }

    /// <summary>The character after a segment break (the default ignorable characters other than U+200B are skipped, as in Firefox), or 0.</summary>
    private static int GetCodePointAfterSegmentBreak(ReadOnlySpan<char> text)
    {
        var codePoint = 0;
        for (var i = 0; i < text.Length; i += codePoint > 0xFFFF ? 2 : 1)
        {
            codePoint = GetCodePointAt(text, i);
            if (codePoint == 0x200B || !UnicodeProperties.IsDefaultIgnorable(codePoint))
                break;
        }

        return codePoint;
    }

    private static int GetCodePointAt(ReadOnlySpan<char> text, int index)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
            return char.ConvertToUtf32(c, text[index + 1]);

        return c;
    }

    private static int GetCodePointBefore(ReadOnlySpan<char> text)
    {
        var last = text[^1];
        if (char.IsLowSurrogate(last) && text.Length > 1 && char.IsHighSurrogate(text[^2]))
            return char.ConvertToUtf32(text[^2], last);

        return last;
    }

    private void AddTextItem(TextRun run, int start, int domStart, int domEnd, InlineItemFlags flags)
    {
        if (_length == start)
            return;

        // https://drafts.csswg.org/css-writing-modes-4/#valdef-text-combine-upright-digits: "Attempt to typeset horizontally each
        // maximal sequence of consecutive ASCII digits (U+0030–U+0039) that has as many or fewer characters than the specified
        // integer such that it takes up the space of a single character within the vertical line box": each one is an item
        // (the digits put in full-width form by text-transform are converted back when they are combined, see Shape)
        var layout = run.Style.Layout;
        var maxDigits = layout.IsVerticalTypographicMode && (flags & (InlineItemFlags.Tab | InlineItemFlags.CombinedUpright)) == InlineItemFlags.None ? layout.Text.TextCombineUpright.Digits : 0;
        var fullWidth = (layout.TextTransform & TextTransform.FullWidth) != TextTransform.None;
        var itemStart = start;
        for (var i = start; maxDigits > 0 && i < _length; i++)
        {
            if (!IsDigit(_text[i]))
                continue;

            var end = i + 1;
            while (end < _length && IsDigit(_text[end]))
            {
                end++;
            }

            if (end - i <= maxDigits && (i > start || start == 0 || !IsDigit(_text[start - 1])))
            {
                Add(itemStart, i, flags);
                Add(i, end, (flags | InlineItemFlags.CombinedUpright | InlineItemFlags.NoWrap) & ~(InlineItemFlags.CollapsesSpaces | InlineItemFlags.HangsSpaces));
                _hasNoWrap = true;
                itemStart = end;
            }

            i = end - 1;
        }

        Add(itemStart, _length, flags);

        bool IsDigit(char c) => char.IsAsciiDigit(c) || (fullWidth && c is >= (char)0xFF10 and <= (char)0xFF19);

        void Add(int itemStart, int itemEnd, InlineItemFlags itemFlags)
        {
            if (itemStart == itemEnd)
                return;

            AddItem(new InlineItem
            {
                Kind = InlineItemKind.Text,
                Start = itemStart,
                End = itemEnd,
                Box = run,
                Container = run.Parent!,
                Style = run.Style,
                DomStart = itemStart == start ? Math.Max(domStart, _offsets[itemStart]) : _offsets[itemStart],
                DomEnd = itemEnd == _length ? domEnd : _offsets[itemEnd],
                Flags = itemFlags,
            });
        }
    }

    /// <summary>
    /// Appends a collapsible space, unless it follows another collapsible space ("any collapsible space immediately following
    /// another collapsible space, even one outside the boundary of the inline containing that space, provided both spaces are
    /// within the same inline formatting context, is collapsed").
    /// </summary>
    private void AppendCollapsibleSpace(int domOffset, TextTransform transform, CaseLanguage language, bool wraps)
    {
        if (!_afterCollapsibleSpace)
        {
            AppendTextChar(" ", 0, domOffset, transform, language);
            _afterCollapsibleSpace = true;
        }
        else if (wraps && _length > 0 && (_wrappingCollapsedSpaces.Count == 0 || _wrappingCollapsedSpaces[^1] != _length))
        {
            // The collapsed space "is invisible, but retains its soft wrap opportunity, if any"
            _wrappingCollapsedSpaces.Add(_length);
        }

        _inWord = false;
    }

    /// <summary>
    /// Appends the character at an index of the text of a run, transformed by <c>text-transform</c>; returns the number of
    /// characters of the run that were transformed with it (1 without transformation).
    /// </summary>
    private int AppendTextChar(ReadOnlySpan<char> text, int index, int domOffset, TextTransform transform, CaseLanguage language)
    {
        var consumed = 1;
        if (transform == TextTransform.None)
        {
            AppendChar(text[index], domOffset);
            _inWord = TextTransformer.IsWordCharacter(text[index]);
        }
        else
        {
            Span<char> transformed = stackalloc char[TextTransformer.MaxLength];
            var count = TextTransformer.Transform(text, index, transform, language, wordStart: !_inWord, transformed, out consumed);
            for (var i = 0; i < count; i++)
            {
                // The characters of the transformed text map to the characters of the source in order
                AppendChar(transformed[i], domOffset + Math.Min(i, consumed - 1));
            }

            _inWord = TextTransformer.IsWordCharacter(text, index);
        }

        // https://drafts.csswg.org/css-text-4/#white-space-phase-1: the white space is processed "ignoring bidi formatting
        // characters (characters with the Bidi_Control property) as if they were not there": a collapsible space after a
        // collapsible space and a bidi control character collapses
        if (!IsBidiControl(text[index]))
        {
            _afterCollapsibleSpace = false;
        }

        return consumed;
    }

    private void AppendChar(char c, int domOffset)
    {
        if (_length == _text.Length)
        {
            Array.Resize(ref _text, _text.Length * 2);
            Array.Resize(ref _offsets, _text.Length);
        }

        _text[_length] = c;
        _offsets[_length] = domOffset;
        _length++;

        // The forced line breaks of UAX #14 that are not items (LB4, LB5: BK, NL, and a CR that is not converted to a space)
        _hasMandatoryBreakCharacters |= c is '\v' or '\f' or '\r' or (char)0x85 or (char)0x2028 or (char)0x2029;

        // The characters below U+0590 are left-to-right or neutral: the bidi algorithm is not needed
        if (c >= 0x0590 && !_needsBidi)
        {
            _needsBidi = char.IsSurrogate(c) || CharacterProperties.GetBidiClass(c) is BidiClass.R or BidiClass.AL or BidiClass.AN
                or BidiClass.LRE or BidiClass.RLE or BidiClass.LRO or BidiClass.RLO or BidiClass.PDF or BidiClass.LRI or BidiClass.RLI or BidiClass.FSI or BidiClass.PDI;
        }
    }

    private void AddItem(InlineItem item)
    {
        // The items of a ruby column know their column, the items of the annotations are marked (see CollectRuby)
        if (_currentRubyColumn >= 0)
        {
            if (item.RubyColumn == 0)
            {
                item.RubyColumn = _currentRubyColumn + 1;
            }

            if (_rubyAnnotationDepth > 0)
            {
                item.Flags |= InlineItemFlags.RubyAnnotation;
            }
        }

        _items.Add(item);
    }

    /// <summary>
    /// The <c>unicode-bidi</c> that applies to the inline content of a block container
    /// (https://drafts.csswg.org/css-writing-modes-4/#unicode-bidi: the override, the isolation and <c>plaintext</c> of a block
    /// container apply to its inline content): the property is not inherited, so the inline content of an anonymous block box
    /// (the text between the block-level children of a block container) uses the value of the block container it is wrapped in.
    /// </summary>
    private static UnicodeBidi GetBlockUnicodeBidi(ContainerBox block)
    {
        while (block is { Kind: ContainerBoxKind.Anonymous, IsInlineBox: false, Display.Internal: DisplayInternal.None, Parent: { Display.Inside: DisplayInside.Flow or DisplayInside.FlowRoot } parent })
        {
            block = parent;
        }

        return block.Style.Layout.UnicodeBidi;
    }

    /// <summary>
    /// The used <c>unicode-bidi</c> of an inline box: https://drafts.csswg.org/css-ruby-1/#bidi: "Bidi isolation is forced on all
    /// internal ruby boxes and the ruby container: the normal and embed values of unicode-bidi compute to isolate, and
    /// bidi-override computes to isolate-override".
    /// </summary>
    private static UnicodeBidi GetUnicodeBidi(ContainerBox box)
    {
        var unicodeBidi = box.Style.Layout.UnicodeBidi;
        if (box.Display.Inside != DisplayInside.Ruby && !box.Display.IsInternalRubyBox)
            return unicodeBidi;

        return unicodeBidi switch
        {
            UnicodeBidi.Normal or UnicodeBidi.Embed => UnicodeBidi.Isolate,
            UnicodeBidi.BidiOverride => UnicodeBidi.IsolateOverride,
            _ => unicodeBidi,
        };
    }

    /// <summary>
    /// The wrapping flags of the text of a style: <c>text-wrap-mode: nowrap</c>, or a composition combined upright (no wrap
    /// opportunity inside, https://drafts.csswg.org/css-writing-modes-4/#text-combine-layout: "the UA must not break the
    /// composition").
    /// </summary>
    private static InlineItemFlags GetWrapFlags(ComputedStyle style) => style.Layout.TextWrapMode == TextWrapMode.NoWrap || IsCombinedUpright(style) ? InlineItemFlags.NoWrap : InlineItemFlags.None;

    /// <summary>
    /// Whether the text of a style is combined upright (https://drafts.csswg.org/css-writing-modes-4/#text-combine-upright):
    /// <c>text-combine-upright: all</c> in a <c>vertical-*</c> writing mode ("This property has no effect on boxes in horizontal
    /// writing mode", nor in the <c>sideways-*</c> modes, which use the horizontal typographic mode). The sequences of digits
    /// of <c>digits</c> are combined per item (<see cref="InlineItemFlags.CombinedUpright"/>, see <see cref="AddTextItem"/>).
    /// </summary>
    internal static bool IsCombinedUpright(ComputedStyle style) => style.Layout.IsVerticalTypographicMode && style.Layout.Text.TextCombineUpright.IsAll;

    /// <summary>
    /// Computes the soft wrap opportunities of the text (https://drafts.csswg.org/css-text-4/#line-breaking): the opportunities
    /// of UAX #14 with the tailorings of the styles of the items, the opportunities around the atomic inlines, the automatic
    /// hyphenation opportunities, and no soft wrap opportunity where <c>text-wrap-mode</c> is <c>nowrap</c>.
    /// </summary>
    private void ComputeBreakOpportunities()
    {
        _opportunitiesComputed = true;
        if (_opportunities.Length < _length)
        {
            _opportunities = new LineBreakOpportunity[Math.Max(_length, _opportunities.Length * 2)];
        }

        if (_length == 0)
            return;

        _breakRuns.Clear();
        _lastHyphenatedWordStart = -1;
        var hasAutomaticHyphenation = false;
        foreach (ref readonly var item in Items)
        {
            // The text of the ruby annotations is not part of the text whose opportunities are computed (see FindOpportunitiesWithoutAnnotations)
            if (!item.HasText || (_hasRubyAnnotations && item.HasFlag(InlineItemFlags.RubyAnnotation)))
                continue;

            var options = GetLineBreakOptions(item.Kind is InlineItemKind.Text or InlineItemKind.ForcedBreak ? item.Style : item.Container.Style);
            if (_breakRuns.Count == 0 || _breakRuns[^1].Options != options)
            {
                _breakRuns.Add(new LineBreakOptionsRun(_breakRuns.Count == 0 ? 0 : item.Start, options));
                hasAutomaticHyphenation |= options.Hyphens == HyphensMode.Auto;
            }
        }

        var opportunities = _opportunities.AsSpan(0, _length);
        if (_hasRubyAnnotations)
        {
            FindOpportunitiesWithoutAnnotations(opportunities);
        }
        else
        {
            _lineBreaker.FindOpportunities(_text.AsSpan(0, _length), CollectionsMarshalAsSpan(_breakRuns), opportunities);
        }

        // https://drafts.csswg.org/css-text-4/#line-break-details: "For Web-compatibility there is a soft wrap opportunity before
        // and after each replaced element or other atomic inline, even when adjacent to a character that would normally
        // suppress them, including U+00A0 NO-BREAK SPACE. However, with the exception of U+00A0 NO-BREAK SPACE, there must be
        // no soft wrap opportunity between atomic inlines and adjacent characters belonging to the Unicode GL, WJ, or ZWJ line
        // breaking classes"
        foreach (ref readonly var item in Items)
        {
            if (item.Kind != InlineItemKind.Atomic || item.HasFlag(InlineItemFlags.RubyAnnotation))
                continue;

            if (item.Start > 0 && opportunities[item.Start] != LineBreakOpportunity.Mandatory && _text[item.Start - 1] != '\n'
                && !IsGlue(GetCodePointBefore(_text.AsSpan(0, item.Start))))
            {
                opportunities[item.Start] = LineBreakOpportunity.Allowed;
            }

            if (item.End < _length && opportunities[item.End] == LineBreakOpportunity.None && !IsCombiningControl(_text[item.End])
                && !IsGlue(GetCodePointAt(_text.AsSpan(0, _length), item.End)))
            {
                opportunities[item.End] = LineBreakOpportunity.Allowed;
            }
        }

        // https://drafts.csswg.org/css-text-4/#hyphenation: the automatic hyphenation opportunities of hyphens: auto
        if (hasAutomaticHyphenation)
        {
            AddAutomaticHyphenationOpportunities(opportunities);
        }

        // The syllables of Pinyin (see AddPinyinOpportunities)
        AddPinyinOpportunities(opportunities);

        if (_hasNoWrap)
        {
            ApplyNoWrap(opportunities);
        }

        // https://quirks.spec.whatwg.org/#the-table-cell-width-calculation-quirk: "In quirks mode, for the purpose of calculating
        // the min-content width of an inline formatting context for which a table cell cell is the containing block, if cell has
        // a computed value of the width property that is auto, img elements that are inline-level replaced elements in that inline
        // formatting context must not have a soft wrap opportunity before or after them"
        if (_measuring && _context.IsQuirksMode && _block.Display.Internal == DisplayInternal.TableCell && _block.Style.Layout.Box.GetSize(_writingDirection.IsHorizontal).IsAuto)
        {
            foreach (ref readonly var item in Items)
            {
                if (item.Kind == InlineItemKind.Atomic && item.Box is ContainerBox { Kind: ContainerBoxKind.Replaced, PseudoElement: null, Element: Dom.HtmlImageElement })
                {
                    if (opportunities[item.Start] != LineBreakOpportunity.Mandatory)
                    {
                        opportunities[item.Start] = LineBreakOpportunity.None;
                    }

                    if (item.End < _length && opportunities[item.End] != LineBreakOpportunity.Mandatory)
                    {
                        opportunities[item.End] = LineBreakOpportunity.None;
                    }
                }
            }
        }

        static ReadOnlySpan<LineBreakOptionsRun> CollectionsMarshalAsSpan(List<LineBreakOptionsRun> list) => System.Runtime.InteropServices.CollectionsMarshal.AsSpan(list);

        // The characters that forbid the break between them and an atomic inline
        static bool IsGlue(int codePoint) => codePoint != 0xA0 && CharacterProperties.GetLineBreakClass(codePoint) is LineBreakClass.GL or LineBreakClass.WJ or LineBreakClass.ZWJ;

        // The bidi controls are combining marks for the line breaking: no opportunity before them
        static bool IsCombiningControl(char c) => c is >= (char)0x202A and <= (char)0x202E or >= (char)0x2066 and <= (char)0x2069;
    }

    /// <summary>
    /// Removes the soft wrap opportunities where wrapping is not allowed (https://drafts.csswg.org/css-text-4/#text-wrap-mode):
    /// "for soft wrap opportunities defined by the boundary between two characters or atomic inlines, the white-space property
    /// on the nearest common ancestor of the two characters controls breaking".
    /// </summary>
    private void ApplyNoWrap(Span<LineBreakOpportunity> opportunities)
    {
        var previous = -1;
        var items = Items;
        for (var index = 0; index < items.Length; index++)
        {
            ref readonly var item = ref items[index];
            if (!item.HasText || (_hasRubyAnnotations && item.HasFlag(InlineItemFlags.RubyAnnotation)))
                continue;

            // The opportunities inside the item
            if (item.Kind == InlineItemKind.Text && item.HasFlag(InlineItemFlags.NoWrap))
            {
                for (var i = item.Start + 1; i < item.End; i++)
                {
                    if (opportunities[i] != LineBreakOpportunity.Mandatory)
                    {
                        opportunities[i] = LineBreakOpportunity.None;
                    }
                }
            }

            // The opportunity before the item: "For soft wrap opportunities created by characters that disappear at the line
            // break (e.g. U+0020 SPACE), properties on the box directly containing that character control the line breaking at
            // that opportunity"
            if (previous >= 0 && opportunities[item.Start] is not (LineBreakOpportunity.None or LineBreakOpportunity.Mandatory))
            {
                // (a collapsed space that follows it keeps its own opportunity)
                ref readonly var previousItem = ref items[previous];
                var wrapMode = previousItem.Kind == InlineItemKind.Text && previousItem.End == item.Start && _text[item.Start - 1] == ' '
                    ? previousItem.Style.Layout.TextWrapMode
                    : GetCommonAncestor(previousItem.Container, item.Container).Style.Layout.TextWrapMode;
                if (wrapMode == TextWrapMode.NoWrap && _wrappingCollapsedSpaces.BinarySearch(item.Start) < 0)
                {
                    opportunities[item.Start] = LineBreakOpportunity.None;
                }
            }

            previous = index;
        }

        static ContainerBox GetCommonAncestor(ContainerBox first, ContainerBox second)
        {
            for (var a = first; a is not null; a = a.Parent)
            {
                for (var b = second; b is not null; b = b.Parent)
                {
                    if (ReferenceEquals(a, b))
                        return a;
                }
            }

            return first;
        }
    }

    /// <summary>The options of the line breaking of a style (https://drafts.csswg.org/css-text-4/#line-breaking).</summary>
    private LineBreakOptions GetLineBreakOptions(ComputedStyle style)
    {
        if (ReferenceEquals(style, _lastBreakOptionsStyle))
            return _lastBreakOptions;

        // (the content language only matters for line-break: auto | normal | loose and word-break: auto-phrase)
        var text = style.Layout.Text;
        var language = text.LineBreak is LineBreak.Auto or LineBreak.Normal or LineBreak.Loose || text.WordBreak == WordBreak.AutoPhrase ? GetContentLanguage(style.Element) : ContentLanguage.Other;
        var options = CreateLineBreakOptions(style, text.LineBreak is LineBreak.Auto or LineBreak.Normal or LineBreak.Loose && language is ContentLanguage.Chinese or ContentLanguage.Japanese, language == ContentLanguage.Japanese);
        _lastBreakOptionsStyle = style;
        _lastBreakOptions = options;
        return options;
    }

    /// <summary>
    /// The options of the line breaking of a style; <paramref name="isChineseOrJapanese"/> is whether the content language is
    /// Chinese or Japanese (it only matters for <c>line-break: auto | normal | loose</c>), <paramref name="isJapanese"/> whether it
    /// is Japanese (the phrases of <c>word-break: auto-phrase</c>).
    /// </summary>
    internal static LineBreakOptions CreateLineBreakOptions(ComputedStyle style, bool isChineseOrJapanese, bool isJapanese = false)
    {
        var layout = style.Layout;
        var text = layout.Text;
        return new LineBreakOptions
        {
            Strictness = text.LineBreak switch
            {
                LineBreak.Loose => LineBreakStrictness.Loose,
                LineBreak.Strict => LineBreakStrictness.Strict,
                LineBreak.Anywhere => LineBreakStrictness.Anywhere,
                _ => LineBreakStrictness.Normal,
            },
            WordBreak = text.WordBreak switch
            {
                WordBreak.BreakAll => WordBreakMode.BreakAll,
                WordBreak.KeepAll => WordBreakMode.KeepAll,
                WordBreak.Manual => WordBreakMode.Manual,
                WordBreak.AutoPhrase => WordBreakMode.AutoPhrase,
                _ => WordBreakMode.Normal,
            },

            // https://drafts.csswg.org/css-text-4/#valdef-word-break-break-word: word-break: break-word is overflow-wrap: anywhere
            OverflowWrap = text.WordBreak == WordBreak.BreakWord ? OverflowWrapMode.Anywhere : text.OverflowWrap switch
            {
                OverflowWrap.Anywhere => OverflowWrapMode.Anywhere,
                OverflowWrap.BreakWord => OverflowWrapMode.BreakWord,
                _ => OverflowWrapMode.Normal,
            },
            Hyphens = text.Hyphens switch
            {
                Hyphens.None => HyphensMode.None,
                Hyphens.Auto => HyphensMode.Auto,
                _ => HyphensMode.Manual,
            },
            BreakSpaces = layout.WhiteSpaceCollapse == WhiteSpaceCollapse.BreakSpaces,
            IsChineseOrJapanese = isChineseOrJapanese,
            IsJapanese = isJapanese,
        };
    }

    /// <summary>
    /// The languages of the content (https://html.spec.whatwg.org/multipage/dom.html#language) that change the line breaking:
    /// the content writing system (https://drafts.csswg.org/css-text-4/#content-writing-system) of Chinese and Japanese (the rules of
    /// <c>line-break</c>, the phrases of Japanese, the segment break transformation), of Yi (the segment break transformation) and
    /// of the Southeast Asian languages whose words are segmented with a dictionary (the phrases of <c>word-space-transform: auto-phrase</c>).
    /// </summary>
    private enum ContentLanguage : byte
    {
        Other,
        Chinese,
        Japanese,
        Thai,
        Lao,
        Burmese,
        Khmer,
        Yi,
    }

    /// <summary>
    /// The content language of an element of the formatting context: the language of the block container (computed once),
    /// unless an inline ancestor has a <c>lang</c> attribute.
    /// </summary>
    private ContentLanguage GetContentLanguage(Dom.Element element)
    {
        var blockElement = _block.Element;
        for (var current = element; current is not null && !ReferenceEquals(current, blockElement); current = current.ParentElement)
        {
            if (current.HasAttribute("lang") || current.HasAttribute("xml:lang"))
                return GetLanguageKind(HtmlElementStates.GetLanguage(element));
        }

        if (_blockContentLanguage is null)
        {
            _blockContentLanguage = GetLanguageKind(HtmlElementStates.GetLanguage(blockElement ?? element));
        }

        return _blockContentLanguage.Value;

        static ContentLanguage GetLanguageKind(string? language)
        {
            if (string.IsNullOrEmpty(language))
                return ContentLanguage.Other;

            var primary = language.AsSpan();
            var dash = primary.IndexOf('-');
            if (dash >= 0)
            {
                // The content writing system is the one of the script subtag of the language when it has one
                // (https://drafts.csswg.org/css-text-4/#content-writing-system: `en-Hrkt` is a Japanese writing system and
                // `ja-Hang` a Korean one): a subtag of four letters after the primary language (and its extended subtags)
                foreach (var range in language.AsSpan(dash + 1).Split('-'))
                {
                    var subtag = language.AsSpan(dash + 1)[range];
                    if (subtag.Length == 4 && !subtag.ContainsAnyExceptInRange('A', 'z'))
                    {
                        return IsAny(subtag, "Hani", "Hans", "Hant", "Bopo") ? ContentLanguage.Chinese
                            : IsAny(subtag, "Jpan", "Hrkt", "Hira", "Kana") ? ContentLanguage.Japanese
                            : subtag.Equals("Thai", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Thai
                            : subtag.Equals("Laoo", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Lao
                            : subtag.Equals("Mymr", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Burmese
                            : subtag.Equals("Khmr", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Khmer
                            : subtag.Equals("Yiii", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Yi
                            : ContentLanguage.Other;
                    }

                    // The script follows the extended language subtags (three letters); anything else ends the search
                    if (subtag.Length != 3)
                        break;
                }

                primary = primary[..dash];
            }

            return primary.Equals("zh", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Chinese
                : primary.Equals("ja", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Japanese
                : primary.Equals("th", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Thai
                : primary.Equals("lo", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Lao
                : primary.Equals("my", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Burmese
                : primary.Equals("km", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Khmer
                : primary.Equals("ii", StringComparison.OrdinalIgnoreCase) ? ContentLanguage.Yi
                : ContentLanguage.Other;
        }

        static bool IsAny(ReadOnlySpan<char> subtag, string first, string second, string third, string fourth)
            => subtag.Equals(first, StringComparison.OrdinalIgnoreCase) || subtag.Equals(second, StringComparison.OrdinalIgnoreCase)
                || subtag.Equals(third, StringComparison.OrdinalIgnoreCase) || subtag.Equals(fourth, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Resolves the bidi levels of the text (https://drafts.csswg.org/css-writing-modes-4/#text-direction) and splits the text
    /// items at the level changes, so that each text item has one level (and one direction for the shaping).
    /// </summary>
    private void ResolveBidi(UnicodeBidi unicodeBidi, bool isRightToLeft)
    {
        if (_bidiClasses.Length < _length)
        {
            _bidiClasses = new BidiClass[Math.Max(_length, _bidiClasses.Length * 2)];
        }

        var text = _text.AsSpan(0, _length);
        var classes = _bidiClasses.AsSpan(0, _length);
        BidiResolver.GetClasses(text, classes);

        // https://drafts.csswg.org/css-writing-modes-4/#bidi-atomic-inline: an atomic inline is a neutral character, except a
        // replaced element with unicode-bidi: embed or bidi-override
        foreach (ref readonly var item in Items)
        {
            if (item.Kind == InlineItemKind.Atomic)
            {
                var box = (ContainerBox)item.Box;
                var layout = box.Style.Layout;
                classes[item.Start] = BidiControls.GetAtomicInlineClass(box.Kind == ContainerBoxKind.Replaced, layout.UnicodeBidi, layout.Direction == Direction.Rtl);
            }
            else if (item.Kind == InlineItemKind.Text && item.Style.Layout.IsUprightVerticalText)
            {
                // https://drafts.csswg.org/css-writing-modes-4/#valdef-text-orientation-upright: "for the purposes of bidi
                // reordering, causes all characters to be treated as strong LTR"
                foreach (ref var bidiClass in classes[item.Start..item.End])
                {
                    if (bidiClass != BidiClass.B)
                    {
                        bidiClass = BidiClass.L;
                    }
                }
            }
        }

        _bidi.Resolve(text, classes, BidiControls.GetParagraphDirection(unicodeBidi, isRightToLeft));
        var levels = _bidi.Levels;

        _splitItems.Clear();

        // The ruby columns reference their items by index: the new index of each item is recorded
        var remap = HasRuby;
        if (remap)
        {
            EnsureItemRemap(_items.Count);
        }

        var oldIndex = 0;
        foreach (var item in Items)
        {
            if (remap)
            {
                _itemRemap[oldIndex++] = _splitItems.Count;
            }

            if (!item.HasText)
            {
                // The items without character have the level of the next character
                var copy = item;
                copy.BidiLevel = item.Start < _length ? levels[item.Start] : GetParagraphLevel(_length - 1);
                _splitItems.Add(copy);
                continue;
            }

            var start = item.Start;
            while (start < item.End)
            {
                var level = levels[start];
                var end = start + 1;
                while (end < item.End && levels[end] == level)
                {
                    end++;
                }

                var part = item;
                part.Start = start;
                part.End = end;
                part.BidiLevel = level;
                if (item.Kind == InlineItemKind.Text)
                {
                    part.DomStart = start == item.Start ? item.DomStart : _offsets[start];
                    part.DomEnd = end == item.End ? item.DomEnd : _offsets[end];
                }

                _splitItems.Add(part);
                start = end;
            }
        }

        if (remap)
        {
            _itemRemap[oldIndex] = _splitItems.Count;
        }

        (_items, _splitItems) = (_splitItems, _items);
        _splitItems.Clear();
        if (remap)
        {
            RemapRubyItems();
        }
    }

    /// <summary>
    /// The level of the bidi paragraph that contains a character. Without text (a block container that only has out-of-flow
    /// boxes or empty inline boxes), it is the level of the direction of the block container (0 with <c>unicode-bidi: plaintext</c>,
    /// https://www.unicode.org/reports/tr9/#P3).
    /// </summary>
    private byte GetParagraphLevel(int index)
    {
        if (!_needsBidi)
            return 0;

        if (_length == 0)
            return _block.Style.Layout.Direction == Direction.Rtl && _blockUnicodeBidi != UnicodeBidi.Plaintext ? (byte)1 : (byte)0;

        return _bidi.Paragraphs[_bidi.GetParagraphIndex(Math.Max(0, index))].Level;
    }

    /// <summary>
    /// Shapes the text items. The text of sibling inline boxes is shaped together when nothing breaks the shaping at their
    /// boundaries (https://drafts.csswg.org/css-text-3/#boundary-shaping): the items of a group have the same fonts and shaping
    /// options, the same bidi level, and no margin, border, padding, non-baseline vertical alignment or bidi isolation between
    /// them (e.g. a ligature or the Arabic joining across a <c>span</c> that only changes the color); each item gets the part
    /// of the glyphs of its characters. The other items keep the joining of the cursive scripts with their neighbors
    /// (<see cref="TextShapingOptions.Context"/>).
    /// </summary>
    private void ShapeItems()
    {
        var items = Items;
        for (var i = 0; i < items.Length; i++)
        {
            if (items[i].Kind != InlineItemKind.Text)
                continue;

            // The items shaped with this one
            var last = i;
            while (GetNextShapingItem(last) is var next and >= 0 && CanShapeTogether(last, next))
            {
                last = next;
            }

            if (last == i)
            {
                items[i].Shaped = Shape(i, items[i].Style);
                continue;
            }

            ShapeGroup(i, last);
            i = last;
        }
    }

    /// <summary>
    /// The index of the text item after a text item when only the starts and the ends of inline boxes are between them (the
    /// boundary of the boxes may not break the shaping), or -1.
    /// </summary>
    private int GetNextShapingItem(int index)
    {
        var items = Items;
        for (var i = index + 1; i < items.Length; i++)
        {
            switch (items[i].Kind)
            {
                case InlineItemKind.Text:
                    return items[i].Start == items[index].End ? i : -1;
                case InlineItemKind.OpenTag or InlineItemKind.CloseTag:
                    continue;

                // An absolutely positioned box is not on the line
                case InlineItemKind.OutOfFlow when !items[i].IsFloat:
                    continue;
                default:
                    return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// The index of the text item before a text item when only the starts and the ends of inline boxes are between them, or -1.
    /// </summary>
    private int GetPreviousShapingItem(int index)
    {
        var items = Items;
        for (var i = index - 1; i >= 0; i--)
        {
            switch (items[i].Kind)
            {
                case InlineItemKind.Text:
                    return items[i].End == items[index].Start ? i : -1;
                case InlineItemKind.OpenTag or InlineItemKind.CloseTag:
                    continue;

                // An absolutely positioned box is not on the line
                case InlineItemKind.OutOfFlow when !items[i].IsFloat:
                    continue;
                default:
                    return -1;
            }
        }

        return -1;
    }

    /// <summary>
    /// Whether the boundary between two consecutive text items does not break the shaping
    /// (https://drafts.csswg.org/css-text-3/#boundary-shaping): the same bidi level, no tab, and no margin, border or padding
    /// and only the baseline alignment for the inline boxes that start or end between them.
    /// </summary>
    private bool IsShapingBoundaryTransparent(int first, int second)
    {
        var items = Items;
        if (items[first].BidiLevel != items[second].BidiLevel || items[first].HasFlag(InlineItemFlags.Tab) || items[second].HasFlag(InlineItemFlags.Tab))
            return false;

        if (items[first].HasFlag(InlineItemFlags.CombinedUpright) || items[second].HasFlag(InlineItemFlags.CombinedUpright))
            return false;

        for (var i = first + 1; i < second; i++)
        {
            if (items[i].Kind == InlineItemKind.OutOfFlow)
                continue;

            if (GetVisualEdgeSize(items, i, items[first].BidiLevel) != 0)
                return false;

            var verticalAlign = items[i].Style.Layout.VerticalAlign;
            if (verticalAlign.AlignmentBaseline != AlignmentBaseline.Baseline || verticalAlign.BaselineShift.Kind != BaselineShiftKind.Length || verticalAlign.BaselineShift.Length != LengthPercentage.Zero && verticalAlign.BaselineShift.Length != default)
                return false;
        }

        return true;

        // The margin, border and padding of the edge of an inline box that is between the texts on the line: the edge of the tag
        // when the text is in the direction of the box, else the other edge of the box (the inline-start edge of a left-to-right
        // box is on the left of its text, which is after the text before it in a right-to-left run: WPT
        // css/css-text/boundary-shaping/boundary-shaping-009.html)
        static double GetVisualEdgeSize(ReadOnlySpan<InlineItem> items, int index, byte level)
        {
            ref readonly var tag = ref items[index];
            if (tag.Kind is not (InlineItemKind.OpenTag or InlineItemKind.CloseTag) || ((level & 1) != 0) == (tag.Container.Style.Layout.Direction == Direction.Rtl))
                return tag.InlineSize;

            if (tag.Kind == InlineItemKind.OpenTag)
            {
                for (var j = index + 1; j < items.Length; j++)
                {
                    if (items[j].Kind == InlineItemKind.CloseTag && items[j].Container == tag.Container)
                        return items[j].InlineSize;
                }
            }
            else
            {
                for (var j = index - 1; j >= 0; j--)
                {
                    if (items[j].Kind == InlineItemKind.OpenTag && items[j].Container == tag.Container)
                        return items[j].InlineSize;
                }
            }

            return tag.InlineSize;
        }
    }

    /// <summary>Whether two consecutive text items are shaped together: their boundary is transparent, and they have the same fonts and shaping options.</summary>
    private bool CanShapeTogether(int first, int second)
    {
        var items = Items;
        if (!IsShapingBoundaryTransparent(first, second))
            return false;

        var firstStyle = items[first].Style;
        var secondStyle = items[second].Style;
        if (ReferenceEquals(firstStyle, secondStyle))
            return true;

        var isRightToLeft = (items[first].BidiLevel & 1) != 0;
        return ReferenceEquals(firstStyle.Fonts, secondStyle.Fonts) && GetShapingOptions(firstStyle, isRightToLeft) == GetShapingOptions(secondStyle, isRightToLeft);
    }

    /// <summary>Shapes the text of the items from <paramref name="first"/> to <paramref name="last"/> together, and gives each item the glyphs of its characters.</summary>
    private void ShapeGroup(int first, int last)
    {
        var items = Items;
        ref readonly var firstItem = ref items[first];
        var start = firstItem.Start;
        var end = items[last].End;
        var isRightToLeft = (firstItem.BidiLevel & 1) != 0;
        ReadOnlySpan<char> text = _text.AsSpan(start, end - start);
        if (isRightToLeft)
        {
            text = Mirror(text);
        }

        var options = GetShapingOptions(firstItem.Style, isRightToLeft) with { Context = GetShapingContext(first, last) };
        var shaped = RunShapeCache.Shape(firstItem.Box as TextRun, text, firstItem.Style.Fonts, options);
        for (var i = first; i <= last; i++)
        {
            if (items[i].Kind != InlineItemKind.Text)
                continue;

            var part = shaped.Slice(items[i].Start - start, items[i].End - start);
            items[i].Shaped = ApplyItemSpacing(i, items[i].Style, part);
        }
    }

    /// <summary>
    /// The characters around the text items from <paramref name="first"/> to <paramref name="last"/> that change their shaping:
    /// the closest joining characters of the neighbor items across a transparent boundary (the Arabic joining continues across
    /// the elements, as in Chrome), or none.
    /// </summary>
    private ShapingContext GetShapingContext(int first, int last)
    {
        var before = 0;
        var after = 0;
        if (GetPreviousShapingItem(first) is var previous and >= 0 && IsShapingBoundaryTransparent(previous, first))
        {
            before = GetJoiningCharacter(Items[previous].End - 1, -1, Items[previous].Start);
        }

        if (GetNextShapingItem(last) is var next and >= 0 && IsShapingBoundaryTransparent(last, next))
        {
            after = GetJoiningCharacter(Items[next].Start, 1, Items[next].End);
        }

        return new ShapingContext(before, after);
    }

    /// <summary>
    /// The first character from <paramref name="index"/> in a direction that is not transparent for the joining (the marks
    /// are skipped), when it joins with its neighbors (a joining type other than non-joining), else 0.
    /// </summary>
    private int GetJoiningCharacter(int index, int step, int limit)
    {
        for (var i = index; step < 0 ? i >= limit : i < limit; i += step)
        {
            int codePoint = _text[i];
            if (step < 0 && char.IsLowSurrogate(_text[i]) && i > limit && char.IsHighSurrogate(_text[i - 1]))
            {
                i--;
                codePoint = char.ConvertToUtf32(_text[i], _text[i + 1]);
            }
            else if (step > 0 && char.IsHighSurrogate(_text[i]) && i + 1 < limit && char.IsLowSurrogate(_text[i + 1]))
            {
                codePoint = char.ConvertToUtf32(_text[i], _text[i + 1]);
                i++;
            }

            var type = ArabicShaper.GetJoiningType(codePoint);
            if (type == (int)ArabicJoiningType.T)
                continue;

            return type == (int)ArabicJoiningType.U ? 0 : codePoint;
        }

        return 0;
    }

    private static readonly uint HalfWidthsTag = ShapePlanBuilder.Tag("hwid");
    private static readonly uint ThirdWidthsTag = ShapePlanBuilder.Tag("twid");
    private static readonly uint QuarterWidthsTag = ShapePlanBuilder.Tag("qwid");

    /// <summary>
    /// Returns the OpenType feature of the glyphs whose advance is 1/n em for a composition of n characters
    /// (https://drafts.csswg.org/css-writing-modes-4/#text-combine-compression: "use OpenType hwid for 2 typographic character
    /// units, twid for 3 typographic character units, etc."), or null for a single character.
    /// </summary>
    private static FontFeature? GetWidthVariantFeature(ReadOnlySpan<char> text)
    {
        var count = 0;
        foreach (var rune in text.EnumerateRunes())
        {
            // (the combining marks are part of the typographic character unit of their base)
            if (UnicodeProperties.GetGraphemeClusterBreak(rune.Value) is not (UnicodeGraphemeClusterBreak.Extend or UnicodeGraphemeClusterBreak.ZWJ or UnicodeGraphemeClusterBreak.SpacingMark))
            {
                count++;
            }
        }

        return count switch
        {
            <= 1 => null,
            2 => new FontFeature(HalfWidthsTag, 1),
            3 => new FontFeature(ThirdWidthsTag, 1),
            _ => new FontFeature(QuarterWidthsTag, 1),
        };
    }

    /// <summary>
    /// Shapes a text item with a style (https://drafts.csswg.org/css-text-4/#text-shaping): the options are the used values of
    /// the style (with the zoom), the direction is the bidi level of the item, and the characters of a right-to-left item are
    /// mirrored (rule L4 of UAX #9).
    /// </summary>
    private ShapedText Shape(int index, ComputedStyle style)
    {
        ref readonly var item = ref Items[index];
        ReadOnlySpan<char> text = _text.AsSpan(item.Start, item.End - item.Start);
        var isRightToLeft = (item.BidiLevel & 1) != 0;
        if (isRightToLeft)
        {
            text = Mirror(text);
        }

        if (item.HasFlag(InlineItemFlags.CombinedUpright))
        {
            // https://drafts.csswg.org/css-writing-modes-4/#text-combine-layout: "the glyphs of the combined text are bidi-isolated
            // and composed horizontally (ignoring letter-spacing and any forced line breaks, but using the specified font
            // settings) [...] the resulting composition is treated as a single glyph"
            var options = GetShapingOptions(style, isRightToLeft) with { Orientation = TextShapingOrientation.Horizontal, LetterSpacing = 0 };
            var fontSize = style.Layout.FontSize;

            // https://drafts.csswg.org/css-writing-modes-4/#text-combine-fullwidth: "when the combined text consists of more than
            // one typographic character unit, then any full-width typographic character units should first be converted to their
            // non-full-width equivalents by reversing the algorithm defined for text-transform: full-width"
            if (text.Length > 1 && text.IndexOfAnyInRange((char)0xFF01, (char)0xFF5E) >= 0)
            {
                var narrow = text.ToArray();
                for (var i = 0; i < narrow.Length; i++)
                {
                    if (narrow[i] is >= (char)0xFF01 and <= (char)0xFF5E)
                    {
                        narrow[i] = (char)(narrow[i] - 0xFEE0);
                    }
                }

                text = narrow;
            }

            var horizontal = RunShapeCache.Shape(item.Box as TextRun, text, style.Fonts, options);

            // https://drafts.csswg.org/css-writing-modes-4/#text-combine-compression: "OpenType implementations must use
            // width-specific variants (OpenType features hwid/twid/qwid) to compress text in cases where those variants are
            // available": the 1/n-width glyphs for a composition of n characters that is wider than 1em, which is then scaled
            // when it is still wider
            if (horizontal.Width > fontSize + LayoutEpsilon && GetWidthVariantFeature(text) is { } feature)
            {
                var compressed = RunShapeCache.Shape(item.Box as TextRun, text, style.Fonts, options with { Features = FontFeatureList.With(options.Features, feature) });
                if (compressed.Width < horizontal.Width)
                {
                    horizontal = compressed;
                }
            }

            return TextShaper.CombineUpright(horizontal, fontSize);
        }

        var shaped = RunShapeCache.Shape(item.Box as TextRun, text, style.Fonts, GetShapingOptions(style, isRightToLeft) with { Context = GetShapingContext(index, index) });
        return item.HasFlag(InlineItemFlags.Tab) ? shaped : ApplyItemSpacing(index, style, shaped);
    }

    /// <summary>
    /// Shapes again the edges of the characters from <paramref name="start"/> to <paramref name="end"/> of a text item (relative
    /// to the item) on a line, where the text is unsafe to break (https://drafts.csswg.org/css-text-3/#line-breaking, as Chrome
    /// does): the glyphs from an edge to the next position that is safe to break (e.g. a kerning pair across the break) are
    /// replaced by the ones of these characters shaped alone. An edge inside a cluster (e.g. a hyphenation opportunity inside a
    /// ligature: "schiff-fahrt" with an "ff" ligature) splits it: the characters on each side of the edge are shaped alone. The
    /// joining of the cursive scripts continues across the break
    /// (https://drafts.csswg.org/css-text-3/#word-break-shaping: "as if the word were still whole").
    /// </summary>
    private ShapedText ShapeLineEdges(int index, ShapedText shaped, int start, int end, bool isFirstLine)
    {
        if (start > 0 && shaped.IsUnsafeToBreak(start))
        {
            shaped = ShapeLinePart(index, shaped, start, FindNextSafeBreak(shaped, start, end), isFirstLine);
        }

        if (end < shaped.Length && shaped.IsUnsafeToBreak(end))
        {
            // The characters after the end get their own glyphs too, so that the end of the line is a cluster boundary
            if (!shaped.IsClusterBoundary(end))
            {
                shaped = ShapeLinePart(index, shaped, end, FindNextSafeBreak(shaped, end, shaped.Length), isFirstLine);
            }

            var safe = end - 1;
            while (safe > start && !(shaped.IsClusterBoundary(safe) && !shaped.IsUnsafeToBreak(safe)))
            {
                safe--;
            }

            shaped = ShapeLinePart(index, shaped, safe, end, isFirstLine);
        }

        return shaped;

        // The first position after an edge, up to a limit, that is safe to break
        static int FindNextSafeBreak(ShapedText shaped, int edge, int limit)
        {
            var safe = edge + 1;
            while (safe < limit && !(shaped.IsClusterBoundary(safe) && !shaped.IsUnsafeToBreak(safe)))
            {
                safe++;
            }

            return safe;
        }
    }

    /// <summary>Replaces the glyphs of the characters from <paramref name="start"/> to <paramref name="end"/> of a text item by the ones of these characters shaped alone.</summary>
    private ShapedText ShapeLinePart(int index, ShapedText shaped, int start, int end, bool isFirstLine)
    {
        ref readonly var item = ref Items[index];
        var style = GetLineStyle(item, isFirstLine);
        ReadOnlySpan<char> text = _text.AsSpan(item.Start + start, end - start);
        var isRightToLeft = (item.BidiLevel & 1) != 0;
        if (isRightToLeft)
        {
            text = Mirror(text);
        }

        var itemContext = GetShapingContext(index, index);
        var context = new ShapingContext(
            start > 0 ? GetJoiningCharacter(item.Start + start - 1, -1, item.Start) : itemContext.Before,
            item.Start + end < item.End ? GetJoiningCharacter(item.Start + end, 1, item.End) : itemContext.After);
        var part = TextShaper.Shape(text, style.Fonts, GetShapingOptions(style, isRightToLeft) with { Context = context });
        return shaped.Replace(start, end, part);
    }

    /// <summary>Replaces the characters that have a mirrored glyph (https://www.unicode.org/reports/tr9/#L4).</summary>
    private ReadOnlySpan<char> Mirror(ReadOnlySpan<char> text)
    {
        var index = -1;
        for (var i = 0; i < text.Length; i++)
        {
            if (UnicodeProperties.IsBidiMirrored(text[i]) && CharacterProperties.GetBidiMirroringGlyph(text[i]) >= 0)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
            return text;

        if (_mirrorBuffer.Length < text.Length)
        {
            _mirrorBuffer = new char[Math.Max(text.Length, 64)];
        }

        var buffer = _mirrorBuffer.AsSpan(0, text.Length);
        text.CopyTo(buffer);
        for (var i = index; i < buffer.Length; i++)
        {
            var c = buffer[i];
            if (UnicodeProperties.IsBidiMirrored(c) && CharacterProperties.GetBidiMirroringGlyph(c) is var mirrored and >= 0 and <= 0xFFFF)
            {
                buffer[i] = (char)mirrored;
            }
        }

        return buffer;
    }

    /// <summary>The options of the shaping of a style: the used font size, spacing and orientation (with the zoom).</summary>
    internal static TextShapingOptions GetShapingOptions(ComputedStyle style, bool isRightToLeft)
    {
        var layout = style.Layout;
        var text = layout.Text;
        return style.ShapingOptions with
        {
            FontSize = layout.FontSize,
            IsRightToLeft = isRightToLeft,
            Orientation = layout.WritingMode switch
            {
                WritingMode.HorizontalTb => TextShapingOrientation.Horizontal,
                WritingMode.SidewaysRl or WritingMode.SidewaysLr => TextShapingOrientation.VerticalSideways,
                _ => layout.TextOrientation switch
                {
                    TextOrientation.Upright => TextShapingOrientation.VerticalUpright,
                    TextOrientation.Sideways => TextShapingOrientation.VerticalSideways,
                    _ => TextShapingOrientation.VerticalMixed,
                },
            },
            LetterSpacing = text.LetterSpacing,
            WordSpacing = text.WordSpacing,
        };
    }

    /// <summary>Returns the glyphs of a text item on a line (with the <c>::first-line</c> style on the first line).</summary>
    private ShapedText GetShaped(int index, bool isFirstLine)
    {
        if (_fontScale != 1)
            return GetScaledShaped(index, isFirstLine);

        ref var item = ref Items[index];
        if (!isFirstLine || _firstLineRoot is null)
            return item.Shaped!;

        if (item.FirstLineShaped is null)
        {
            var style = GetLineStyle(item, isFirstLine);
            if (ReferenceEquals(style, item.Style))
            {
                item.FirstLineShaped = item.Shaped;
            }
            else
            {
                ShapeFirstLineGroup(index);
            }
        }

        return item.FirstLineShaped!;
    }

    /// <summary>
    /// Shapes a text item with the styles of the first formatted line, together with its neighbors whose first-line styles have
    /// the same fonts and shaping options across a transparent boundary (https://drafts.csswg.org/css-text-3/#boundary-shaping,
    /// as <see cref="ShapeItems"/>: e.g. the kerning across a <c>span</c> styled by <c>::first-line</c>).
    /// </summary>
    private void ShapeFirstLineGroup(int index)
    {
        var first = index;
        while (GetPreviousShapingItem(first) is var previous and >= 0 && CanShapeTogetherOnFirstLine(previous, first))
        {
            first = previous;
        }

        var last = index;
        while (GetNextShapingItem(last) is var next and >= 0 && CanShapeTogetherOnFirstLine(last, next))
        {
            last = next;
        }

        var items = Items;
        if (first == last)
        {
            items[index].FirstLineShaped = Shape(index, GetLineStyle(items[index], isFirstLine: true));
            return;
        }

        ref readonly var firstItem = ref items[first];
        var firstStyle = GetLineStyle(firstItem, isFirstLine: true);
        var start = firstItem.Start;
        var end = items[last].End;
        var isRightToLeft = (firstItem.BidiLevel & 1) != 0;
        ReadOnlySpan<char> text = _text.AsSpan(start, end - start);
        if (isRightToLeft)
        {
            text = Mirror(text);
        }

        var options = GetShapingOptions(firstStyle, isRightToLeft) with { Context = GetShapingContext(first, last) };
        var shaped = RunShapeCache.Shape(firstItem.Box as TextRun, text, firstStyle.Fonts, options);
        for (var i = first; i <= last; i++)
        {
            if (items[i].Kind != InlineItemKind.Text)
                continue;

            var part = shaped.Slice(items[i].Start - start, items[i].End - start);
            items[i].FirstLineShaped = ApplyItemSpacing(i, GetLineStyle(items[i], isFirstLine: true), part);
        }
    }

    /// <summary>Whether two consecutive text items are shaped together on the first formatted line (<see cref="CanShapeTogether"/> with their first-line styles).</summary>
    private bool CanShapeTogetherOnFirstLine(int first, int second)
    {
        var items = Items;
        if (!IsShapingBoundaryTransparent(first, second))
            return false;

        var firstStyle = GetLineStyle(items[first], isFirstLine: true);
        var secondStyle = GetLineStyle(items[second], isFirstLine: true);
        if (ReferenceEquals(firstStyle, secondStyle))
            return true;

        var isRightToLeft = (items[first].BidiLevel & 1) != 0;
        return ReferenceEquals(firstStyle.Fonts, secondStyle.Fonts) && GetShapingOptions(firstStyle, isRightToLeft) == GetShapingOptions(secondStyle, isRightToLeft);
    }
}
