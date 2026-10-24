using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The fragments of a line: the leaves in visual order nested in the fragments of their inline boxes, aligned in the block
// axis (https://drafts.csswg.org/css2/#line-height, https://drafts.csswg.org/css-inline-3/#line-layout).
internal sealed partial class InlineLayoutAlgorithm
{
    private int _rootFirstChild;
    private int _rootLastChild;
    private bool _rootHasText;

    /// <summary>
    /// The position of the baseline of the fictional inline box of <c>::first-line</c> from the line-over edge of the line box,
    /// when its <c>vertical-align</c> moves it away from the baseline of the root inline box (see <see cref="AlignVertically"/>), or null.
    /// </summary>
    private double? _firstLineBoxBaseline;

    /// <summary>
    /// Places the leaves in visual order (rule L2 of UAX #9) from the line-left edge of the line box, in the fragments of their
    /// inline boxes. An inline box whose content is not contiguous after the reordering has several fragments on the line:
    /// its inline-start edges are on the fragment of its start in its direction, the inline-end ones on the fragment of its
    /// end (https://drafts.csswg.org/css-writing-modes-4/#bidi-box-model).
    /// </summary>
    private void PlaceLeaves(bool reordered)
    {
        _nodes.Clear();
        _openNodes.Clear();
        _lineBoxes.Clear();
        _rootFirstChild = -1;
        _rootLastChild = -1;
        _rootHasText = false;
        var count = _leaves.Count;
        if (!reordered)
        {
            EnsureOrderBuffers(count);
            for (var i = 0; i < count; i++)
            {
                _visualOrder[i] = i;
            }
        }
        else
        {
            if (HasRuby)
            {
                RegroupRubyLeaves(count);
            }

            CountFragments(count);
        }

        var x = 0d;
        var hasText = false;
        var hasAtomic = false;
        int? lineBreakParent = null;
        for (var v = 0; v < count; v++)
        {
            var leafIndex = _visualOrder[v];
            var leaf = _leaves[leafIndex];
            BuildChain(leaf);
            var common = 0;
            while (common < _openNodes.Count && common < _chain.Count && ReferenceEquals(_nodes[_openNodes[common]].Box, _chain[common]))
            {
                common++;
            }

            while (_openNodes.Count > common)
            {
                CloseNode(ref x);
            }

            for (var i = common; i < _chain.Count; i++)
            {
                OpenNode(_chain[i], reordered, ref x);
            }

            if (leaf.Kind == LineLeafKind.EmptyBox)
            {
                OpenNode(leaf.Box!, reordered, ref x);
                CloseNode(ref x);
                continue;
            }

            var node = new LineNode { Leaf = leafIndex, FirstChild = -1, LastChild = -1, NextSibling = -1, AlignRoot = -1 };
            if (leaf.Kind == LineLeafKind.Atomic)
            {
                hasAtomic = true;
                var fragment = Items[leaf.Item].Fragment!;
                var lineLeft = LineLeftSide;
                node.MarginLeft = fragment.Margin.Get(lineLeft);
                node.MarginRight = fragment.Margin.Get(Opposite(lineLeft));
                node.Width = _writingDirection.ToLogical(fragment.Size).Inline;

                // The letter spacing after a run of atomic inlines (see ApplyAtomicLetterSpacing) and the space of the justification
                // after it are on the inline-end side of its last atomic inline: on its line-left side in a right-to-left run
                var spacing = leaf.Width - (node.MarginLeft + node.Width + node.MarginRight);
                if (Math.Abs(spacing) < LayoutEpsilon)
                {
                    spacing = 0;
                }

                var isRightToLeft = (leaf.Level & 1) != 0;
                x += node.MarginLeft + (isRightToLeft ? spacing : 0);
                node.X = x;
                x += node.Width + node.MarginRight + (isRightToLeft ? 0 : spacing);
            }
            else
            {
                node.X = x;
                node.Width = leaf.Width;
                x += leaf.Width;
            }

            AddNode(node);
            if (leaf.Kind == LineLeafKind.RubyGap)
            {
                OnRubyGapPlaced(leaf, node.X, x);
            }

            if (leaf.Kind is LineLeafKind.Text or LineLeafKind.Hyphen)
            {
                hasText = true;
                MarkDirectText(_openNodes.Count > 0 ? _openNodes[^1] : -1);
            }
            else if (leaf.Kind == LineLeafKind.LineBreak)
            {
                lineBreakParent = _openNodes.Count > 0 ? _openNodes[^1] : -1;
            }
        }

        // A forced line break is not a text for the line height calculation quirk, except on a line without other content (as in
        // the browsers, an empty paragraph with a <br> keeps the height of a line; a <br> after an atomic inline does not make
        // its line taller: WPT quirks/line-height-trailing-collapsable-whitespace.html)
        if (!hasText && !hasAtomic && lineBreakParent is { } parent)
        {
            MarkDirectText(parent);
        }

        while (_openNodes.Count > 0)
        {
            CloseNode(ref x);
        }
    }

    /// <summary>
    /// Marks the inline box of a preserved segment break that ends the line as containing a text directly (see
    /// <see cref="LineNode.HasDirectText"/>): the root inline box when the segment break is directly in the block container, or
    /// when its inline box has no other content on the line.
    /// </summary>
    private void MarkPreservedSegmentBreak(InlineItem segmentBreak)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        for (var i = 0; i < nodes.Length; i++)
        {
            if (ReferenceEquals(nodes[i].Box, segmentBreak.Container))
            {
                MarkDirectText(i);
                return;
            }
        }

        MarkDirectText(-1);
    }

    /// <summary>Marks a node (or the root inline box, -1) as containing a text directly (see <see cref="LineNode.HasDirectText"/>).</summary>
    private void MarkDirectText(int node)
    {
        if (node >= 0)
        {
            CollectionsMarshal.AsSpan(_nodes)[node].HasDirectText = true;
        }
        else
        {
            _rootHasText = true;
        }
    }

    /// <summary>The physical side of the line-left edge of the line boxes (https://drafts.csswg.org/css-writing-modes-4/#line-left).</summary>
    private PhysicalSide LineLeftSide => new WritingDirection(_writingDirection.WritingMode, Direction.Ltr).InlineStart;

    private static PhysicalSide Opposite(PhysicalSide side) => side switch
    {
        PhysicalSide.Top => PhysicalSide.Bottom,
        PhysicalSide.Right => PhysicalSide.Left,
        PhysicalSide.Bottom => PhysicalSide.Top,
        _ => PhysicalSide.Right,
    };

    /// <summary>The inline boxes that contain a leaf, from the outermost one (the block container excluded).</summary>
    private void BuildChain(in LineLeaf leaf)
    {
        _chain.Clear();
        for (var box = leaf.Parent; box is not null && !ReferenceEquals(box, _block); box = box.Parent)
        {
            _chain.Add(box);
        }

        _chain.Reverse();
    }

    /// <summary>Counts the fragments of each inline box on a reordered line, to put the edges on the last one.</summary>
    private void CountFragments(int count)
    {
        _fragmentCounts.Clear();
        _lineBoxes.Clear();
        for (var v = 0; v < count; v++)
        {
            var leaf = _leaves[_visualOrder[v]];
            BuildChain(leaf);
            if (leaf.Kind == LineLeafKind.EmptyBox)
            {
                _chain.Add(leaf.Box!);
            }

            var common = 0;
            while (common < _lineBoxes.Count && common < _chain.Count && ReferenceEquals(_lineBoxes[common], _chain[common]))
            {
                common++;
            }

            _lineBoxes.RemoveRange(common, _lineBoxes.Count - common);
            for (var i = common; i < _chain.Count; i++)
            {
                _lineBoxes.Add(_chain[i]);
                _fragmentCounts[_chain[i]] = _fragmentCounts.GetValueOrDefault(_chain[i]) + 1;
            }

            if (leaf.Kind == LineLeafKind.EmptyBox)
            {
                _lineBoxes.RemoveAt(_lineBoxes.Count - 1);
            }
        }

        _lineBoxes.Clear();
    }

    /// <summary>Opens a fragment of an inline box at the current position, with its line-left edges when they belong to it.</summary>
    private void OpenNode(ContainerBox box, bool reordered, ref double x)
    {
        // _lineBoxes lists the boxes that already have a fragment on the line (the line-left edges are on the first one)
        var isFirst = !ContainsBox(_lineBoxes, box);
        if (isFirst)
        {
            _lineBoxes.Add(box);
        }

        var isLast = true;
        if (reordered && _fragmentCounts.TryGetValue(box, out var remaining))
        {
            _fragmentCounts[box] = remaining - 1;
            isLast = remaining <= 1;
        }

        // The inline-start edges are on the line where the box starts, the inline-end edges where it ends (box-decoration-break:
        // slice), or on every line (clone: https://drafts.csswg.org/css-break-4/#break-decoration; the line breaking reserves
        // the space of the repeated edges). In a right-to-left box, the start is on the line-right side
        var layout = box.Style.Layout;
        var leftIsStart = layout.Direction != Direction.Rtl;
        var clones = layout.MayHaveBoxEdges && layout.Fragmentation.BoxDecorationBreak == BoxDecorationBreak.Clone;
        var startsHere = clones || (ContainsBox(_openTags, box) && box.PreviousContinuation is null);
        var endsHere = clones || (ContainsBox(_closeTags, box) && box.Continuation is null);
        var hasLeft = isFirst && (leftIsStart ? startsHere : endsHere);
        var hasRight = isLast && (leftIsStart ? endsHere : startsHere);
        var (leftMargin, leftBorderPadding) = hasLeft ? GetInlineEdgeParts(box, start: leftIsStart) : default;
        var (rightMargin, rightBorderPadding) = hasRight ? GetInlineEdgeParts(box, start: !leftIsStart) : default;

        // A ruby base records the start of its slot, a ruby annotation moves over the slots of its bases (see the ruby layout)
        var rubyExtra = HasRuby ? OnRubyBoxOpened(box, ref x) : 0;
        x += leftMargin;
        var node = new LineNode
        {
            Leaf = -1,
            Box = box,
            FirstChild = -1,
            LastChild = -1,
            NextSibling = -1,
            AlignRoot = -1,
            X = x,
            MarginLeft = leftMargin,
            MarginRight = rightMargin,
            BorderPaddingLeft = leftBorderPadding,
            BorderPaddingRight = rightBorderPadding,
            HasLeftEdge = hasLeft,
            HasRightEdge = hasRight,
            HasStartEdge = leftIsStart ? hasLeft : hasRight,
            HasEndEdge = leftIsStart ? hasRight : hasLeft,
        };
        x += leftBorderPadding + rubyExtra;
        _openNodes.Add(AddNode(node));

        static bool ContainsBox(List<ContainerBox> boxes, ContainerBox box)
        {
            foreach (var item in boxes)
            {
                if (ReferenceEquals(item, box))
                    return true;
            }

            return false;
        }
    }

    private void CloseNode(ref double x)
    {
        var index = _openNodes[^1];
        _openNodes.RemoveAt(_openNodes.Count - 1);
        ref var node = ref CollectionsMarshal.AsSpan(_nodes)[index];
        if (HasRuby)
        {
            x += OnRubyBoxClosed(ref node, beforeEdges: true, ref x);
        }

        x += node.BorderPaddingRight;
        node.Width = x - node.X;
        x += node.MarginRight;
        if (HasRuby)
        {
            OnRubyBoxClosed(ref node, beforeEdges: false, ref x);
        }
    }

    /// <summary>Adds a node as the last child of the innermost open fragment (or of the line box) and returns its index.</summary>
    private int AddNode(LineNode node)
    {
        var index = _nodes.Count;
        node.Parent = _openNodes.Count > 0 ? _openNodes[^1] : -1;
        _nodes.Add(node);
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        if (node.Parent < 0)
        {
            if (_rootLastChild < 0)
            {
                _rootFirstChild = index;
            }
            else
            {
                nodes[_rootLastChild].NextSibling = index;
            }

            _rootLastChild = index;
        }
        else
        {
            ref var parent = ref nodes[node.Parent];
            if (parent.LastChild < 0)
            {
                parent.FirstChild = index;
            }
            else
            {
                nodes[parent.LastChild].NextSibling = index;
            }

            parent.LastChild = index;
        }

        return index;
    }

    /// <summary>
    /// Aligns the boxes of the line in the block axis (https://drafts.csswg.org/css2/#line-height): each inline box and atomic
    /// inline is aligned relative to its parent by <c>vertical-align</c>, the line box is as tall as the layout bounds of the
    /// boxes (the inline boxes have their line height, the root inline box gives the strut), then the boxes aligned with
    /// <c>top</c> and <c>bottom</c> are aligned with the line box. Returns the block size of the line box and the position of
    /// its baseline; <see cref="LineNode.Baseline"/> is then the position of the baseline of each node from the top of the line box.
    /// </summary>
    private (double BlockSize, double Baseline) AlignVertically(bool isFirstLine, bool isEmpty)
    {
        // (as in the browsers, the line height quirks do not apply to the lines of a list item: https://github.com/whatwg/quirks/issues/38)
        var quirks = _context.UsesLineHeightQuirks && !_block.Display.IsListItem;

        // https://drafts.csswg.org/css-inline-3/#strut: the root inline box has the font and the line height of the block container
        // (its ::first-line style on the first line, as in the browsers)
        var rootStyle = GetLineStyle(_block, isFirstLine);
        var rootMetrics = GetMetrics(rootStyle);
        var strutStyle = rootStyle;
        var strutMetrics = rootMetrics;

        // https://drafts.csswg.org/css-pseudo-4/#first-line-styling: the vertical-align of ::first-line ("any inline layout properties
        // that apply to inline elements") aligns its fictional inline box, which wraps the content of the line, relative to the root
        // inline box: the root inline box then has the strut of the block container, and the fictional box (an extra node, removed
        // before the fragments are created) has the layout bounds of the ::first-line style, as in Gecko (Blink and WebKit ignore it)
        _firstLineBoxBaseline = null;
        var wrapperIndex = -1;
        if (isFirstLine && !isEmpty && !HasRuby && _firstLineRoot is not null && rootStyle.Layout.VerticalAlign is var firstLineAlign
            && (firstLineAlign.AlignmentBaseline != AlignmentBaseline.Baseline || firstLineAlign.BaselineShift.Kind != BaselineShiftKind.Length || !firstLineAlign.BaselineShift.Length.IsZero))
        {
            strutStyle = GetLineStyle(_block, isFirstLine: false);
            strutMetrics = GetMetrics(strutStyle);
            wrapperIndex = _nodes.Count;
            _nodes.Add(new LineNode { Leaf = -1, Parent = -1, FirstChild = -1, LastChild = -1, NextSibling = -1, HasDirectText = _rootHasText });
        }

        var nodes = CollectionsMarshal.AsSpan(_nodes);
        var lineTop = double.PositiveInfinity;
        var lineBottom = double.NegativeInfinity;

        // https://quirks.spec.whatwg.org/#the-blocks-ignore-line-height-quirk: "the element's line-height must be ignored when
        // calculating the minimal height of line boxes within the element", and the line height calculation quirk
        // (https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk) applies to the root inline box: as in the browsers,
        // it does not contribute to the height of a line without text directly in it
        if (!quirks || _rootHasText)
        {
            var (rootAscentLeading, rootDescentLeading) = GetHalfLeadings(strutMetrics);
            var (rootAscent, rootDescent) = (strutMetrics.Ascent + rootAscentLeading, strutMetrics.Descent + rootDescentLeading);
            if (_rootHasText)
            {
                (rootAscent, rootDescent) = AddEmphasisMarks(strutStyle, strutMetrics, rootAscent, rootDescent);
            }

            Extend(-rootAscent, rootDescent);
        }

        if (wrapperIndex >= 0)
        {
            ref var wrapper = ref nodes[wrapperIndex];
            AlignInlineBox(ref wrapper, rootStyle, strutMetrics, quirks);
            wrapper.AlignRoot = -1;
            if (wrapper.LineRelative != BaselineShiftKind.Length)
            {
                wrapper.AlignRoot = wrapperIndex;
                wrapper.GroupTop = wrapper.Contributes ? -wrapper.Ascent : 0;
                wrapper.GroupBottom = wrapper.Contributes ? wrapper.Descent : 0;
                wrapper.Baseline = 0;
            }
            else if (wrapper.Contributes)
            {
                Extend(wrapper.Baseline - wrapper.Ascent, wrapper.Baseline + wrapper.Descent);
            }
        }

        // https://drafts.csswg.org/css-lists-3/#list-style-position-outside: the outside marker that the line places is aligned
        // with its baseline; as in the browsers, where the marker of a list item with inline content is an atomic inline of its
        // first line, its margin box makes the line taller (e.g. a marker image, or a marker with a larger font). The marker
        // placed next to a line of a descendant block container does not change the line (WPT css/css-pseudo/marker-content-010.html)
        if (_outsideMarker is { IsPlaced: false } pendingMarker && ReferenceEquals(pendingMarker.ListItem, _block) && !isEmpty && !_measuring)
        {
            var markerFragment = _context.LayoutBox(pendingMarker.Marker, _childSpace);
            if (markerFragment.FirstBaseline is { } markerBaseline && !_block.Style.Layout.HasCentralDominantBaseline)
            {
                var markerMargin = _writingDirection.ToLogical(markerFragment.Margin);
                var markerSize = _writingDirection.ToLogical(markerFragment.Size).Block;
                Extend(-markerBaseline - markerMargin.BlockStart, markerSize - markerBaseline + markerMargin.BlockEnd);
            }
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            if (i == wrapperIndex)
                continue;

            ref var node = ref nodes[i];

            // (the children of the root inline box are in the fictional inline box of ::first-line, when it has one)
            var parent = node.Parent < 0 ? wrapperIndex : node.Parent;
            var parentMetrics = parent < 0 ? rootMetrics : nodes[parent].Metrics;

            // The position of the baseline of the parent relative to the baseline of its aligned subtree (0 for its root)
            var parentBaseline = parent < 0 ? 0 : nodes[parent].Baseline;
            node.AlignRoot = parent < 0 ? -1 : nodes[parent].AlignRoot;
            if (node.Leaf >= 0)
            {
                var leaf = _leaves[node.Leaf];
                if (leaf.Kind == LineLeafKind.Atomic)
                {
                    AlignAtomic(ref node, Items[leaf.Item], parentMetrics);
                }
                else
                {
                    // The text is on the baseline of its parent; its content area is given by its font
                    node.Metrics = leaf.Kind is LineLeafKind.OutOfFlow or LineLeafKind.RubyGap ? parentMetrics : GetMetrics(GetLineStyle(Items[leaf.Item], isFirstLine));
                    node.LineRelative = BaselineShiftKind.Length;
                    node.Baseline = parentBaseline;
                    if (leaf.Kind == LineLeafKind.Text && IsTextOfContentsElement(Items[leaf.Item]))
                    {
                        // https://drafts.csswg.org/css-inline-3/#baseline-alignment: "for a glyph, the alignment baseline is always
                        // determined by the parent's dominant baseline"
                        node.Baseline -= parentMetrics.GetBaseline(parentMetrics.DominantBaseline) - node.Metrics.GetBaseline(parentMetrics.DominantBaseline);

                        // The text of a display: contents element has the font and the line height of the element, not the ones of
                        // the box it is in: as in the browsers, its layout bounds (its content area and its half-leading) make the
                        // line taller, as the ones of an inline box would
                        var (textAscentLeading, textDescentLeading) = GetHalfLeadings(node.Metrics);
                        var (textTop, textBottom) = (node.Baseline - node.Metrics.Ascent - textAscentLeading, node.Baseline + node.Metrics.Descent + textDescentLeading);
                        if (node.AlignRoot < 0)
                        {
                            Extend(textTop, textBottom);
                        }
                        else
                        {
                            ref var group = ref nodes[node.AlignRoot];
                            group.GroupTop = Math.Min(group.GroupTop, textTop);
                            group.GroupBottom = Math.Max(group.GroupBottom, textBottom);
                        }
                    }

                    // https://drafts.csswg.org/css-inline-3/#valdef-line-height-normal: with line-height: normal, the layout bounds of
                    // the box of the text include the ones of the fallback fonts of its glyphs (their ascent, descent and line gap),
                    // as in the browsers (Blink's InlineBoxState::AccumulateUsedFonts), also the hyphen of a hyphenated line
                    if (leaf.Kind is LineLeafKind.Text or LineLeafKind.Hyphen && leaf.Shaped is not null && (parent < 0 ? !quirks || _rootHasText : nodes[parent].Contributes)
                        && GetFallbackFontBounds(leaf, GetLineStyle(Items[leaf.Item], isFirstLine)) is { } fallback)
                    {
                        var (fallbackTop, fallbackBottom) = (node.Baseline - fallback.Ascent, node.Baseline + fallback.Descent);
                        if (node.AlignRoot < 0)
                        {
                            Extend(fallbackTop, fallbackBottom);
                        }
                        else
                        {
                            ref var group = ref nodes[node.AlignRoot];
                            group.GroupTop = Math.Min(group.GroupTop, fallbackTop);
                            group.GroupBottom = Math.Max(group.GroupBottom, fallbackBottom);
                        }
                    }

                    continue;
                }
            }
            else
            {
                if (node.Box is { PseudoElement: null, Element: { } breakElement } && breakElement.IsHtml(TagId.Br))
                {
                    // As in the browsers, the font and the line height of a br element do not change the height of the line it
                    // ends: its box has the layout bounds of its parent
                    node.Metrics = parentMetrics;
                    var (breakAscentLeading, breakDescentLeading) = GetHalfLeadings(parentMetrics);
                    node.Ascent = parentMetrics.Ascent + breakAscentLeading;
                    node.Descent = parentMetrics.Descent + breakDescentLeading;
                    node.Contributes = !quirks || node.HasDirectText;
                    node.Baseline = 0;
                }
                else
                {
                    AlignInlineBox(ref node, GetLineStyle(node.Box!, isFirstLine), parentMetrics, quirks);
                }

                if (HasRuby)
                {
                    // https://drafts.csswg.org/css-ruby-1/#line-height: "ruby annotation containers do not contribute to measured line
                    // height": an annotation is the root of an aligned subtree, placed over or under its bases once they are placed
                    if (IsRubyAnnotationBox(node.Box!))
                    {
                        node.IsRubyAnnotation = true;
                        node.Contributes = true;
                        node.LineRelative = BaselineShiftKind.Top;
                    }
                    else if (node.Box!.Display.Internal == DisplayInternal.RubyTextContainer)
                    {
                        node.Contributes = false;
                    }
                }
            }

            // The content of a ruby annotation is aligned in the annotation, not with the line box (top, bottom and center are
            // relative to the line of the annotation, which is as tall as its content: see SetRubyAnnotationExtent)
            if (node.LineRelative != BaselineShiftKind.Length && !node.IsRubyAnnotation && node.AlignRoot >= 0 && nodes[node.AlignRoot].IsRubyAnnotation)
            {
                node.LineRelative = BaselineShiftKind.Length;
                node.Baseline = 0;
            }

            if (node.LineRelative != BaselineShiftKind.Length)
            {
                // The root of an aligned subtree: its descendants are aligned relative to it
                node.AlignRoot = i;
                node.GroupTop = node.Contributes ? -node.Ascent : 0;
                node.GroupBottom = node.Contributes ? node.Descent : 0;
                node.Baseline = 0;
                continue;
            }

            node.Baseline += parentBaseline;
            if (!node.Contributes)
                continue;

            if (node.AlignRoot < 0)
            {
                Extend(node.Baseline - node.Ascent, node.Baseline + node.Descent);
            }
            else
            {
                ref var group = ref nodes[node.AlignRoot];
                group.GroupTop = Math.Min(group.GroupTop, node.Baseline - node.Ascent);
                group.GroupBottom = Math.Max(group.GroupBottom, node.Baseline + node.Descent);
            }
        }

        if (double.IsInfinity(lineTop))
        {
            lineTop = 0;
            lineBottom = 0;
        }

        // https://drafts.csswg.org/css2/#valdef-vertical-align-top: the aligned subtrees of top and bottom may make the line taller
        for (var i = 0; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            if (node.LineRelative == BaselineShiftKind.Length || node.AlignRoot != i || node.IsRubyAnnotation)
                continue;

            var height = node.GroupBottom - node.GroupTop;
            if (height > lineBottom - lineTop)
            {
                if (node.LineRelative == BaselineShiftKind.Bottom)
                {
                    lineTop = lineBottom - height;
                }
                else if (node.LineRelative == BaselineShiftKind.Center)
                {
                    var extra = (height - (lineBottom - lineTop)) / 2;
                    lineTop -= extra;
                    lineBottom += extra;
                }
                else
                {
                    lineBottom = lineTop + height;
                }
            }
        }

        if (isEmpty)
        {
            lineTop = 0;
            lineBottom = 0;
        }

        // The positions of the baselines from the top of the line box (the ruby annotations are placed after the other nodes; the
        // fictional inline box of ::first-line, after the nodes it contains, is placed first)
        if (wrapperIndex >= 0)
        {
            PlaceBaseline(nodes, wrapperIndex, lineTop, lineBottom);
            _firstLineBoxBaseline = nodes[wrapperIndex].Baseline;
        }

        for (var i = 0; i < nodes.Length; i++)
        {
            if (i != wrapperIndex && !(nodes[i].AlignRoot >= 0 && nodes[nodes[i].AlignRoot].IsRubyAnnotation))
            {
                PlaceBaseline(nodes, i, lineTop, lineBottom);
            }
        }

        if (wrapperIndex >= 0)
        {
            _nodes.RemoveAt(wrapperIndex);
            nodes = CollectionsMarshal.AsSpan(_nodes);
        }

        if (HasRuby)
        {
            PlaceRubyAnnotations(-lineTop);
            for (var i = 0; i < nodes.Length; i++)
            {
                ref var node = ref nodes[i];
                if (node.AlignRoot >= 0 && node.AlignRoot != i && nodes[node.AlignRoot].IsRubyAnnotation)
                {
                    node.Baseline += nodes[node.AlignRoot].Baseline;
                }
            }
        }

        return (lineBottom - lineTop, -lineTop);

        void Extend(double top, double bottom)
        {
            lineTop = Math.Min(lineTop, top);
            lineBottom = Math.Max(lineBottom, bottom);
        }

        static void PlaceBaseline(Span<LineNode> nodes, int i, double lineTop, double lineBottom)
        {
            ref var node = ref nodes[i];
            if (node.AlignRoot == i)
            {
                node.Baseline = node.LineRelative switch
                {
                    BaselineShiftKind.Bottom => lineBottom - node.GroupBottom,
                    BaselineShiftKind.Center => ((lineTop + lineBottom) / 2) - ((node.GroupTop + node.GroupBottom) / 2),
                    _ => lineTop - node.GroupTop,
                } - lineTop;
            }
            else if (node.AlignRoot >= 0)
            {
                node.Baseline += nodes[node.AlignRoot].Baseline;
            }
            else
            {
                node.Baseline -= lineTop;
            }
        }
    }

    /// <summary>
    /// Returns the layout bounds above and below the baseline of the fonts other than the first available font that render the
    /// glyphs of a text leaf whose style has <c>line-height: normal</c>: the content area of each font with the half of its line
    /// gap on each side (its normal line height), or null when the glyphs are all rendered by the first available font.
    /// </summary>
    private static (double Ascent, double Descent)? GetFallbackFontBounds(in LineLeaf leaf, ComputedStyle style)
    {
        var layout = style.Layout;
        if (layout.LineHeight.Kind != LineHeightKind.Normal || leaf.GlyphCount == 0)
            return null;

        var primary = style.Fonts.PrimaryFont;
        var primaryEmSize = primary.GetEmSize(layout.FontSize);
        var glyphEnd = leaf.GlyphStart + leaf.GlyphCount;
        (double Ascent, double Descent)? bounds = null;
        foreach (var run in leaf.Shaped!.Runs)
        {
            if (run.GlyphStart >= glyphEnd || run.GlyphStart + run.GlyphCount <= leaf.GlyphStart || (ReferenceEquals(run.Font, primary) && run.BaseEmSize == primaryEmSize)
                || !HasVisibleGlyph(leaf.Shaped.Glyphs[Math.Max(run.GlyphStart, leaf.GlyphStart)..Math.Min(run.GlyphStart + run.GlyphCount, glyphEnd)]))
            {
                continue;
            }

            var metrics = run.Font.GetMetricsForEmSize(run.BaseEmSize);
            var (ascent, descent) = GetContentArea(layout.HasCentralDominantBaseline, metrics, run.Font.GetBaselines(metrics));
            var ascentLeading = Math.Floor(metrics.LineGap / 2);
            ascent += ascentLeading;
            descent += metrics.LineGap - ascentLeading;
            bounds = bounds is { } current ? (Math.Max(current.Ascent, ascent), Math.Max(current.Descent, descent)) : (ascent, descent);
        }

        return bounds;

        // (the invisible characters, e.g. a soft hyphen that the first available font does not have, use no font)
        static bool HasVisibleGlyph(ReadOnlySpan<Fonts.ShapedGlyph> glyphs)
        {
            foreach (var glyph in glyphs)
            {
                if (!glyph.IsInvisible)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Returns the half-leadings (https://drafts.csswg.org/css-inline-3/#half-leading) added above the ascent and below the
    /// descent of a box: half of the difference between its line height and its content area each. As in the browsers (Blink
    /// floors the half-leading of the ascent side to whole pixels, the descent side gets the rest), the baselines stay on whole
    /// pixels when the line height and the metrics are integers (the metrics of the fonts are rounded, see <see cref="Fonts.MatchedFont.GetMetrics"/>).
    /// </summary>
    private static (double Ascent, double Descent) GetHalfLeadings(in InlineMetrics metrics)
    {
        var leading = metrics.LineHeight - (metrics.Ascent + metrics.Descent);
        var ascent = Math.Floor(leading / 2);
        return (ascent, leading - ascent);
    }

    /// <summary>
    /// Whether the text of an item has another style than the box it is in: the text of a <c>display: contents</c> element
    /// (the text of a pseudo-element and of an anonymous box has the font and the line height of its box).
    /// </summary>
    private static bool IsTextOfContentsElement(in InlineItem item)
        => !ReferenceEquals(item.Style, item.Container.Style) && item.Container.PseudoElement is null && !ReferenceEquals(item.Style.Element, item.Container.Element);

    /// <summary>
    /// Aligns an inline box relative to its parent (https://drafts.csswg.org/css-inline-3/#transverse-alignment): its layout
    /// bounds are its content area (the ascent and the descent of its first available font) plus the half-leading of its line height.
    /// </summary>
    private void AlignInlineBox(ref LineNode node, ComputedStyle style, in InlineMetrics parentMetrics, bool quirks)
    {
        var metrics = GetMetrics(style);
        node.Metrics = metrics;
        var (ascentLeading, descentLeading) = GetHalfLeadings(metrics);
        node.Ascent = metrics.Ascent + ascentLeading;
        node.Descent = metrics.Descent + descentLeading;
        if (node.HasDirectText)
        {
            (node.Ascent, node.Descent) = AddEmphasisMarks(style, metrics, node.Ascent, node.Descent);
        }

        // https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk: an inline box without text "must, for the purpose of
        // line height calculation, act as if the box had a line-height of zero". As in the browsers, such a box does not
        // contribute to the height of the line at all, and the condition is on the borders and paddings in the inline axis (the
        // ones that make the line box not empty, https://drafts.csswg.org/css2/#inline-formatting): a box with block-axis
        // paddings and borders but no text does not contribute either
        node.Contributes = !quirks || node.HasDirectText || HasInlineEdges(style);
        var alignment = GetAlignmentBaseline(style, parentMetrics);
        node.Baseline = -GetBaselineShift(style, alignment, node.Ascent, node.Descent, metrics.GetBaseline(alignment), metrics.LineHeight, parentMetrics, out node.LineRelative);
    }

    /// <summary>
    /// Returns the alignment baseline of a box (https://drafts.csswg.org/css-inline-3/#alignment-baseline-property): "the
    /// baseline keyword uses the dominant baseline choice of the parent".
    /// </summary>
    private static AlignmentBaseline GetAlignmentBaseline(ComputedStyle style, in InlineMetrics parentMetrics)
    {
        var alignment = style.Layout.VerticalAlign.AlignmentBaseline;
        return alignment == AlignmentBaseline.Baseline ? parentMetrics.DominantBaseline : alignment;
    }

    /// <summary>
    /// Adds the emphasis marks of the text of a box to its layout bounds (https://drafts.csswg.org/css-text-decor-4/#emphasis-marks):
    /// as in the browsers, a mark (half the font size) drawn outside of the content area makes the line taller when the half-leading
    /// does not leave room for it.
    /// </summary>
    private static (double Ascent, double Descent) AddEmphasisMarks(ComputedStyle style, in InlineMetrics metrics, double ascent, double descent)
    {
        var side = style.Layout.Text.EmphasisMarks;
        if (side == EmphasisMarksSide.None)
            return (ascent, descent);

        var mark = (metrics.Ascent + metrics.Descent) / 2;
        return side == EmphasisMarksSide.Over ? (Math.Max(ascent, metrics.Ascent + mark), descent) : (ascent, Math.Max(descent, metrics.Descent + mark));
    }

    /// <summary>Returns whether an inline box has borders or paddings in the inline axis (the line height calculation quirk).</summary>
    private bool HasInlineEdges(ComputedStyle style)
    {
        if (!style.Layout.MayHaveBoxEdges)
            return false;

        var boxModel = style.Layout.Box;
        var border = _writingDirection.ToLogical(boxModel.BorderWidth);
        var padding = _writingDirection.ToLogical(FormattingContext.ResolvePadding(boxModel, _percentageBasis));
        return border.InlineStart + border.InlineEnd + padding.InlineStart + padding.InlineEnd != 0;
    }

    /// <summary>
    /// Aligns an atomic inline relative to its parent: its baseline (https://drafts.csswg.org/css2/#propdef-vertical-align) is
    /// the baseline of its last line box for an <c>inline-block</c> (the first one for the other atomic inlines, see
    /// <c>baseline-source</c>); the other baselines of its baseline set are generated from it with the first available font of
    /// the box (https://drafts.csswg.org/css-align-3/#generate-baselines). When it has none, is a replaced element (except the
    /// form controls with text, see <see cref="FormControlBaseline"/>) or a scroll container, its baselines are synthesized from
    /// its margin box (https://drafts.csswg.org/css-inline-3/#baseline-synthesis-box).
    /// </summary>
    private void AlignAtomic(ref LineNode node, in InlineItem item, in InlineMetrics parentMetrics)
    {
        var fragment = item.Fragment!;
        var box = (ContainerBox)item.Box;
        var style = box.Style;
        var layout = style.Layout;
        var margin = _writingDirection.ToLogical(fragment.Margin);
        var size = _writingDirection.ToLogical(fragment.Size);
        var marginBoxSize = margin.BlockStart + size.Block + margin.BlockEnd;
        var lineOver = _writingDirection.LineOver;
        var marginOver = fragment.Margin.Get(lineOver);
        double? baseline = null;
        // https://drafts.csswg.org/css2/#leading: "The baseline of an 'inline-block' is the baseline of its last line box in the
        // normal flow, unless it has either no in-flow line boxes or if its 'overflow' property has a computed value other than
        // 'visible', in which case the baseline is the bottom margin edge" (an inline-block only: a scrollable inline flex or
        // grid container is aligned by the baseline of its contents, clamped to its border box, https://drafts.csswg.org/css-align-3/#baseline-export:
        // WPT css/css-align/baseline-of-scrollable-1a.html);
        // https://drafts.csswg.org/css-inline-3/#baseline-source: only a block-axis scroll container gets this synthesized
        // baseline (WPT css/css-align/baseline-of-single-axis-scroll-container.html). An inline-table is aligned by the baseline
        // of its first row (https://drafts.csswg.org/css-tables-3/#table-wrapper-box). As in the browsers, a button that scrolls
        // keeps the baseline of its contents (https://html.spec.whatwg.org/multipage/rendering.html#button-layout: its
        // contents are in an anonymous button content box; WPT html/rendering/widgets/button-layout/scrollable-button-centering.html)
        var hasBaseline = box.Kind is ContainerBoxKind.Replaced or ContainerBoxKind.TableWrapper
            ? fragment.FirstBaseline is not null
            : !(box.Display.Inside == DisplayInside.FlowRoot && layout.VerticalAlign.BaselineSource == BaselineSource.Auto && IsBlockAxisScrollContainer(layout) && box.Element?.IsHtml(TagId.Button) != true);
        // The baselines of a box whose block axis is the block axis of the line (they are measured from its block-start edge, which
        // may be on the line-under side, e.g. a vertical-lr inline-block in vertical-rl text)
        var boxBlockStart = fragment.WritingDirection.BlockStart;
        if (hasBaseline && (boxBlockStart == lineOver || boxBlockStart == WritingDirection.Opposite(lineOver)))
        {
            var useLast = layout.VerticalAlign.BaselineSource switch
            {
                BaselineSource.First => false,
                BaselineSource.Last => true,
                // (a form control is aligned by the baseline of its first line, but a textarea, an inline-block scroll container that
                // has no last baseline: as in the browsers, it is aligned by the block-end edge of its margin box, whatever the
                // position of its lines, WPT html/rendering/replaced-elements/the-textarea-element/textarea-padding-bstart-moves-content-001.tentative.html)
                _ => (box.Display.Inside == DisplayInside.FlowRoot && box.Kind is not (ContainerBoxKind.TableWrapper or ContainerBoxKind.Replaced)) || TextControlLayout.IsMultiline(box),
            };
            baseline = useLast ? fragment.LastBaseline : fragment.FirstBaseline;
            if (baseline is { } fromBlockStart && boxBlockStart != lineOver)
            {
                baseline = size.Block - fromBlockStart;
            }
        }

        // As in the browsers, a button without baseline (it has no in-flow line box, or it has layout containment) gets one
        // synthesized from its content box instead of its margin box (WPT css/css-contain/contain-layout-button-001.tentative.html)
        if (baseline is null && boxBlockStart == lineOver && !_block.Style.Layout.HasCentralDominantBaseline && box is { PseudoElement: null, Element: { } buttonElement } && buttonElement.IsHtml(TagId.Button))
        {
            baseline = size.Block - fragment.Border.Get(WritingDirection.Opposite(lineOver)) - fragment.Padding.Get(WritingDirection.Opposite(lineOver));
        }

        // The distance of the alignment baseline from the line-over margin edge
        var alignment = GetAlignmentBaseline(style, parentMetrics);
        node.Metrics = GetMetrics(style);
        double ascent;
        if (baseline is { } value)
        {
            // https://drafts.csswg.org/css-align-3/#generate-baselines: "to generate baselines for a box from a single baseline,
            // use the baseline table from the font settings and first available font of that box, and align that baseline set to
            // the given single baseline" (the middle, text-top and text-bottom alignments use the edges of the box)
            ascent = marginOver + value;
            if (alignment is not (AlignmentBaseline.Middle or AlignmentBaseline.TextTop or AlignmentBaseline.TextBottom))
            {
                ascent -= node.Metrics.GetBaseline(alignment);
            }
        }
        else
        {
            // https://drafts.csswg.org/css-inline-3/#baseline-synthesis-box: a box without baseline in the inline axis of the
            // line gets them synthesized from its margin box: the alphabetic and ideographic-under baselines "are assumed to be at
            // its line-under margin edge", the central and math baselines "halfway between its line-under and line-over margin
            // edges", the hanging baseline "at its line-over margin edge"
            ascent = alignment switch
            {
                AlignmentBaseline.Central or AlignmentBaseline.Mathematical or AlignmentBaseline.Middle => marginBoxSize / 2,
                AlignmentBaseline.Hanging or AlignmentBaseline.TextTop => 0,
                _ => marginBoxSize,
            };
        }

        node.Ascent = ascent;
        node.Descent = marginBoxSize - ascent;
        node.Contributes = true;
        node.Baseline = -GetBaselineShift(style, alignment, node.Ascent, node.Descent, boxBaseline: 0, node.Metrics.LineHeight, parentMetrics, out node.LineRelative);
    }

    /// <summary>Returns whether a box is a scroll container in its block axis (its <c>overflow</c> in that axis is <c>hidden</c>, <c>scroll</c> or <c>auto</c>).</summary>
    private static bool IsBlockAxisScrollContainer(LayoutStyle layout)
        => layout.IsScrollContainer && ScrollContainers.IsScrollable(layout.IsHorizontalWritingMode ? layout.OverflowY : layout.OverflowX);

    /// <summary>
    /// Returns how much a box is raised above the baseline of its parent by <c>vertical-align</c>
    /// (https://drafts.csswg.org/css-inline-3/#propdef-vertical-align): its <c>alignment-baseline</c> and its
    /// <c>baseline-shift</c>. The values that align the box with the line box (<c>top</c>, <c>bottom</c>, <c>center</c>)
    /// are returned in <paramref name="lineRelative"/>. <paramref name="boxBaseline"/> is the distance of the alignment
    /// baseline of the box above its baseline.
    /// </summary>
    private static double GetBaselineShift(ComputedStyle style, AlignmentBaseline alignment, double ascent, double descent, double boxBaseline, double lineHeight, in InlineMetrics parent, out BaselineShiftKind lineRelative)
    {
        var verticalAlign = style.Layout.VerticalAlign;
        lineRelative = BaselineShiftKind.Length;
        var shift = alignment switch
        {
            // https://drafts.csswg.org/css2/#propdef-vertical-align: "Align the vertical midpoint of the box with the baseline of
            // the parent box plus half the x-height of the parent" (the x-middle baseline of the parent). As in the browsers, the
            // midpoint of the box is aligned, not its own x-middle baseline (https://drafts.csswg.org/css-inline-3/#valdef-dominant-baseline-middle)
            AlignmentBaseline.Middle => parent.GetBaseline(AlignmentBaseline.Middle) - ((ascent - descent) / 2),

            // "Align the top of the box with the top of the parent's content area" (and the bottoms for text-bottom): as in the
            // browsers, the edges of the layout bounds of the box, not the ones of its content area
            AlignmentBaseline.TextTop => parent.Ascent - ascent,
            AlignmentBaseline.TextBottom => descent - parent.Descent,

            // https://drafts.csswg.org/css-inline-3/#baseline-alignment: "each glyph or inline-level box is aligned in the block
            // axis by positioning its alignment baseline to match the corresponding baseline of its parent"
            _ => parent.GetBaseline(alignment) - boxBaseline,
        };

        var baselineShift = verticalAlign.BaselineShift;
        switch (baselineShift.Kind)
        {
            case BaselineShiftKind.Length:
                // The percentages refer to the line height of the box
                shift += baselineShift.Length.Resolve(lineHeight);
                break;

            // The offsets of the subscripts and the superscripts (the ones of the browsers: the font data is not used)
            case BaselineShiftKind.Sub:
                shift -= (parent.FontSize / 5) + 1;
                break;
            case BaselineShiftKind.Super:
                shift += (parent.FontSize / 3) + 1;
                break;
            default:
                lineRelative = baselineShift.Kind;
                break;
        }

        return shift;
    }

    /// <summary>Creates the fragments of the nodes (bottom-up) and the line box.</summary>
    private LineBoxFragment? CreateFragments(double inlineSize, double blockSize, double baseline, bool isFirstLine, bool isEmpty, double blockEndAnnotationOverflow)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        if (_nodeFragments.Length < nodes.Length)
        {
            _nodeFragments = new Fragment?[Math.Max(nodes.Length, _nodeFragments.Length * 2)];
        }

        var isRightToLeft = _writingDirection.IsRightToLeft;
        var lineLeft = LineLeftSide;
        var lineRight = Opposite(lineLeft);
        for (var i = nodes.Length - 1; i >= 0; i--)
        {
            ref var node = ref nodes[i];
            node.InlineStart = isRightToLeft ? inlineSize - node.X - node.Width : node.X;
            Fragment? fragment;
            if (node.Leaf < 0)
            {
                fragment = CreateBoxFragment(ref node, lineLeft, lineRight, isFirstLine, isEmpty);
            }
            else
            {
                var leaf = _leaves[node.Leaf];
                ref readonly var item = ref Items[leaf.Item];
                switch (leaf.Kind)
                {
                    case LineLeafKind.Atomic:
                        fragment = item.Fragment!;
                        node.BlockStart = node.Baseline - node.Ascent + item.Fragment!.Margin.Get(_writingDirection.LineOver);
                        node.BlockSize = _writingDirection.ToLogical(item.Fragment.Size).Block;
                        break;

                    case LineLeafKind.OutOfFlow:
                        node.BlockStart = 0;
                        node.BlockSize = 0;
                        _outOfFlowPositions.Add((leaf.Item, node.InlineStart, HasInFlowContentBefore(node.Leaf) || HasInlineBoxEdgeBefore(i), leaf.Level));
                        fragment = null;
                        break;

                    case LineLeafKind.RubyGap:
                        node.BlockStart = 0;
                        node.BlockSize = 0;
                        fragment = null;
                        break;

                    default:
                        fragment = CreateTextFragment(ref node, leaf, item, isFirstLine);
                        break;
                }
            }

            _nodeFragments[i] = fragment;
        }

        // The out-of-flow boxes are in content order (the nodes are in visual order: a right-to-left line reverses them), so an
        // anchor is laid out before the boxes after it that are positioned from it
        _outOfFlowPositions.Sort(static (a, b) => a.Item.CompareTo(b.Item));

        var lineSize = _writingDirection.ToPhysical(new LogicalSize(inlineSize, blockSize));
        var children = BuildChildren(_rootFirstChild, parentInline: 0, parentBlock: 0, lineSize);
        return children is null && isEmpty ? null : new LineBoxFragment(lineSize, baseline, children) { BlockEndAnnotationOverflow = blockEndAnnotationOverflow };
    }

    /// <summary>Returns whether a leaf of the line is preceded (in logical order) by a text or an atomic inline.</summary>
    private bool HasInFlowContentBefore(int leafIndex)
    {
        var leaves = CollectionsMarshal.AsSpan(_leaves);
        for (var i = 0; i < leafIndex; i++)
        {
            if (leaves[i].Kind == LineLeafKind.Atomic || (leaves[i].Kind == LineLeafKind.Text && leaves[i].End > leaves[i].Start))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns whether an inline box that starts on the line before a node has a margin, a border or a padding on its start edge:
    /// the line is then not a zero-height line box (https://drafts.csswg.org/css2/#inline-formatting: "line boxes that contain
    /// no text, no preserved white space, no inline elements with non-zero margins, padding, or borders, and no other in-flow
    /// content must be treated as zero-height").
    /// </summary>
    private bool HasInlineBoxEdgeBefore(int nodeIndex)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        for (var i = 0; i < nodeIndex; i++)
        {
            ref readonly var node = ref nodes[i];
            if (node.Leaf >= 0 || !node.HasStartEdge)
                continue;

            var leftIsStart = node.Box!.Style.Layout.Direction != Direction.Rtl;
            if (leftIsStart ? node.MarginLeft != 0 || node.BorderPaddingLeft != 0 : node.MarginRight != 0 || node.BorderPaddingRight != 0)
                return true;
        }

        return false;
    }

    private TextFragment CreateTextFragment(ref LineNode node, in LineLeaf leaf, in InlineItem item, bool isFirstLine)
    {
        // The style of the text on the first line (::first-line) or on a line scaled by text-fit, for the painting
        var lineStyle = (isFirstLine && _firstLineRoot is not null) || _fontScale != 1 ? GetLineStyle(item, isFirstLine) : null;
        if (ReferenceEquals(lineStyle, item.Box.Style))
        {
            lineStyle = null;
        }

        var metrics = node.Metrics;
        node.BlockStart = node.Baseline - metrics.Ascent;
        node.BlockSize = metrics.Ascent + metrics.Descent;
        var size = _writingDirection.ToPhysical(new LogicalSize(node.Width, node.BlockSize));

        // The baseline from the block-start edge of the fragment
        var baseline = _writingDirection.IsLineOverBlockEnd ? metrics.Descent : metrics.Ascent;
        var run = (TextRun)item.Box;
        switch (leaf.Kind)
        {
            case LineLeafKind.LineBreak:
                return new TextFragment(run, item.DomStart, item.DomEnd, size, baseline, shapedText: null, glyphStart: 0, glyphCount: 0) { LineStyle = lineStyle };

            case LineLeafKind.Hyphen:
                // After the soft hyphen
                var offset = leaf.Start < item.End ? _offsets[leaf.Start] : item.DomEnd;
                return new TextFragment(run, offset, offset, size, baseline, leaf.Shaped, 0, leaf.GlyphCount) { IsHyphen = true, LineStyle = lineStyle };

            default:
                var domStart = leaf.Start == item.Start ? item.DomStart : _offsets[leaf.Start];
                var domEnd = leaf.End == item.End ? item.DomEnd : _offsets[leaf.End];
                return new TextFragment(run, domStart, Math.Max(domStart, domEnd), size, baseline, leaf.Shaped, leaf.GlyphStart, leaf.GlyphCount)
                {
                    DomOffsets = GetDomOffsets(leaf.Start, leaf.End, domStart),
                    LineStyle = lineStyle,
                };
        }
    }

    /// <summary>
    /// Returns the offsets in the text node of the characters of a text leaf (see <see cref="TextFragment.DomOffsets"/>), or null
    /// when they are consecutive from <paramref name="domStart"/> (no collapsed white space nor text transformation that changes
    /// the length inside the leaf).
    /// </summary>
    private int[]? GetDomOffsets(int start, int end, int domStart)
    {
        var offsets = _offsets.AsSpan(start, end - start);
        for (var i = 0; i < offsets.Length; i++)
        {
            if (offsets[i] != domStart + i)
                return offsets.ToArray();
        }

        return null;
    }

    /// <summary>
    /// Creates the fragment of an inline box on the line: its content area is given by its first available font, plus its
    /// paddings and borders in the block axis (https://drafts.csswg.org/css-inline-3/#inline-box-dimensions); its margins,
    /// borders and paddings in the inline axis are the ones of the edges on the line. On a phantom line
    /// (https://drafts.csswg.org/css-inline-3/#invisible-line-boxes: a line without content nor edges, e.g. an empty inline box,
    /// or the parts of an inline box around a block), the fragment has no block size and is at the top of the line, as in the
    /// browsers.
    /// </summary>
    private BoxFragment CreateBoxFragment(ref LineNode node, PhysicalSide lineLeft, PhysicalSide lineRight, bool isFirstLine, bool isPhantomLine)
    {
        var box = node.Box!;
        var style = GetLineStyle(box, isFirstLine);
        var hasEdges = style.Layout.MayHaveBoxEdges;
        var border = hasEdges ? style.Layout.Box.BorderWidth : default;
        var padding = hasEdges ? FormattingContext.ResolvePadding(style.Layout.Box, _percentageBasis) : default;
        var leftBorder = node.HasLeftEdge ? border.Get(lineLeft) : 0;
        var rightBorder = node.HasRightEdge ? border.Get(lineRight) : 0;
        var appliedBorder = SetSide(SetSide(border, lineLeft, leftBorder), lineRight, rightBorder);
        var appliedPadding = SetSide(SetSide(padding, lineLeft, node.HasLeftEdge ? padding.Get(lineLeft) : 0), lineRight, node.HasRightEdge ? padding.Get(lineRight) : 0);
        var appliedMargin = SetSide(SetSide(default, lineLeft, node.MarginLeft), lineRight, node.MarginRight);
        var blockBorder = _writingDirection.ToLogical(border);
        var blockPadding = _writingDirection.ToLogical(padding);
        var metrics = node.Metrics;
        var startEdge = blockBorder.BlockStart + blockPadding.BlockStart;
        var endEdge = blockBorder.BlockEnd + blockPadding.BlockEnd;
        var flipped = _writingDirection.IsLineOverBlockEnd;
        var overEdge = flipped ? endEdge : startEdge;

        // The content area: the ascent and the descent of the first available font; https://drafts.csswg.org/css-inline-3/#text-box-trim:
        // "On inline boxes, specifies whether to trim the content box to match the specified text-box-edge metric" (the block-start
        // side is the line-over side, except in vertical-lr). The layout bounds of the box do not change
        var over = metrics.Ascent;
        var under = metrics.Descent;
        var textBoxTrim = style.Layout.Text.TextBoxTrim;
        if (textBoxTrim != TextBoxTrim.None)
        {
            var textEdge = style.Layout.Text.TextBoxEdge;
            if ((textBoxTrim & (flipped ? TextBoxTrim.TrimEnd : TextBoxTrim.TrimStart)) != TextBoxTrim.None)
            {
                over = metrics.GetOverEdge(textEdge.Over);
            }

            if ((textBoxTrim & (flipped ? TextBoxTrim.TrimStart : TextBoxTrim.TrimEnd)) != TextBoxTrim.None)
            {
                under = metrics.GetUnderEdge(textEdge.Under);
            }
        }

        var blockSize = over + under + startEdge + endEdge;
        node.BlockStart = node.Baseline - over - overEdge;
        if (box.Display.Internal == DisplayInternal.RubyTextContainer && GetChildrenBlockExtent(node) is { } extent)
        {
            // An annotation container spans its annotations (they are placed over or under their bases)
            node.BlockStart = extent.Start;
            blockSize = extent.Size;
        }

        if (isPhantomLine)
        {
            blockSize = 0;
            node.BlockStart = 0;
        }

        node.BlockSize = blockSize;
        var size = _writingDirection.ToPhysical(new LogicalSize(node.Width, blockSize));
        var children = BuildChildren(node.FirstChild, node.InlineStart, node.BlockStart, size);
        var baseline = flipped ? under + startEdge : over + startEdge;
        var fragment = new BoxFragment(box, size, appliedBorder, appliedPadding, appliedMargin, children)
        {
            FirstBaseline = baseline,
            LastBaseline = baseline,
            IsFirstFragment = node.HasStartEdge,
            IsLastFragment = node.HasEndEdge,
        };

        // https://drafts.csswg.org/css-position-3/#relpos-insets: the containing block of an inline box is the content box of the block container
        PositionedLayout.SetRelativeOffset(box, fragment, _childSpace);
        return fragment;

        static Sides<double> SetSide(Sides<double> sides, PhysicalSide side, double value) => side switch
        {
            PhysicalSide.Top => sides with { Top = value },
            PhysicalSide.Right => sides with { Right = value },
            PhysicalSide.Bottom => sides with { Bottom = value },
            _ => sides with { Left = value },
        };
    }

    /// <summary>
    /// Returns the fragments of the children of a node (or of the line box), positioned relative to it. The block positions of
    /// the nodes are measured from the line-over edge: in <c>vertical-lr</c>, the line-over edge is the block-end edge, so the
    /// block-start offset of a child is the distance between the line-under edges.
    /// </summary>
    private Fragment[]? BuildChildren(int firstChild, double parentInline, double parentBlock, PhysicalSize parentSize)
    {
        if (firstChild < 0)
            return null;

        var builder = _childrenBuilder;
        builder.Reset(_writingDirection);
        var flipped = _writingDirection.IsLineOverBlockEnd;
        var parentBlockEnd = flipped ? parentBlock + _writingDirection.ToLogical(parentSize).Block : 0;
        for (var child = firstChild; child >= 0; child = _nodes[child].NextSibling)
        {
            if (_nodeFragments[child] is { } fragment)
            {
                var node = _nodes[child];
                var blockOffset = flipped ? parentBlockEnd - (node.BlockStart + node.BlockSize) : node.BlockStart - parentBlock;
                builder.AddChild(fragment, new LogicalOffset(node.InlineStart - parentInline, blockOffset));
            }
        }

        return builder.Build(parentSize);
    }
}
