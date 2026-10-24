using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The ruby layout (https://drafts.csswg.org/css-ruby-1/#ruby-layout), as in Blink: the ruby containers are inline boxes of the
// inline formatting context, whose bases and annotations are paired in ruby columns.
//
// - Collection (CollectRuby): the children of a ruby container are split in ruby segments (a base container, or a sequence of
//   bases, followed by its annotation levels: the annotation containers, or the sequences of annotations), and the annotations
//   of each level are paired with the bases of the segment (https://drafts.csswg.org/css-ruby-1/#pairing): in order, the
//   excess annotations with anonymous empty bases, and the single anonymous annotation of an annotation container spans all the
//   bases; the intra-level white space is paired with the white space of the other levels (PairRubySegment), and the hidden
//   annotations (https://drafts.csswg.org/css-ruby-1/#hiding) are laid out as empty annotations. Each base, with the annotations paired with it, is a ruby column (the bases spanned by an annotation are one
//   column): a RubyColumn item followed by the items of its bases, then by the items of its annotations (flagged
//   RubyAnnotation). The text of the annotations is in the text of the formatting context, but the line breaking ignores it:
//   the break opportunities are computed on the text without the annotations, so that the opportunities around the ruby are
//   the ones of its bases and of the text around it.
// - Line breaking (BreakLine): a ruby column is a unit (no break inside it, https://drafts.csswg.org/css-ruby-1/#break-between:
//   "ruby may be broken wherever all levels simultaneously allow a break"), whose inline size is the largest of its bases and of
//   its annotations (ComputeRubyColumnLayout: the slot of each base is as wide as its content and its annotations).
// - Line box (BuildLine): the bases are laid out on the line like the other inline content; the space of their slot that their
//   content does not fill is distributed by ruby-align (AlignRubyColumn: at the edges of the base box, or to the justification
//   opportunities of the text); the annotations are moved over the slots of their bases (PlaceLeaves jumps back to the start of
//   the slots, see OpenNode/CloseNode) and aligned the same way in them. In the block axis, the annotation boxes do not
//   contribute to the height of the line (https://drafts.csswg.org/css-ruby-1/#line-height): each one is the root of an aligned
//   subtree placed over (under) the content areas of its bases, the levels stacked outward (PlaceRubyAnnotations); then the line box
//   is extended by the annotations that overflow it, unless the half-leading of the previous (next) line leaves room for them
//   (ApplyRubyAnnotationOverflow, as Blink does).
internal sealed partial class InlineLayoutAlgorithm
{
    /// <summary>The ruby columns of the formatting context (an item references its column by its index + 1, see <see cref="InlineItem.RubyColumn"/>).</summary>
    private readonly List<RubyColumnInfo> _rubyColumns = [];

    /// <summary>The bases of the ruby columns (the bases of a column are consecutive).</summary>
    private readonly List<RubyBaseInfo> _rubyBases = [];

    /// <summary>The annotations of the ruby columns (the annotations of a column are consecutive, by level).</summary>
    private readonly List<RubyAnnotationInfo> _rubyAnnotations = [];

    /// <summary>The ruby bases (index) and annotations (bitwise complement of the index) by box.</summary>
    private readonly Dictionary<ContainerBox, int> _rubyBoxes = new(ReferenceEqualityComparer.Instance);

    /// <summary>The children of the ruby segments being collected: the base row, then the annotations of the levels.</summary>
    private readonly List<LayoutBox> _rubyScratchBoxes = [];
    private readonly List<RubyLevel> _rubyScratchLevels = [];

    /// <summary>The pairs of the ruby segments being collected (see <see cref="PairRubySegment"/>).</summary>
    private readonly List<int> _rubyScratchSlots = [];

    /// <summary>The number of ruby containers whose items are being collected.</summary>
    private int _rubyDepth;

    /// <summary>The ruby column whose items are being collected, or -1.</summary>
    private int _currentRubyColumn = -1;

    /// <summary>The number of ruby annotations whose items are being collected (the items are flagged <see cref="InlineItemFlags.RubyAnnotation"/>).</summary>
    private int _rubyAnnotationDepth;

    /// <summary>Whether an item is in a ruby annotation: the break opportunities are computed without the text of the annotations.</summary>
    private bool _hasRubyAnnotations;

    // The layout of the ruby columns, indexed by base or annotation (see ComputeRubyColumnLayout, AlignRubyColumn)
    private double[] _rubyContentWidths = [];
    private double[] _rubySlotWidths = [];
    private double[] _rubyGapWidths = [];
    private double[] _rubyAnnotationWidths = [];
    private double[] _rubyBaseExtraStart = [];
    private double[] _rubyBaseExtraEnd = [];
    private double[] _rubyAnnotationExtraStart = [];
    private double[] _rubyAnnotationExtraEnd = [];

    // The overhang of the ruby columns of the line being built over the content before and after them, indexed by column
    private double[] _rubyOverhangStart = [];
    private double[] _rubyOverhangEnd = [];

    // The line-left and line-right edges of the slots of the bases on the line being built (see PlaceLeaves)
    private double[] _rubyBaseStartX = [];
    private double[] _rubyBaseEndX = [];

    /// <summary>The positions of the base row to restore after the annotations placed over their bases (see OpenNode/CloseNode).</summary>
    private readonly List<double> _rubySavedX = [];

    private readonly List<int> _rubyPositions = [];
    private readonly List<int> _rubyRootNodes = [];
    private readonly List<int> _rubyPendingLeaves = [];
    private int[] _rubyRemainingLeaves = [];
    private int[] _rubyRegroupedOrder = [];
    private int[] _itemRemap = [];

    // The text without the annotations, for the break opportunities
    private char[] _rubyFilteredText = [];
    private int[] _rubyFilteredPositions = [];
    private LineBreakOpportunity[] _rubyFilteredOpportunities = [];
    private readonly List<LineBreakOptionsRun> _rubyFilteredRuns = [];

    /// <summary>
    /// The block-end side of the previous line (https://drafts.csswg.org/css-ruby-1/#line-height, as in Blink): the space between its
    /// content and its block-end edge that an annotation of the next line may use (positive), or the amount its annotations overflow
    /// it (negative: the next line is placed after them, and may move back into the space before its own content).
    /// </summary>
    private double _rubyBlockEndSpace;

    /// <summary>
    /// The block offset (from the block-start edge of the content box) of the block-end edge of the annotations that overflow the
    /// previous line, or negative infinity: the next line, or the end of the content, is after them (see ApplyRubyOverflow).
    /// </summary>
    private double _rubyOverflowEnd = double.NegativeInfinity;

    /// <summary>
    /// The ruby column whose bases end the line being built and lost their collapsible spaces at the end of the line (see
    /// TrimTrailingSpaces), or -1, and the text position from which the text of its bases is removed.
    /// </summary>
    private int _rubyTrimmedColumn = -1;
    private int _rubyTrimmedPosition;

    /// <summary>The range of the items of the line being built (the ruby columns on the line).</summary>
    private int _lineStartItem;
    private int _lineEndItem;

    /// <summary>Whether the formatting context contains ruby columns.</summary>
    private bool HasRuby => _rubyColumns.Count > 0;

    /// <summary>A ruby column (https://drafts.csswg.org/css-ruby-1/#ruby-columns): the unit of the line breaking.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RubyColumnInfo
    {
        /// <summary>The ruby container.</summary>
        public ContainerBox Ruby;

        /// <summary>The ruby column that contains this column (a ruby in a base), or -1.</summary>
        public int Parent;

        /// <summary>The <see cref="InlineItemKind.RubyColumn"/> item, the first item of the annotations (the end of the bases), the end of the column.</summary>
        public int MarkerItem;
        public int AnnotationStartItem;
        public int EndItem;

        public int FirstBase;
        public int BaseCount;
        public int FirstAnnotation;
        public int AnnotationCount;
    }

    /// <summary>A ruby base of a column: its box (null for an anonymous empty base) and its items.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RubyBaseInfo
    {
        public ContainerBox? Box;
        public int StartItem;
        public int EndItem;
    }

    /// <summary>A ruby annotation of a column: the range of the bases it is paired with (they are in its column), its level and its side.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct RubyAnnotationInfo
    {
        public ContainerBox Box;
        public int Column;
        public int FirstBase;
        public int LastBase;

        /// <summary>The level of the annotation in its ruby segment (0 for the first annotation container).</summary>
        public int Level;

        /// <summary>Whether the annotation is over the bases (else under), from <c>ruby-position</c>.</summary>
        public bool IsOver;

        /// <summary>Whether the annotation is hidden (https://drafts.csswg.org/css-ruby-1/#hiding): its content is not laid out.</summary>
        public bool IsHidden;
        public int StartItem;
        public int EndItem;
    }

    /// <summary>An annotation level of the segment being collected: its annotation container (null for an anonymous one) and its annotations in <see cref="_rubyScratchBoxes"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct RubyLevel(ContainerBox? Container, int First, int Count)
    {
        public bool IsOver { get; init; }

        /// <summary>Whether the level is a single annotation spanning all the bases of the segment.</summary>
        public bool IsSpanning { get; init; }
    }

    /// <summary>Clears the ruby state of the formatting context.</summary>
    private void ClearRuby()
    {
        _rubyColumns.Clear();
        _rubyBases.Clear();
        _rubyAnnotations.Clear();
        _rubyBoxes.Clear();
        _rubyScratchBoxes.Clear();
        _rubyScratchLevels.Clear();
        _rubyScratchSlots.Clear();
        _rubySavedX.Clear();
        _rubyPositions.Clear();
        _rubyRootNodes.Clear();
        _rubyPendingLeaves.Clear();
        _rubyFilteredRuns.Clear();
        _currentRubyColumn = -1;
        _rubyAnnotationDepth = 0;
        _rubyDepth = 0;
        _hasRubyAnnotations = false;
        _rubyBlockEndSpace = 0;
        _rubyOverflowEnd = double.NegativeInfinity;
    }

    /// <summary>
    /// Moves the position of the next line (or the end of the content) after the annotations that overflow the block-end side of the
    /// previous line. After the last line they may overflow into the block-end padding of the block container
    /// (<paramref name="blockEndPadding"/>, as in Blink: WPT css/css-ruby/br-clear-all-001.html), not into its border (as in Gecko:
    /// WPT css/css-text-decor/text-emphasis-line-height-002a.html, where Blink lets them overflow into the border too).
    /// </summary>
    private void ApplyRubyOverflow(ref LineLayoutState lines, double blockEndPadding)
    {
        var end = _rubyOverflowEnd - blockEndPadding;
        _rubyOverflowEnd = double.NegativeInfinity;
        if (end <= lines.BlockOffset)
            return;

        _blockFormattingContext!.AdvanceBlockPosition(end - lines.BlockOffset);
        lines.BlockOffset = end;
    }

    /// <summary>The block-end padding of the block container.</summary>
    private double GetBlockEndPadding()
    {
        var layout = _block.Style.Layout;
        if (!layout.MayHaveBoxEdges)
            return 0;

        return _writingDirection.ToLogical(FormattingContext.ResolvePadding(layout.Box, _percentageBasis)).BlockEnd;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Collection
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Collects the items of a ruby container: its segments are paired in ruby columns
    /// (https://drafts.csswg.org/css-ruby-1/#pairing); the white space between the segments (and between the bases of different
    /// columns) is laid out between the columns.
    /// </summary>
    private void CollectRuby(ContainerBox ruby)
    {
        _rubyDepth++;
        OpenInlineBox(ruby);
        for (var child = ruby.FirstChild; child is not null;)
        {
            if (child is ContainerBox { Display.IsInternalRubyBox: true })
            {
                child = CollectRubySegment(ruby, child);
                continue;
            }

            CollectChild(ruby, child);
            child = child.NextSibling;
        }

        CloseInlineBox(ruby);
        _rubyDepth--;
    }

    /// <summary>
    /// Collects the ruby segment that starts at <paramref name="first"/> (https://drafts.csswg.org/css-ruby-1/#ruby-segments) and
    /// returns the child after it.
    /// </summary>
    private LayoutBox? CollectRubySegment(ContainerBox ruby, LayoutBox first)
    {
        var rowStart = _rubyScratchBoxes.Count;
        var levelStart = _rubyScratchLevels.Count;
        var child = first;

        // The base row: a base container (its bases and inter-base white space), or the bases and the white space between them
        ContainerBox? baseContainer = null;
        if (child is ContainerBox { Display.Internal: DisplayInternal.RubyBaseContainer } container)
        {
            baseContainer = container;
            foreach (var baseChild in container.Children)
            {
                _rubyScratchBoxes.Add(baseChild);
            }

            child = child.NextSibling;
        }
        else
        {
            while (child is not null && (IsRubyBase(child) || (child is TextRun && IsRubyBase(SkipWhiteSpaceRuns(child)))))
            {
                _rubyScratchBoxes.Add(child);
                child = child.NextSibling;
            }
        }

        var rowEnd = _rubyScratchBoxes.Count;

        // The annotation levels: each annotation container, or each sequence of annotations (an anonymous annotation container).
        // The white space between the annotations is not rendered
        var anonymousLevelOpen = false;
        while (child is not null)
        {
            if (child is ContainerBox { Display.Internal: DisplayInternal.RubyTextContainer } annotationContainer)
            {
                var levelFirst = _rubyScratchBoxes.Count;
                foreach (var annotation in annotationContainer.Children)
                {
                    if (annotation is ContainerBox { Display.Internal: DisplayInternal.RubyText })
                    {
                        _rubyScratchBoxes.Add(annotation);
                    }
                }

                _rubyScratchLevels.Add(new RubyLevel(annotationContainer, levelFirst, _rubyScratchBoxes.Count - levelFirst));
                anonymousLevelOpen = false;
            }
            else if (child is ContainerBox { Display.Internal: DisplayInternal.RubyText })
            {
                if (!anonymousLevelOpen)
                {
                    _rubyScratchLevels.Add(new RubyLevel(null, _rubyScratchBoxes.Count, 0));
                    anonymousLevelOpen = true;
                }

                _rubyScratchBoxes.Add(child);
                _rubyScratchLevels[^1] = _rubyScratchLevels[^1] with { Count = _rubyScratchLevels[^1].Count + 1 };
            }
            else if (!(child is TextRun && IsRubyAnnotation(SkipWhiteSpaceRuns(child))))
            {
                break;
            }

            child = child.NextSibling;
        }

        EmitRubySegment(ruby, baseContainer, rowStart, rowEnd, levelStart);
        _rubyScratchBoxes.RemoveRange(rowStart, _rubyScratchBoxes.Count - rowStart);
        _rubyScratchLevels.RemoveRange(levelStart, _rubyScratchLevels.Count - levelStart);
        return child;

        static bool IsRubyBase(LayoutBox? box) => box is ContainerBox { Display.Internal: DisplayInternal.RubyBase };

        static bool IsRubyAnnotation(LayoutBox? box) => box is ContainerBox { Display.Internal: DisplayInternal.RubyText or DisplayInternal.RubyTextContainer };

        static LayoutBox? SkipWhiteSpaceRuns(LayoutBox box)
        {
            var current = box;
            while (current is TextRun)
            {
                current = current.NextSibling;
            }

            return current;
        }
    }

    /// <summary>
    /// Pairs the bases and the annotations of a segment in ruby columns (https://drafts.csswg.org/css-ruby-1/#pairing) and
    /// collects their items.
    /// </summary>
    private void EmitRubySegment(ContainerBox ruby, ContainerBox? baseContainer, int rowStart, int rowEnd, int levelStart)
    {
        var baseCount = 0;
        for (var i = rowStart; i < rowEnd; i++)
        {
            if (_rubyScratchBoxes[i] is ContainerBox)
            {
                baseCount++;
            }
        }

        // https://drafts.csswg.org/css-ruby-1/#rubypos: the side of each level ("alternate" alternates from the previous level).
        // The position of an anonymous level is the one of its ruby container (the one of the annotations does not apply).
        // "If a ruby annotation container contains only a single, anonymous ruby annotation, then that ruby annotation is paired
        // with (i.e. spans across) all of the ruby bases in its ruby segment"
        var hasSpanning = false;
        bool? previousIsOver = null;
        for (var l = levelStart; l < _rubyScratchLevels.Count; l++)
        {
            var level = _rubyScratchLevels[l];
            var position = (level.Container ?? ruby).Style.Layout.Text.RubyPosition;
            var isOver = position switch
            {
                RubyPosition.Under => false,
                RubyPosition.AlternateOver => previousIsOver is not { } previous || !previous,
                RubyPosition.AlternateUnder => previousIsOver is { } previous && !previous,
                _ => true,
            };

            previousIsOver = isOver;
            var isSpanning = level.Container is not null && level.Count == 1 && _rubyScratchBoxes[level.First] is ContainerBox { IsAnonymous: true } && baseCount > 1;
            _rubyScratchLevels[l] = level with { IsOver = isOver, IsSpanning = isSpanning };
            hasSpanning |= isSpanning;
        }

        // The pairs (https://drafts.csswg.org/css-ruby-1/#pairing): the other annotations are paired with the bases in order, the
        // excess ones with anonymous empty bases. As in Gecko, the intra-level white space (inter-base and inter-annotation white
        // space) is paired with the white space at the same position of the other levels, and with nothing in the levels that have
        // no white space there; the inter-base white space paired with no annotation stays between the columns
        var slotStart = _rubyScratchSlots.Count;
        var levelCount = _rubyScratchLevels.Count - levelStart;
        PairRubySegment(rowStart, rowEnd, levelStart);
        var totalBases = (_rubyScratchSlots.Count - slotStart) / (levelCount + 1);

        // The columns: the bases spanned by an annotation are one column, the other bases have a column each. Their bases and
        // annotations are reserved now, so that the bases (annotations) of a column are consecutive even when the bases contain
        // other ruby containers
        var firstBase = _rubyBases.Count;
        for (var j = 0; j < totalBases; j++)
        {
            var baseRow = _rubyScratchSlots[slotStart + (j * (levelCount + 1))];
            var box = baseRow >= 0 ? (ContainerBox)_rubyScratchBoxes[baseRow] : null;
            if (box is not null)
            {
                _rubyBoxes[box] = _rubyBases.Count;
            }

            _rubyBases.Add(new RubyBaseInfo { Box = box });
        }

        var oneColumn = hasSpanning && totalBases > 1;
        var columnCount = oneColumn ? 1 : totalBases;
        var firstColumn = _rubyColumns.Count;
        for (var c = 0; c < columnCount; c++)
        {
            var column = new RubyColumnInfo
            {
                Ruby = ruby,
                Parent = _currentRubyColumn,
                FirstBase = firstBase + c,
                BaseCount = oneColumn ? totalBases : 1,
                FirstAnnotation = _rubyAnnotations.Count,
            };

            for (var l = levelStart; l < _rubyScratchLevels.Count; l++)
            {
                var level = _rubyScratchLevels[l];
                if (level.IsSpanning)
                {
                    AddRubyAnnotation((ContainerBox)_rubyScratchBoxes[level.First], firstColumn + c, firstBase, firstBase + totalBases - 1, l - levelStart, level.IsOver);
                    continue;
                }

                for (var j = column.FirstBase - firstBase; j < column.FirstBase - firstBase + column.BaseCount; j++)
                {
                    var annotationRow = _rubyScratchSlots[slotStart + (j * (levelCount + 1)) + 1 + (l - levelStart)];
                    if (annotationRow >= 0)
                    {
                        AddRubyAnnotation((ContainerBox)_rubyScratchBoxes[annotationRow], firstColumn + c, firstBase + j, firstBase + j, l - levelStart, level.IsOver);
                    }
                }
            }

            column.AnnotationCount = _rubyAnnotations.Count - column.FirstAnnotation;
            _rubyColumns.Add(column);
        }

        _rubyScratchSlots.RemoveRange(slotStart, _rubyScratchSlots.Count - slotStart);

        // The items: the white space between the columns, then for each column its marker, its bases (with the white space between
        // them) and its annotations
        var row = rowStart;
        var parent = baseContainer ?? ruby;
        for (var c = 0; c < columnCount; c++)
        {
            var columnIndex = firstColumn + c;
            var column = _rubyColumns[columnIndex];
            for (var b = column.FirstBase; b < column.FirstBase + column.BaseCount; b++)
            {
                // The white space before the base: between the columns, or in the column when it has several bases
                var baseInfo = _rubyBases[b];
                if (baseInfo.Box is { } box)
                {
                    row = CollectRubyRowWhiteSpace(parent, row, _rubyScratchBoxes.IndexOf(box, row, rowEnd - row));
                }

                if (b == column.FirstBase)
                {
                    StartRubyColumn(columnIndex);
                }

                baseInfo.StartItem = _items.Count;
                if (baseInfo.Box is { } baseBox)
                {
                    row++;
                    OpenInlineBox(baseBox);
                    CollectChildren(baseBox);
                    CloseInlineBox(baseBox);
                }

                baseInfo.EndItem = _items.Count;
                _rubyBases[b] = baseInfo;
            }

            column = _rubyColumns[columnIndex];
            column.AnnotationStartItem = _items.Count;
            for (var a = column.FirstAnnotation; a < column.FirstAnnotation + column.AnnotationCount; a++)
            {
                CollectRubyAnnotation(a);
            }

            column.EndItem = _items.Count;
            _rubyColumns[columnIndex] = column;
            _currentRubyColumn = column.Parent;
        }

        // The white space at the end of the base row (not removed by the fixup in a base container)
        CollectRubyRowWhiteSpace(parent, row, rowEnd);
    }

    /// <summary>
    /// Pairs the bases of the base row (<see cref="_rubyScratchBoxes"/> from <paramref name="rowStart"/> to <paramref name="rowEnd"/>)
    /// with the annotations of the levels that do not span the segment, as Gecko enumerates its ruby columns: each pair is added to
    /// <see cref="_rubyScratchSlots"/> as the index of its base (-1 for an anonymous empty base) followed by the index of its
    /// annotation in each level (-1 for none). When the next box of a level is intra-level white space, the pair is made of the
    /// white space of the levels at that position only, and only these levels move to their next box; a pair of white space
    /// without annotation is not added (the inter-base white space is laid out between the columns).
    /// </summary>
    private void PairRubySegment(int rowStart, int rowEnd, int levelStart)
    {
        var levelCount = _rubyScratchLevels.Count - levelStart;
        Span<int> cursors = levelCount <= 8 ? stackalloc int[levelCount] : new int[levelCount];
        for (var l = 0; l < levelCount; l++)
        {
            var level = _rubyScratchLevels[levelStart + l];
            cursors[l] = level.IsSpanning ? level.Count : 0;
        }

        var rowCursor = NextBase(rowStart);
        var atWhiteSpace = IsWhiteSpaceBase(rowCursor);
        for (var l = 0; l < levelCount; l++)
        {
            atWhiteSpace |= IsWhiteSpaceAnnotation(l, cursors[l]);
        }

        var slotStart = _rubyScratchSlots.Count;
        while (true)
        {
            var hasMore = rowCursor < rowEnd;
            for (var l = 0; l < levelCount && !hasMore; l++)
            {
                hasMore = cursors[l] < _rubyScratchLevels[levelStart + l].Count;
            }

            if (!hasMore)
                break;

            var slot = _rubyScratchSlots.Count;
            var takesBase = rowCursor < rowEnd && (!atWhiteSpace || IsWhiteSpaceBase(rowCursor));
            _rubyScratchSlots.Add(takesBase ? rowCursor : -1);
            var hasAnnotation = false;
            for (var l = 0; l < levelCount; l++)
            {
                var level = _rubyScratchLevels[levelStart + l];
                var takesAnnotation = cursors[l] < level.Count && (!atWhiteSpace || IsWhiteSpaceAnnotation(l, cursors[l]));
                _rubyScratchSlots.Add(takesAnnotation ? level.First + cursors[l] : -1);
                hasAnnotation |= takesAnnotation;
            }

            if (atWhiteSpace && !hasAnnotation)
            {
                _rubyScratchSlots.RemoveRange(slot, _rubyScratchSlots.Count - slot);
            }

            // The next pair: after white space, only the levels that had white space move
            var nextAtWhiteSpace = false;
            if (takesBase)
            {
                rowCursor = NextBase(rowCursor + 1);
                nextAtWhiteSpace |= IsWhiteSpaceBase(rowCursor);
            }

            for (var l = 0; l < levelCount; l++)
            {
                if (cursors[l] < _rubyScratchLevels[levelStart + l].Count && (!atWhiteSpace || IsWhiteSpaceAnnotation(l, cursors[l])))
                {
                    cursors[l]++;
                    nextAtWhiteSpace |= IsWhiteSpaceAnnotation(l, cursors[l]);
                }
            }

            atWhiteSpace = nextAtWhiteSpace;
        }

        if (_rubyScratchSlots.Count == slotStart)
        {
            // A segment without base nor annotation: an anonymous empty base
            for (var l = 0; l <= levelCount; l++)
            {
                _rubyScratchSlots.Add(-1);
            }
        }

        int NextBase(int row)
        {
            while (row < rowEnd && _rubyScratchBoxes[row] is not ContainerBox)
            {
                row++;
            }

            return row;
        }

        bool IsWhiteSpaceBase(int row) => row < rowEnd && _rubyScratchBoxes[row] is ContainerBox { IsRubyWhiteSpace: true };

        bool IsWhiteSpaceAnnotation(int l, int cursor)
        {
            var level = _rubyScratchLevels[levelStart + l];
            return cursor < level.Count && _rubyScratchBoxes[level.First + cursor] is ContainerBox { IsRubyWhiteSpace: true };
        }
    }

    /// <summary>
    /// Adds an annotation paired with a range of bases. As in Gecko, an annotation paired with a single base is hidden
    /// (https://drafts.csswg.org/css-ruby-1/#autohide): when its <c>visibility</c> is <c>collapse</c>, or when its text content is
    /// the text content of its base ("prior to white space collapsing and text transformation", the elements ignored).
    /// </summary>
    private void AddRubyAnnotation(ContainerBox annotation, int column, int firstBase, int lastBase, int level, bool isOver)
    {
        var isHidden = false;
        if (firstBase == lastBase && !annotation.IsRubyWhiteSpace)
        {
            isHidden = annotation.Style.Layout.Visibility == Visibility.Collapse
                || (_rubyBases[firstBase].Box is { } baseBox && HasSameRubyTextContent(baseBox, annotation));
        }

        _rubyBoxes[annotation] = ~_rubyAnnotations.Count;
        _rubyAnnotations.Add(new RubyAnnotationInfo
        {
            Box = annotation,
            Column = column,
            FirstBase = firstBase,
            LastBase = lastBase,
            Level = level,
            IsOver = isOver,
            IsHidden = isHidden,
        });
    }

    /// <summary>Whether the text content of two boxes (the texts of the DOM text nodes in them) is the same.</summary>
    private static bool HasSameRubyTextContent(ContainerBox first, ContainerBox second)
    {
        var builder = new System.Text.StringBuilder();
        AppendText(builder, first);
        var text = builder.ToString();
        builder.Clear();
        AppendText(builder, second);
        return builder.Length > 0 && text.AsSpan().SequenceEqual(builder.ToString());

        static void AppendText(System.Text.StringBuilder builder, ContainerBox box)
        {
            foreach (var child in box.Children)
            {
                if (child is TextRun { Kind: TextRunKind.Text } run)
                {
                    builder.Append(run.Text);
                }
                else if (child is ContainerBox container)
                {
                    AppendText(builder, container);
                }
            }
        }
    }

    /// <summary>
    /// Collects the white space of the base row before a base (<paramref name="end"/>): the white space between the columns, or
    /// between the bases of a column. The intra-level white space that is not paired is collected without its anonymous base.
    /// </summary>
    private int CollectRubyRowWhiteSpace(ContainerBox parent, int row, int end)
    {
        for (; row < end; row++)
        {
            if (_rubyScratchBoxes[row] is ContainerBox whiteSpace)
            {
                foreach (var child in whiteSpace.Children)
                {
                    CollectChild(whiteSpace, child);
                }
            }
            else
            {
                CollectChild(parent, _rubyScratchBoxes[row]);
            }
        }

        return row;
    }

    /// <summary>Adds the item that starts a ruby column.</summary>
    private void StartRubyColumn(int columnIndex)
    {
        var column = _rubyColumns[columnIndex];
        column.MarkerItem = _items.Count;
        _rubyColumns[columnIndex] = column;
        AddItem(new InlineItem { Kind = InlineItemKind.RubyColumn, Start = _length, End = _length, Box = column.Ruby, Container = column.Ruby, Style = column.Ruby.Style, RubyColumn = columnIndex + 1 });
        _currentRubyColumn = columnIndex;
    }

    /// <summary>
    /// Collects the items of an annotation. Its white space is collapsed as if it were on a line of its own (the state of the
    /// white space processing of the base text is restored after it): the intra-level white space is collapsed away, but its
    /// annotation box keeps its place. The content of a hidden annotation is not collected (it is laid out as an empty annotation).
    /// </summary>
    private void CollectRubyAnnotation(int index)
    {
        var box = _rubyAnnotations[index].Box;
        var (afterCollapsibleSpace, inWord, discard) = (_afterCollapsibleSpace, _inWord, _discardLeadingWhiteSpace);
        _afterCollapsibleSpace = true;
        _inWord = false;
        _discardLeadingWhiteSpace = WhiteSpaceDiscard.None;
        _rubyAnnotationDepth++;
        _hasRubyAnnotations = true;
        var start = _items.Count;
        OpenInlineBox(box);
        if (!_rubyAnnotations[index].IsHidden)
        {
            CollectChildren(box);
        }

        DiscardTrailingWhiteSpace(collapsibleOnly: true);
        CloseInlineBox(box);
        _rubyAnnotationDepth--;
        (_afterCollapsibleSpace, _inWord, _discardLeadingWhiteSpace) = (afterCollapsibleSpace, inWord, discard);

        var info = _rubyAnnotations[index];
        info.StartItem = start;
        info.EndItem = _items.Count;
        _rubyAnnotations[index] = info;
    }

    /// <summary>Updates the item indices of the ruby columns after the items were split (<see cref="_itemRemap"/>: the new index of each old index).</summary>
    private void RemapRubyItems()
    {
        var remap = _itemRemap;
        var columns = CollectionsMarshal.AsSpan(_rubyColumns);
        foreach (ref var column in columns)
        {
            column.MarkerItem = remap[column.MarkerItem];
            column.AnnotationStartItem = remap[column.AnnotationStartItem];
            column.EndItem = remap[column.EndItem];
        }

        foreach (ref var info in CollectionsMarshal.AsSpan(_rubyBases))
        {
            info.StartItem = remap[info.StartItem];
            info.EndItem = remap[info.EndItem];
        }

        foreach (ref var info in CollectionsMarshal.AsSpan(_rubyAnnotations))
        {
            info.StartItem = remap[info.StartItem];
            info.EndItem = remap[info.EndItem];
        }
    }

    /// <summary>Ensures that <see cref="_itemRemap"/> can map the indices of the items (and the end index).</summary>
    private void EnsureItemRemap(int count)
    {
        if (_itemRemap.Length < count + 1)
        {
            _itemRemap = new int[Math.Max(count + 1, _itemRemap.Length * 2)];
        }
    }

    /// <summary>
    /// Computes the soft wrap opportunities of the text without the text of the ruby annotations: the opportunities between the
    /// bases and around the ruby containers are the ones of the base text with the text around it
    /// (https://drafts.csswg.org/css-ruby-1/#break-between). There is no opportunity in the annotations.
    /// </summary>
    private void FindOpportunitiesWithoutAnnotations(Span<LineBreakOpportunity> opportunities)
    {
        if (_rubyFilteredText.Length < _length)
        {
            var size = Math.Max(_length, _rubyFilteredText.Length * 2);
            _rubyFilteredText = new char[size];
            _rubyFilteredPositions = new int[size];
            _rubyFilteredOpportunities = new LineBreakOpportunity[size];
        }

        opportunities.Clear();
        var count = 0;
        var nextRun = 0;
        _rubyFilteredRuns.Clear();
        foreach (ref readonly var item in Items)
        {
            if (!item.HasText || item.HasFlag(InlineItemFlags.RubyAnnotation))
                continue;

            // The runs of the options start at the first character of an item (the runs of the annotations were not added)
            while (nextRun < _breakRuns.Count && _breakRuns[nextRun].Start <= item.Start)
            {
                _rubyFilteredRuns.Add(_breakRuns[nextRun] with { Start = count });
                nextRun++;
            }

            for (var i = item.Start; i < item.End; i++)
            {
                _rubyFilteredText[count] = _text[i];
                _rubyFilteredPositions[count] = i;
                count++;
            }
        }

        if (count == 0)
            return;

        var filtered = _rubyFilteredOpportunities.AsSpan(0, count);
        _lineBreaker.FindOpportunities(_rubyFilteredText.AsSpan(0, count), CollectionsMarshal.AsSpan(_rubyFilteredRuns), filtered);
        for (var i = 0; i < count; i++)
        {
            // An opportunity before the isolate controls of the internal ruby boxes (after a space at the end of a base, the
            // controls are combining marks that UAX #14 treats as letters) is the opportunity before the character after them: the
            // first character of the next ruby column (see GetRubyColumnBreakPosition)
            var opportunity = filtered[i];
            if (opportunity is not LineBreakOpportunity.None and not LineBreakOpportunity.Mandatory && IsIsolateControl(_rubyFilteredText[i]))
            {
                var next = i + 1;
                while (next < count && IsIsolateControl(_rubyFilteredText[next]))
                {
                    next++;
                }

                if (next < count && filtered[next] == LineBreakOpportunity.None)
                {
                    filtered[next] = opportunity;
                    filtered[i] = LineBreakOpportunity.None;
                    opportunity = LineBreakOpportunity.None;
                }
            }

            opportunities[_rubyFilteredPositions[i]] = opportunity;
        }
    }

    /// <summary>Returns the outermost ruby column that contains an item, or -1.</summary>
    private int GetOutermostRubyColumn(in InlineItem item)
    {
        var column = item.RubyColumn - 1;
        while (column >= 0 && _rubyColumns[column].Parent >= 0)
        {
            column = _rubyColumns[column].Parent;
        }

        return column;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Measure
    // ----------------------------------------------------------------------------------------------------------------

    private void EnsureRubyBuffers()
    {
        var bases = _rubyBases.Count;
        if (_rubySlotWidths.Length < bases)
        {
            var size = Math.Max(bases, _rubySlotWidths.Length * 2);
            _rubyContentWidths = new double[size];
            _rubySlotWidths = new double[size];
            _rubyGapWidths = new double[size];
            _rubyBaseExtraStart = new double[size];
            _rubyBaseExtraEnd = new double[size];
            _rubyBaseStartX = new double[size];
            _rubyBaseEndX = new double[size];
        }

        var columns = _rubyColumns.Count;
        if (_rubyOverhangStart.Length < columns)
        {
            var size = Math.Max(columns, _rubyOverhangStart.Length * 2);
            _rubyOverhangStart = new double[size];
            _rubyOverhangEnd = new double[size];
        }

        var annotations = _rubyAnnotations.Count;
        if (_rubyAnnotationWidths.Length < annotations)
        {
            var size = Math.Max(annotations, _rubyAnnotationWidths.Length * 2);
            _rubyAnnotationWidths = new double[size];
            _rubyAnnotationExtraStart = new double[size];
            _rubyAnnotationExtraEnd = new double[size];
        }
    }

    /// <summary>
    /// Computes the inline size of a ruby column (https://drafts.csswg.org/css-ruby-1/#ruby-layout): the slot of each base is as
    /// wide as its content and as the annotations paired with it alone, then the annotations spanning several bases widen their
    /// slots equally when they are wider than them. The widths are kept in <see cref="_rubyContentWidths"/>,
    /// <see cref="_rubySlotWidths"/>, <see cref="_rubyGapWidths"/> (the white space before a base) and <see cref="_rubyAnnotationWidths"/>.
    /// The line breaking passes the position of the column on the line (<paramref name="tabOrigin"/>): the advances of the
    /// preserved tabs of the bases are computed from it (as if the bases were not aligned in their slots); else the advances
    /// computed by the line breaking are used.
    /// </summary>
    private double ComputeRubyColumnLayout(int columnIndex, bool isFirstLine, bool minContent, double tabOrigin = double.NaN)
    {
        EnsureRubyBuffers();
        var column = _rubyColumns[columnIndex];
        var baseEnd = column.FirstBase + column.BaseCount;
        for (var b = column.FirstBase; b < baseEnd; b++)
        {
            var info = _rubyBases[b];
            var gap = b > column.FirstBase ? MeasureRubyItems(_rubyBases[b - 1].EndItem, info.StartItem, isFirstLine, minContent, double.NaN) : 0;
            var width = MeasureRubyItems(info.StartItem, info.EndItem, isFirstLine, minContent, tabOrigin) - GetRubyTrimmedWidth(columnIndex, info.StartItem, info.EndItem, isFirstLine);
            _rubyContentWidths[b] = width;
            _rubySlotWidths[b] = width;
            _rubyGapWidths[b] = gap;
            tabOrigin += gap + width;
        }

        var annotationEnd = column.FirstAnnotation + column.AnnotationCount;
        for (var a = column.FirstAnnotation; a < annotationEnd; a++)
        {
            var info = _rubyAnnotations[a];
            var width = MeasureRubyItems(info.StartItem, info.EndItem, isFirstLine, minContent, double.NaN);
            _rubyAnnotationWidths[a] = width;
            if (info.FirstBase == info.LastBase)
            {
                _rubySlotWidths[info.FirstBase] = Math.Max(_rubySlotWidths[info.FirstBase], width);
            }
        }

        for (var a = column.FirstAnnotation; a < annotationEnd; a++)
        {
            var info = _rubyAnnotations[a];
            if (info.FirstBase == info.LastBase)
                continue;

            var extra = _rubyAnnotationWidths[a] - GetRubySpanWidth(info.FirstBase, info.LastBase);
            if (extra > 0)
            {
                var share = extra / (info.LastBase - info.FirstBase + 1);
                for (var b = info.FirstBase; b <= info.LastBase; b++)
                {
                    _rubySlotWidths[b] += share;
                }
            }
        }

        return GetRubySpanWidth(column.FirstBase, baseEnd - 1);
    }

    /// <summary>The inline size of the slots of a range of bases of a column, with the white space between them.</summary>
    private double GetRubySpanWidth(int firstBase, int lastBase)
    {
        var width = 0d;
        for (var b = firstBase; b <= lastBase; b++)
        {
            width += _rubySlotWidths[b] + (b > firstBase ? _rubyGapWidths[b] : 0);
        }

        return width;
    }

    /// <summary>
    /// Measures the items of a range as they are laid out on a line, without breaking them: the advances of the texts, the inline
    /// edges of the inline boxes, the margin boxes of the atomic inlines (their min-content contributions for the min-content
    /// size), and the inline size of the ruby columns in the range (a ruby in a base).
    /// </summary>
    private double MeasureRubyItems(int start, int end, bool isFirstLine, bool minContent, double tabOrigin)
    {
        var width = 0d;
        for (var i = start; i < end; i++)
        {
            ref var item = ref Items[i];
            switch (item.Kind)
            {
                case InlineItemKind.Text when item.HasFlag(InlineItemFlags.Tab):
                    if (!double.IsNaN(tabOrigin))
                    {
                        item.InlineSize = GetTabAdvance(item, tabOrigin + width);
                    }

                    width += item.InlineSize;
                    break;

                case InlineItemKind.Text:
                    width += GetShaped(i, isFirstLine).Width;
                    break;

                case InlineItemKind.OpenTag or InlineItemKind.CloseTag:
                    width += item.InlineSize;
                    break;

                case InlineItemKind.Atomic:
                    width += minContent ? item.MinContentInlineSize : item.InlineSize;
                    break;

                case InlineItemKind.RubyColumn:
                    var column = item.RubyColumn - 1;
                    width += ComputeRubyColumnLayout(column, isFirstLine, minContent, double.IsNaN(tabOrigin) ? tabOrigin : tabOrigin + width);
                    i = _rubyColumns[column].EndItem - 1;
                    break;
            }
        }

        return width;
    }

    /// <summary>
    /// The inline size of the text of the bases of a column (the items of a range) that is removed at the end of the line (see
    /// <see cref="_rubyTrimmedColumn"/>).
    /// </summary>
    private double GetRubyTrimmedWidth(int columnIndex, int start, int end, bool isFirstLine)
    {
        if (columnIndex != _rubyTrimmedColumn)
            return 0;

        var width = 0d;
        for (var i = start; i < end; i++)
        {
            ref readonly var item = ref Items[i];
            if (item.Kind == InlineItemKind.Text && !item.HasFlag(InlineItemFlags.Tab) && !item.HasFlag(InlineItemFlags.RubyAnnotation) && item.End > _rubyTrimmedPosition)
            {
                var shaped = GetShaped(i, isFirstLine);
                width += shaped.GetWidth(Math.Max(0, _rubyTrimmedPosition - item.Start), item.End - item.Start);
            }
        }

        return width;
    }

    /// <summary>
    /// Returns how much narrower a ruby column is when it ends a line: the collapsible spaces at the end of its last base are removed
    /// (see TrimTrailingSpaces). <paramref name="width"/> is its inline size with them.
    /// </summary>
    private double GetRubyColumnTrailingWidth(int columnIndex, bool isFirstLine, bool minContent, double width)
    {
        var column = _rubyColumns[columnIndex];
        var position = -1;
        for (var i = column.AnnotationStartItem - 1; i > column.MarkerItem; i--)
        {
            ref readonly var item = ref Items[i];
            if (item.Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.Control)
                continue;

            if (item.Kind != InlineItemKind.Text || item.HasFlag(InlineItemFlags.Tab) || item.HasFlag(InlineItemFlags.RubyAnnotation) || !item.HasFlag(InlineItemFlags.CollapsesSpaces))
                break;

            var end = item.End;
            while (end > item.Start && _text[end - 1] == ' ')
            {
                end--;
            }

            if (end == item.End)
                break;

            position = end;
            if (end > item.Start)
                break;
        }

        if (position < 0)
            return 0;

        var (trimmedColumn, trimmedPosition) = (_rubyTrimmedColumn, _rubyTrimmedPosition);
        (_rubyTrimmedColumn, _rubyTrimmedPosition) = (columnIndex, position);
        var trimmedWidth = ComputeRubyColumnLayout(columnIndex, isFirstLine, minContent);
        (_rubyTrimmedColumn, _rubyTrimmedPosition) = (trimmedColumn, trimmedPosition);
        return Math.Max(0, width - trimmedWidth);
    }

    /// <summary>The sum of the inline sizes of the items of a range as the leaves of a line measure them (the ruby columns are not laid out).</summary>
    private double MeasureRubyLeafItems(int start, int end, bool isFirstLine)
    {
        var width = 0d;
        for (var i = start; i < end; i++)
        {
            ref readonly var item = ref Items[i];
            width += item.Kind switch
            {
                InlineItemKind.Text when item.HasFlag(InlineItemFlags.Tab) => item.InlineSize,
                InlineItemKind.Text => GetShaped(i, isFirstLine).Width,
                InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.Atomic => item.InlineSize,
                _ => 0,
            };
        }

        return width;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Line breaking
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// The position of the soft wrap opportunity before a ruby column: the first character of its bases that is not a bidi
    /// control code (the opportunities are computed without the control codes, which are combining marks for UAX #14).
    /// </summary>
    private int GetRubyColumnBreakPosition(int columnIndex)
    {
        var column = _rubyColumns[columnIndex];
        var items = Items;
        for (var i = column.MarkerItem + 1; i < column.AnnotationStartItem; i++)
        {
            if (items[i].HasText && items[i].Kind != InlineItemKind.Control)
                return items[i].Start;
        }

        return items[column.MarkerItem].Start;
    }

    /// <summary>Returns the first float of a ruby column whose position is not decided yet, or -1.</summary>
    private int FindUnplacedRubyFloat(int columnIndex)
    {
        var column = _rubyColumns[columnIndex];
        var items = Items;
        for (var i = column.MarkerItem + 1; i < column.EndItem; i++)
        {
            if (items[i].IsFloat && !items[i].HasFlag(InlineItemFlags.FloatPlaced))
                return i;
        }

        return -1;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Line box: inline axis
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>
    /// Lays out the ruby columns of the line in the inline axis: the inline size of each column, and the distribution of the space
    /// that the content of its bases and annotations does not fill (<see cref="AlignRubyColumn"/>). Returns the difference between the
    /// inline size of the columns and the inline size of their leaves (the annotations are not after the bases on the line).
    /// </summary>
    private double LayoutRubyColumnsOnLine(int startItem, int endItem, bool isFirstLine)
    {
        _lineStartItem = startItem;
        _lineEndItem = Math.Min(endItem, _items.Count);
        var correction = 0d;
        for (var i = _lineStartItem; i < _lineEndItem; i++)
        {
            ref readonly var item = ref Items[i];
            if (item.Kind != InlineItemKind.RubyColumn)
                continue;

            var columnIndex = item.RubyColumn - 1;
            var column = _rubyColumns[columnIndex];
            var width = ComputeRubyColumnLayout(columnIndex, isFirstLine, minContent: false);
            var (overhangStart, overhangEnd) = GetRubyOverhang(columnIndex, isFirstLine, _lineStartItem, _lineEndItem);
            _rubyOverhangStart[columnIndex] = overhangStart;
            _rubyOverhangEnd[columnIndex] = overhangEnd;
            if (column.Parent < 0)
            {
                correction += width - overhangStart - overhangEnd - MeasureRubyLeafItems(column.MarkerItem + 1, column.EndItem, isFirstLine)
                    + GetRubyTrimmedWidth(columnIndex, column.MarkerItem + 1, column.AnnotationStartItem, isFirstLine);
            }

            AlignRubyColumn(columnIndex, isFirstLine);
        }

        _rubyTrimmedColumn = -1;
        return correction;
    }

    /// <summary>
    /// Distributes the space of the slots of the bases and of the annotations of a column that their content does not fill
    /// (https://drafts.csswg.org/css-ruby-1/#ruby-align-property): the extra space at the edges of their boxes
    /// (<see cref="_rubyBaseExtraStart"/>...), or added to the justification opportunities of their text.
    /// </summary>
    private void AlignRubyColumn(int columnIndex, bool isFirstLine)
    {
        var column = _rubyColumns[columnIndex];
        for (var b = column.FirstBase; b < column.FirstBase + column.BaseCount; b++)
        {
            var info = _rubyBases[b];
            var extra = _rubySlotWidths[b] - _rubyContentWidths[b];
            if (info.Box is null)
            {
                // An empty base: its slot is the width of a gap leaf (see CollectLeaves), or is added to the base before it
                _rubyBaseExtraStart[b] = 0;
                _rubyBaseExtraEnd[b] = 0;
                if (b > column.FirstBase && _rubyBases[b - 1].Box is not null)
                {
                    _rubyBaseExtraEnd[b - 1] += _rubySlotWidths[b] + _rubyGapWidths[b];
                }

                continue;
            }

            (_rubyBaseExtraStart[b], _rubyBaseExtraEnd[b]) = DistributeRubySpace(extra, info.Box.Style.Layout.Text.RubyAlign, info.StartItem, info.EndItem, annotation: false, isFirstLine);
        }

        for (var a = column.FirstAnnotation; a < column.FirstAnnotation + column.AnnotationCount; a++)
        {
            var info = _rubyAnnotations[a];
            var extra = GetRubySpanWidth(info.FirstBase, info.LastBase) - _rubyAnnotationWidths[a];
            (_rubyAnnotationExtraStart[a], _rubyAnnotationExtraEnd[a]) = DistributeRubySpace(extra, info.Box.Style.Layout.Text.RubyAlign, info.StartItem, info.EndItem, annotation: true, isFirstLine);
        }
    }

    /// <summary>
    /// Distributes space in a base or an annotation by <c>ruby-align</c> and returns the space added at its inline-start and
    /// inline-end edges: <c>start</c> puts it at the end, <c>center</c> on both sides, <c>space-between</c> distributes it to the
    /// justification opportunities of the text (see <see cref="CollectRubyJustificationOpportunities"/>) and <c>space-around</c> too, with half an
    /// opportunity at each edge (https://drafts.csswg.org/css-ruby-1/#valdef-ruby-align-space-around). Without justification
    /// opportunity, the content is centered.
    /// </summary>
    private (double Start, double End) DistributeRubySpace(double space, RubyAlign align, int startItem, int endItem, bool annotation, bool isFirstLine)
    {
        if (space <= LayoutEpsilon)
            return default;

        switch (align)
        {
            case RubyAlign.Start:
                return (0, space);
            case RubyAlign.Center:
                return (space / 2, space / 2);
        }

        CollectRubyJustificationOpportunities(startItem, endItem, annotation, isFirstLine);
        var count = _rubyPositions.Count;
        if (count == 0)
            return (space / 2, space / 2);

        var parts = align == RubyAlign.SpaceAround ? count + 1 : count;
        var share = space / parts;
        foreach (var position in _rubyPositions)
        {
            AddLineSpacing(position, 0, share);
        }

        return align == RubyAlign.SpaceAround ? (share / 2, share / 2) : default;
    }

    /// <summary>
    /// Collects in <see cref="_rubyPositions"/> the justification opportunities of the text of a base (the annotations of the ruby
    /// columns in it excluded) or of an annotation: as in Gecko, after each typographic character unit (as <c>text-justify:
    /// inter-character</c>, the <c>ruby</c> value of the UA style sheet), except after the last one.
    /// </summary>
    private void CollectRubyJustificationOpportunities(int startItem, int endItem, bool annotation, bool isFirstLine)
    {
        _rubyPositions.Clear();
        var items = Items;
        var lastEnd = -1;
        var lastCollectedEnd = -1;
        for (var i = startItem; i < endItem; i++)
        {
            ref readonly var item = ref items[i];
            if (item.Kind != InlineItemKind.Text || item.HasFlag(InlineItemFlags.Tab) || item.HasFlag(InlineItemFlags.RubyAnnotation) != annotation)
                continue;

            var shaped = GetShaped(i, isFirstLine);
            var glyphs = shaped.Glyphs;
            var g = 0;
            while (g < glyphs.Length)
            {
                var cluster = glyphs[g].Cluster;
                var invisible = glyphs[g].IsInvisible;
                g++;
                while (g < glyphs.Length && glyphs[g].Cluster == cluster)
                {
                    g++;
                }

                lastEnd = (g < glyphs.Length ? glyphs[g].Cluster : shaped.Length) + item.Start;
                if (!invisible)
                {
                    _rubyPositions.Add(item.Start + cluster);
                    lastCollectedEnd = lastEnd;
                }
            }
        }

        // No opportunity after the last character
        if (_rubyPositions.Count > 0 && lastCollectedEnd >= lastEnd)
        {
            _rubyPositions.RemoveAt(_rubyPositions.Count - 1);
        }
    }

    /// <summary>
    /// Moves the leaves of the annotations of each ruby column after the leaves of its bases in the visual order of a reordered
    /// line: the annotations are placed over the slots of their bases, which must be placed first.
    /// </summary>
    private void RegroupRubyLeaves(int count)
    {
        var columns = _rubyColumns.Count;
        if (_rubyRemainingLeaves.Length < columns)
        {
            _rubyRemainingLeaves = new int[Math.Max(columns, _rubyRemainingLeaves.Length * 2)];
        }

        var leaves = CollectionsMarshal.AsSpan(_leaves);
        var hasAnnotation = false;
        foreach (ref readonly var leaf in leaves)
        {
            if (leaf.RubyColumn > 0)
            {
                _rubyRemainingLeaves[leaf.RubyColumn - 1] = 0;
                hasAnnotation |= leaf.IsRubyAnnotation;
            }
        }

        if (!hasAnnotation)
            return;

        foreach (ref readonly var leaf in leaves)
        {
            if (leaf.RubyColumn > 0 && !leaf.IsRubyAnnotation)
            {
                _rubyRemainingLeaves[leaf.RubyColumn - 1]++;
            }
        }

        if (_rubyRegroupedOrder.Length < count)
        {
            _rubyRegroupedOrder = new int[Math.Max(count, _rubyRegroupedOrder.Length * 2)];
        }

        var output = 0;
        _rubyPendingLeaves.Clear();
        for (var v = 0; v < count; v++)
        {
            var leafIndex = _visualOrder[v];
            ref readonly var leaf = ref leaves[leafIndex];
            var column = leaf.RubyColumn - 1;
            if (column >= 0 && leaf.IsRubyAnnotation && _rubyRemainingLeaves[column] > 0)
            {
                _rubyPendingLeaves.Add(leafIndex);
                continue;
            }

            _rubyRegroupedOrder[output++] = leafIndex;
            if (column >= 0 && !leaf.IsRubyAnnotation && --_rubyRemainingLeaves[column] == 0)
            {
                // The bases of the column are placed: its annotations follow
                for (var p = 0; p < _rubyPendingLeaves.Count; p++)
                {
                    if (leaves[_rubyPendingLeaves[p]].RubyColumn - 1 == column)
                    {
                        _rubyRegroupedOrder[output++] = _rubyPendingLeaves[p];
                        _rubyPendingLeaves.RemoveAt(p);
                        p--;
                    }
                }
            }
        }

        foreach (var leafIndex in _rubyPendingLeaves)
        {
            _rubyRegroupedOrder[output++] = leafIndex;
        }

        _rubyPendingLeaves.Clear();
        _rubyRegroupedOrder.AsSpan(0, count).CopyTo(_visualOrder);
    }

    /// <summary>Sets the ruby fields of a leaf of the line: its outermost ruby column, and whether it is in the annotations of that column.</summary>
    private void SetRubyLeafInfo(ref LineLeaf leaf, int itemIndex)
    {
        if (itemIndex < 0)
            return;

        var column = GetOutermostRubyColumn(Items[itemIndex]);
        if (column < 0)
            return;

        leaf.RubyColumn = column + 1;
        leaf.IsRubyAnnotation = itemIndex >= _rubyColumns[column].AnnotationStartItem && itemIndex < _rubyColumns[column].EndItem;
    }

    /// <summary>
    /// Called when the fragment of an inline box is opened on the line (<see cref="OpenNode"/>), before its inline-start margin:
    /// records the start of the slot of a ruby base, or moves to the start of the slot of the bases of an annotation (the position
    /// in the base row is restored when it is closed). Returns the extra space added after its inline-start border and padding.
    /// </summary>
    private double OnRubyBoxOpened(ContainerBox box, ref double x)
    {
        if (!_rubyBoxes.TryGetValue(box, out var index))
            return 0;

        var leftIsStart = box.Style.Layout.Direction != Direction.Rtl;
        if (index >= 0)
        {
            // The first base of a column moves back over the content before the column (ruby-overhang)
            if (GetRubyColumnOfFirstBase(index) is var column and >= 0)
            {
                x -= leftIsStart ? _rubyOverhangStart[column] : _rubyOverhangEnd[column];
            }

            _rubyBaseStartX[index] = x;
            return leftIsStart ? _rubyBaseExtraStart[index] : _rubyBaseExtraEnd[index];
        }

        var annotation = ~index;
        _rubySavedX.Add(x);
        var info = _rubyAnnotations[annotation];
        var start = double.PositiveInfinity;
        for (var b = info.FirstBase; b <= info.LastBase; b++)
        {
            start = Math.Min(start, _rubyBaseStartX[b]);
        }

        if (!double.IsInfinity(start))
        {
            x = start;
        }

        return leftIsStart ? _rubyAnnotationExtraStart[annotation] : _rubyAnnotationExtraEnd[annotation];
    }

    /// <summary>
    /// Called when the fragment of an inline box is closed on the line (<see cref="CloseNode"/>): returns the extra space added
    /// before the inline-end border and padding of a ruby base or annotation (<paramref name="beforeEdges"/>), or, after its
    /// inline-end margin, records the end of the slot of a base, restores the position of the base row after an annotation, or
    /// sets the inline extent of an annotation container to the one of its annotations.
    /// </summary>
    private double OnRubyBoxClosed(ref LineNode node, bool beforeEdges, ref double x)
    {
        var box = node.Box!;
        if (!_rubyBoxes.TryGetValue(box, out var index))
        {
            if (!beforeEdges && box.Display.Internal == DisplayInternal.RubyTextContainer)
            {
                // The annotation container spans its annotations (they are placed over their bases)
                var nodes = CollectionsMarshal.AsSpan(_nodes);
                var start = double.PositiveInfinity;
                var end = double.NegativeInfinity;
                for (var child = node.FirstChild; child >= 0; child = nodes[child].NextSibling)
                {
                    start = Math.Min(start, nodes[child].X);
                    end = Math.Max(end, nodes[child].X + nodes[child].Width);
                }

                if (!double.IsInfinity(start))
                {
                    node.X = start;
                    node.Width = end - start;
                }
            }

            return 0;
        }

        var leftIsStart = box.Style.Layout.Direction != Direction.Rtl;
        if (index >= 0)
        {
            if (beforeEdges)
                return leftIsStart ? _rubyBaseExtraEnd[index] : _rubyBaseExtraStart[index];

            _rubyBaseEndX[index] = x;

            // The content after the column moves back over the end of the column (ruby-overhang)
            if (GetRubyColumnOfLastBase(index) is var column and >= 0)
            {
                x -= leftIsStart ? _rubyOverhangEnd[column] : _rubyOverhangStart[column];
            }

            return 0;
        }

        if (beforeEdges)
            return leftIsStart ? _rubyAnnotationExtraEnd[~index] : _rubyAnnotationExtraStart[~index];

        if (_rubySavedX.Count > 0)
        {
            x = _rubySavedX[^1];
            _rubySavedX.RemoveAt(_rubySavedX.Count - 1);
        }

        return 0;
    }

    /// <summary>Returns the ruby column whose first (last) base is a base, or -1.</summary>
    private int GetRubyColumnOfFirstBase(int baseIndex) => FindRubyColumnOfBase(baseIndex) is var column and >= 0 && _rubyColumns[column].FirstBase == baseIndex ? column : -1;

    private int GetRubyColumnOfLastBase(int baseIndex) => FindRubyColumnOfBase(baseIndex) is var column and >= 0 && _rubyColumns[column].FirstBase + _rubyColumns[column].BaseCount - 1 == baseIndex ? column : -1;

    /// <summary>Returns the ruby column of a base: the column of the items of the base.</summary>
    private int FindRubyColumnOfBase(int baseIndex)
    {
        var info = _rubyBases[baseIndex];
        return info.StartItem < info.EndItem ? Items[info.StartItem].RubyColumn - 1 : -1;
    }

    /// <summary>
    /// Returns how much a ruby column overhangs the content before and after it on the line (https://drafts.csswg.org/css-ruby-1/#ruby-overhang),
    /// as Blink does: when an annotation is wider than the bases, the space of the bases at their edges may be laid out over the
    /// adjacent text. With <c>auto</c>, up to half the font size of the annotation, over a text that is not larger than the ruby
    /// container; with <c>spaces</c>, only over the adjacent spaces (the spaces, the tabs, the ideographic space, the blank half of
    /// the fullwidth brackets and quarters of the middle dots, unless a font feature such as <c>halt</c> changes their widths).
    /// <see cref="ComputeRubyColumnLayout"/> must have laid out the column. <paramref name="endPosition"/> is the inline position
    /// of the end of the column on the line without its overhangs, when the line breaking knows it: the advance of a preserved tab
    /// after the column depends on the position of the end of the column, which depends on the overhang over the tab (NaN: the
    /// tabs have the advances that the line breaking gave them).
    /// </summary>
    private (double Start, double End) GetRubyOverhang(int columnIndex, bool isFirstLine, int lineStartItem, int lineEndItem, bool includeStart = true, double endPosition = double.NaN)
    {
        var column = _rubyColumns[columnIndex];
        if (column.Parent >= 0 || column.AnnotationCount == 0 || _rubyBases[column.FirstBase].Box is not { } firstBase)
            return default;

        var baseEnd = column.FirstBase + column.BaseCount;
        var width = GetRubySpanWidth(column.FirstBase, baseEnd - 1);
        var baseWidth = 0d;
        for (var b = column.FirstBase; b < baseEnd; b++)
        {
            baseWidth += _rubyContentWidths[b] + (b > column.FirstBase ? _rubyGapWidths[b] : 0);
        }

        // An empty base does not overhang (there is no base content to center the annotation over)
        var space = width - baseWidth;
        if (space <= LayoutEpsilon || baseWidth <= 0)
            return default;

        // The annotations that make the column wider than its bases
        var halfFontSize = 0d;
        for (var a = column.FirstAnnotation; a < column.FirstAnnotation + column.AnnotationCount; a++)
        {
            if (_rubyAnnotationWidths[a] >= GetRubySpanWidth(_rubyAnnotations[a].FirstBase, _rubyAnnotations[a].LastBase) - LayoutEpsilon)
            {
                halfFontSize = Math.Max(halfFontSize, _rubyAnnotations[a].Box.Style.Layout.FontSize / 2);
            }
        }

        if (halfFontSize <= 0)
            return default;

        // The space at the edges of the bases (see DistributeRubySpace)
        var align = firstBase.Style.Layout.Text.RubyAlign;
        var (startSpace, endSpace) = align switch
        {
            RubyAlign.Start => (0d, space),
            RubyAlign.SpaceAround when column.BaseCount == 1 && CountRubyJustificationOpportunities(column.FirstBase, isFirstLine) is var count and > 0 => (space / (2 * (count + 1)), space / (2 * (count + 1))),
            RubyAlign.SpaceBetween when column.BaseCount == 1 && CountRubyJustificationOpportunities(column.FirstBase, isFirstLine) > 0 => (0d, 0d),
            _ => (space / 2, space / 2),
        };

        var spaces = column.Ruby.Style.Layout.Text.RubyOverhangSpaces;
        if (!spaces)
        {
            startSpace = Math.Min(startSpace, halfFontSize);
            endSpace = Math.Min(endSpace, halfFontSize);
        }

        var rubyFontSize = column.Ruby.Style.Layout.FontSize;
        var start = includeStart && startSpace > 0 ? Math.Min(startSpace, GetRubyAdjacentOverhangSpace(column.MarkerItem - 1, lineStartItem, -1, spaces, rubyFontSize, isFirstLine)) : 0;
        var end = 0d;
        if (endSpace > 0)
        {
            var position = endPosition - start;
            end = Math.Min(endSpace, GetRubyAdjacentOverhangSpace(column.EndItem, lineEndItem, 1, spaces, rubyFontSize, isFirstLine, position));
            if (!double.IsNaN(position))
            {
                // The tabs after the column are measured again from the end of the column with its overhang: the overhang
                // is reduced until the spaces at that position are as large as it
                for (var i = 0; i < 4 && end > 0; i++)
                {
                    var available = GetRubyAdjacentOverhangSpace(column.EndItem, lineEndItem, 1, spaces, rubyFontSize, isFirstLine, position - end);
                    if (available >= end - LayoutEpsilon)
                        break;

                    end = available;
                }
            }
        }

        return (start, end);
    }

    /// <summary>
    /// Returns the overhang of the ruby column that ends just before an item over the content after it (the text between two ruby
    /// columns is overhung by the first one, then by the second one with what remains, as in Blink), or 0.
    /// </summary>
    private double GetPreviousRubyEndOverhang(int index, int lineStartItem, bool isFirstLine)
    {
        var items = Items;
        while (index >= lineStartItem && index >= 0 && items[index].Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.Control)
        {
            index--;
        }

        if (index < lineStartItem || index < 0 || GetOutermostRubyColumn(items[index]) is not (var column and >= 0))
            return 0;

        ComputeRubyColumnLayout(column, isFirstLine, minContent: false);
        return GetRubyOverhang(column, isFirstLine, lineStartItem, int.MaxValue, includeStart: false).End;
    }

    /// <summary>The number of justification opportunities of the text of a base.</summary>
    private int CountRubyJustificationOpportunities(int baseIndex, bool isFirstLine)
    {
        var info = _rubyBases[baseIndex];
        CollectRubyJustificationOpportunities(info.StartItem, info.EndItem, annotation: false, isFirstLine);
        var count = _rubyPositions.Count;
        _rubyPositions.Clear();
        return count;
    }

    /// <summary>
    /// Returns the inline size of the content next to a ruby column (before it when <paramref name="step"/> is -1) that the column
    /// may overhang: the adjacent text (not larger than the ruby container), or only its spaces with <c>ruby-overhang: spaces</c>.
    /// <paramref name="tabPosition"/> is the inline position of the content after the column, where its tabs are measured (NaN:
    /// the tabs have the advances that the line breaking gave them).
    /// </summary>
    private double GetRubyAdjacentOverhangSpace(int index, int limit, int step, bool spaces, double rubyFontSize, bool isFirstLine, double tabPosition = double.NaN)
    {
        var items = Items;
        var text = _text.AsSpan(0, _length);
        var result = 0d;
        for (; step < 0 ? index >= limit && index >= 0 : index < limit && index < items.Length; index += step)
        {
            ref readonly var item = ref items[index];
            if (item.Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag or InlineItemKind.Control || item.Kind == InlineItemKind.OutOfFlow)
                continue;

            if (item.Kind != InlineItemKind.Text || item.HasFlag(InlineItemFlags.RubyAnnotation) || item.RubyColumn != 0)
                return spaces && step < 0 ? Math.Max(0, result - GetPreviousRubyEndOverhang(index, limit, isFirstLine)) : result;

            if (!spaces)
            {
                if (item.Style.Layout.FontSize > rubyFontSize)
                    return 0;

                var width = GetShaped(index, isFirstLine).Width;
                return step < 0 ? Math.Max(0, width - GetPreviousRubyEndOverhang(index - 1, limit, isFirstLine)) : width;
            }

            if (item.HasFlag(InlineItemFlags.Tab))
            {
                // A tab after the column, when the position of the end of the column is known: its advance at that position
                result += double.IsNaN(tabPosition) ? item.InlineSize : GetTabAdvance(item, tabPosition + result);
                continue;
            }

            // The spaces at the edge of the text next to the column, and the blank part of the punctuation after them
            var shaped = GetShaped(index, isFirstLine);
            var position = step < 0 ? item.End - 1 : item.Start;
            while (position >= item.Start && position < item.End)
            {
                var c = text[position];
                var advance = shaped.GetWidth(position - item.Start, position - item.Start + 1);
                if (c is ' ' or '\t' or (char)0x00A0 or (char)0x3000 || IsSpace(c))
                {
                    result += advance;
                    position += step;
                    continue;
                }

                // The punctuation of a text that enables a feature that changes the widths of the glyphs (e.g. halt) has no blank
                // part (see GetHalfWidthSpacing)
                var punctuation = shaped.Options.Features is { } features && HasExclusiveWidthFeature(features)
                    ? FullwidthPunctuation.None
                    : TextSpacingClasses.GetFullwidthPunctuation(c, GetPunctuationConvention(item, c));
                if (punctuation == FullwidthPunctuation.MiddleDot)
                {
                    result += advance / 4;
                }
                else if (punctuation == (step < 0 ? FullwidthPunctuation.Closing : FullwidthPunctuation.Opening))
                {
                    result += advance / 2;
                }

                return result;
            }
        }

        return result;
    }

    /// <summary>Records the slot of the empty base of a gap leaf (see <see cref="LineLeafKind.RubyGap"/>).</summary>
    private void OnRubyGapPlaced(in LineLeaf leaf, double start, double end)
    {
        var column = _rubyColumns[Items[leaf.Item].RubyColumn - 1];
        for (var b = column.FirstBase; b < column.FirstBase + column.BaseCount; b++)
        {
            if (_rubyBases[b].Box is null)
            {
                _rubyBaseStartX[b] = start;
                _rubyBaseEndX[b] = end;
                return;
            }
        }
    }

    /// <summary>Sets the inline size of the gap leaves of the line (the slots of the empty bases).</summary>
    private void SetRubyGapWidths()
    {
        foreach (ref var leaf in CollectionsMarshal.AsSpan(_leaves))
        {
            if (leaf.Kind == LineLeafKind.RubyGap)
            {
                leaf.Width = GetRubyGapWidth(Items[leaf.Item].RubyColumn - 1);
            }
        }
    }

    /// <summary>The inline size of the gap leaf of a column whose base is empty: the slot of that base.</summary>
    private double GetRubyGapWidth(int columnIndex)
    {
        var column = _rubyColumns[columnIndex];
        return column.BaseCount == 1 && _rubyBases[column.FirstBase].Box is null ? _rubySlotWidths[column.FirstBase] : 0;
    }

    // ----------------------------------------------------------------------------------------------------------------
    // Line box: block axis
    // ----------------------------------------------------------------------------------------------------------------

    /// <summary>The block extent (from the line-over edge of the line box) of the fragments of the children of a node, or null.</summary>
    private (double Start, double Size)? GetChildrenBlockExtent(in LineNode node)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        var start = double.PositiveInfinity;
        var end = double.NegativeInfinity;
        for (var child = node.FirstChild; child >= 0; child = nodes[child].NextSibling)
        {
            start = Math.Min(start, nodes[child].BlockStart);
            end = Math.Max(end, nodes[child].BlockStart + nodes[child].BlockSize);
        }

        return double.IsInfinity(start) ? null : (start, end - start);
    }

    /// <summary>Whether a box is a ruby annotation of a column (the root of an aligned subtree placed over or under its bases).</summary>
    private bool IsRubyAnnotationBox(ContainerBox box) => _rubyBoxes.TryGetValue(box, out var index) && index < 0;

    /// <summary>
    /// Places the annotations of the ruby columns of the line in the block axis (https://drafts.csswg.org/css-ruby-1/#rubypos):
    /// the first level over (under) the bases is placed against the over (under) edge of the content of the bases of its column
    /// (see <see cref="GetRubyBaseExtent"/>), the next levels outward. <see cref="LineNode.Baseline"/> of the annotation boxes is set (from the
    /// top of the line box); the other nodes are already placed.
    /// </summary>
    private void PlaceRubyAnnotations(double rootBaseline)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        _rubyRootNodes.Clear();
        for (var i = 0; i < nodes.Length; i++)
        {
            if (nodes[i].IsRubyAnnotation)
            {
                _rubyRootNodes.Add(i);
                SetRubyAnnotationExtent(i);
            }
        }

        for (var r = 0; r < _rubyRootNodes.Count; r++)
        {
            var columnIndex = _rubyAnnotations[~_rubyBoxes[nodes[_rubyRootNodes[r]].Box!]].Column;

            // Each column once (its first annotation on the line)
            var seen = false;
            for (var p = 0; p < r && !seen; p++)
            {
                seen = _rubyAnnotations[~_rubyBoxes[nodes[_rubyRootNodes[p]].Box!]].Column == columnIndex;
            }

            if (seen)
                continue;

            var (over, under) = GetRubyBaseExtent(columnIndex, rootBaseline);
            for (var level = 0; ; level++)
            {
                var found = false;
                var isOver = true;
                var height = 0d;
                for (var p = r; p < _rubyRootNodes.Count; p++)
                {
                    ref readonly var node = ref nodes[_rubyRootNodes[p]];
                    var info = _rubyAnnotations[~_rubyBoxes[node.Box!]];
                    if (info.Column != columnIndex || info.Level != level)
                        continue;

                    found = true;
                    isOver = info.IsOver;
                    height = Math.Max(height, node.GroupBottom - node.GroupTop);
                }

                if (!found)
                {
                    // The levels of a column are consecutive, but a level may have no annotation in this column
                    if (!HasRubyLevelAfter(columnIndex, level))
                        break;

                    continue;
                }

                for (var p = r; p < _rubyRootNodes.Count; p++)
                {
                    ref var node = ref nodes[_rubyRootNodes[p]];
                    var info = _rubyAnnotations[~_rubyBoxes[node.Box!]];
                    if (info.Column != columnIndex || info.Level != level)
                        continue;

                    node.Baseline = isOver ? over - node.GroupBottom : under - node.GroupTop;
                }

                if (isOver)
                {
                    over -= height;
                }
                else
                {
                    under += height;
                }
            }
        }

        bool HasRubyLevelAfter(int columnIndex, int level)
        {
            var column = _rubyColumns[columnIndex];
            for (var a = column.FirstAnnotation; a < column.FirstAnnotation + column.AnnotationCount; a++)
            {
                if (_rubyAnnotations[a].Level > level)
                    return true;
            }

            return false;
        }
    }

    /// <summary>
    /// Sets the block extent of an annotation (<see cref="LineNode.GroupTop"/>, <see cref="LineNode.GroupBottom"/>, relative to its
    /// baseline) to the one of its content: the content areas of its texts and the margin boxes of its atomic inlines (not the line
    /// height of its boxes), or the content area of the annotation when it is empty, and its block-axis margins, borders and paddings.
    /// </summary>
    private void SetRubyAnnotationExtent(int root)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        var over = double.PositiveInfinity;
        var under = double.NegativeInfinity;
        for (var i = root + 1; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            if (node.AlignRoot != root || node.Leaf < 0)
                continue;

            switch (_leaves[node.Leaf].Kind)
            {
                case LineLeafKind.Text or LineLeafKind.Hyphen:
                    over = Math.Min(over, node.Baseline - node.Metrics.Ascent);
                    under = Math.Max(under, node.Baseline + node.Metrics.Descent);
                    break;

                case LineLeafKind.Atomic:
                    over = Math.Min(over, node.Baseline - node.Ascent);
                    under = Math.Max(under, node.Baseline + node.Descent);
                    break;
            }
        }

        ref var annotation = ref nodes[root];
        if (double.IsInfinity(over))
        {
            (over, under) = (-annotation.Metrics.Ascent, annotation.Metrics.Descent);
        }

        // The margins, borders and paddings of the annotation box in the block axis separate it from its bases and from the other
        // levels (https://drafts.csswg.org/css-ruby-1/#interlinear-block)
        var layout = annotation.Box!.Style.Layout;
        if (layout.MayHaveBoxEdges)
        {
            var lineOver = _writingDirection.LineOver;
            over = Math.Min(over, -annotation.Metrics.Ascent - GetBlockEdges(layout.Box, lineOver));
            under = Math.Max(under, annotation.Metrics.Descent + GetBlockEdges(layout.Box, Opposite(lineOver)));
        }

        annotation.GroupTop = over;
        annotation.GroupBottom = under;
    }

    /// <summary>The margin, border and padding of a side of a box (the percentages refer to the inline size of the containing block).</summary>
    private double GetBlockEdges(BoxModelStyle box, PhysicalSide side)
        => box.Margin.Get(side).Resolve(_percentageBasis) + box.BorderWidth.Get(side) + Math.Max(0, box.Padding.Get(side).Resolve(_percentageBasis));

    /// <summary>
    /// Returns the over and under edges (from the top of the line box) of the content of the bases of a ruby column: the content areas
    /// of their texts, the margin boxes of their atomic inlines, the annotations of the ruby containers in them, or the content area
    /// of the base box (of the ruby container for an anonymous empty base) when its bases are empty.
    /// </summary>
    private (double Over, double Under) GetRubyBaseExtent(int columnIndex, double rootBaseline)
    {
        var column = _rubyColumns[columnIndex];
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        var over = double.PositiveInfinity;
        var under = double.NegativeInfinity;
        var rubyNode = -1;
        var baseNode = -1;
        for (var i = 0; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            if (node.Leaf < 0)
            {
                if (rubyNode < 0 && ReferenceEquals(node.Box, column.Ruby))
                {
                    rubyNode = i;
                }

                if (baseNode < 0 && _rubyBoxes.TryGetValue(node.Box!, out var baseIndex) && baseIndex >= column.FirstBase && baseIndex < column.FirstBase + column.BaseCount)
                {
                    baseNode = i;
                }

                // The annotations of a ruby in the bases (placed before the annotations of this column): the annotations of this
                // column are outside of them
                if (node.IsRubyAnnotation && _rubyAnnotations[~_rubyBoxes[node.Box!]] is var nested && nested.StartItem > column.MarkerItem && nested.EndItem <= column.AnnotationStartItem)
                {
                    over = Math.Min(over, node.Baseline + node.GroupTop);
                    under = Math.Max(under, node.Baseline + node.GroupBottom);
                }

                continue;
            }

            var leaf = _leaves[node.Leaf];
            if (leaf.Item <= column.MarkerItem || leaf.Item >= column.AnnotationStartItem || Items[leaf.Item].HasFlag(InlineItemFlags.RubyAnnotation))
                continue;

            switch (leaf.Kind)
            {
                case LineLeafKind.Text or LineLeafKind.Hyphen:
                    over = Math.Min(over, node.Baseline - node.Metrics.Ascent);
                    under = Math.Max(under, node.Baseline + node.Metrics.Descent);
                    break;

                case LineLeafKind.Atomic:
                    over = Math.Min(over, node.Baseline - node.Ascent);
                    under = Math.Max(under, node.Baseline + node.Descent);
                    break;
            }
        }

        if (!double.IsInfinity(over))
            return (over, under);

        // Empty bases: the content area of the first base box, else of the ruby container
        var fallback = baseNode >= 0 ? baseNode : rubyNode;
        var metrics = fallback >= 0 ? nodes[fallback].Metrics : GetMetrics(column.Ruby.Style);
        var baseline = fallback >= 0 ? nodes[fallback].Baseline : rootBaseline;
        return (baseline - metrics.Ascent, baseline + metrics.Descent);
    }

    /// <summary>
    /// The space of the line box taken by the annotations (https://drafts.csswg.org/css-ruby-1/#line-height): "ruby annotation
    /// containers do not contribute to measured line height", but, as in Blink, the line box is extended when the annotations
    /// overflow it, unless the half-leading of the previous line (of the next line, for the annotations under the last line)
    /// leaves room for them. Measures the over (under) side of the content of the line (the content areas of the texts, the margin boxes
    /// of the atomic inlines, the annotations), moves the content of the line toward its block-end side when annotations overflow
    /// its block-start side, and returns the space at its block-end side (negative: the overflow of the annotations).
    /// </summary>
    private double ApplyRubyAnnotationOverflow(ref double blockSize, ref double baseline, bool isFirstLine)
    {
        var nodes = CollectionsMarshal.AsSpan(_nodes);
        var contentOver = baseline;
        var contentUnder = baseline;
        var hasOver = false;
        var hasUnder = false;
        var hasOverEmphasis = false;
        var hasUnderEmphasis = false;
        for (var i = 0; i < nodes.Length; i++)
        {
            ref readonly var node = ref nodes[i];
            if (node.IsRubyAnnotation)
            {
                var info = _rubyAnnotations[~_rubyBoxes[node.Box!]];
                hasOver |= info.IsOver;
                hasUnder |= !info.IsOver;
                contentOver = Math.Min(contentOver, node.Baseline + node.GroupTop);
                contentUnder = Math.Max(contentUnder, node.Baseline + node.GroupBottom);
                continue;
            }

            if (node.Leaf < 0 || (node.AlignRoot >= 0 && nodes[node.AlignRoot].IsRubyAnnotation))
                continue;

            var leaf = _leaves[node.Leaf];
            switch (leaf.Kind)
            {
                case LineLeafKind.Text or LineLeafKind.Hyphen:
                    contentOver = Math.Min(contentOver, node.Baseline - node.Metrics.Ascent);
                    contentUnder = Math.Max(contentUnder, node.Baseline + node.Metrics.Descent);

                    // The emphasis marks use the space between the content and the edges of the line box
                    var emphasis = GetLineStyle(Items[leaf.Item], isFirstLine).Layout.Text.EmphasisMarks;
                    hasOverEmphasis |= emphasis == EmphasisMarksSide.Over;
                    hasUnderEmphasis |= emphasis == EmphasisMarksSide.Under;
                    break;

                case LineLeafKind.Atomic:
                    contentOver = Math.Min(contentOver, node.Baseline - node.Ascent);
                    contentUnder = Math.Max(contentUnder, node.Baseline + node.Descent);
                    break;
            }
        }

        // A line without content: the content is the font size of the line, centered in the line box
        var fontSize = GetLineStyle(_block, isFirstLine).Layout.FontSize;
        if (contentUnder - contentOver < fontSize)
        {
            var halfLeading = Math.Max(0, (blockSize - fontSize) / 2);
            contentOver = halfLeading;
            contentUnder = blockSize - halfLeading;
        }

        // "Don't provide annotation space if text-emphasis exists" (Blink)
        if (hasOverEmphasis)
        {
            contentOver = Math.Min(contentOver, 0);
        }

        if (hasUnderEmphasis)
        {
            contentUnder = Math.Max(contentUnder, blockSize);
        }

        // The content that is not an annotation may exceed the line box: only the annotations extend it
        if (contentOver < 0 && !hasOver)
        {
            contentOver = 0;
        }

        if (contentUnder > blockSize && !hasUnder)
        {
            contentUnder = blockSize;
        }

        var overflowOver = Math.Max(0, -contentOver);
        var overflowUnder = Math.Max(0, contentUnder - blockSize);
        var spaceOver = Math.Max(0, contentOver);
        var spaceUnder = Math.Max(0, blockSize - contentUnder);

        // The block-start side is the line-under side in vertical-lr
        var flipped = _writingDirection.IsLineOverBlockEnd;
        var (overflowStart, overflowEnd) = flipped ? (overflowUnder, overflowOver) : (overflowOver, overflowUnder);
        var (spaceStart, spaceEnd) = flipped ? (spaceUnder, spaceOver) : (spaceOver, spaceUnder);

        // The annotations of the previous line overflow it (the line is placed after them): this line moves back into the space
        // before its content, unless its own annotations overflow it. Else, the annotations that overflow the block-start side of
        // this line use the space after the content of the previous line
        var shift = overflowStart;
        if (_rubyBlockEndSpace < 0 && overflowStart <= 0)
        {
            shift = -Math.Min(spaceStart, -_rubyBlockEndSpace);
        }
        else if (_rubyBlockEndSpace > 0)
        {
            shift = Math.Max(0, overflowStart - _rubyBlockEndSpace);
        }

        if (shift != 0)
        {
            blockSize += shift;
            if (!flipped)
            {
                baseline += shift;
                foreach (ref var node in nodes)
                {
                    node.Baseline += shift;
                }
            }
        }

        return overflowEnd > 0 ? -overflowEnd : spaceEnd;
    }
}
