using System.Diagnostics;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Cascade;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The construction of a line box from the items of a line (https://drafts.csswg.org/css-inline-3/#line-boxes): the white
// space at the end of the line (phase II), the bidi reordering (rules L1 and L2 of UAX #9), the fragments of the inline boxes
// with their edges, the alignment in the inline axis (text-align, text-indent) and in the block axis (vertical-align and the
// line height calculation of https://drafts.csswg.org/css2/#line-height).
internal sealed partial class InlineLayoutAlgorithm
{
    private readonly List<LineLeaf> _leaves = [];
    private readonly List<LineLeaf> _splitLeaves = [];
    private readonly List<LineNode> _nodes = [];
    private readonly List<ContainerBox> _chain = [];
    private readonly List<int> _openNodes = [];
    private readonly List<ContainerBox> _lineBoxes = [];
    private readonly List<(ContainerBox Box, int LeafCount)> _openedOnLine = [];
    private readonly List<ContainerBox> _openTags = [];
    private readonly List<ContainerBox> _closeTags = [];
    private readonly Dictionary<ContainerBox, int> _fragmentCounts = new(ReferenceEqualityComparer.Instance);
    /// <summary>The out-of-flow items of the line being built: their inline offset on the line, and whether in-flow content precedes them on the line.</summary>
    private readonly List<(int Item, double InlineOffset, bool AfterContent, byte Level)> _outOfFlowPositions = [];
    private readonly FragmentBuilder _childrenBuilder = new(WritingDirection.HorizontalLtr);
    private int[] _visualOrder = [];
    private byte[] _leafLevels = [];
    private int[] _sequenceLeaves = [];
    private int[] _sequenceOrder = [];
    private byte[] _lineLevels = [];
    private Fragment?[] _nodeFragments = [];

    /// <summary>Whether an inline box that starts or ends on the line has margins, borders or paddings in the inline axis (the line is not empty).</summary>
    private bool _lineHasEdges;

    /// <summary>The space distributed to the content of the line by its justification (see <see cref="AlignLine"/>).</summary>
    private double _justifiedSpace;

    /// <summary>The kinds of <see cref="LineLeaf"/>.</summary>
    private enum LineLeafKind : byte
    {
        Text,
        Atomic,
        OutOfFlow,

        /// <summary>The line break of a <c>br</c> element: a text fragment without glyph.</summary>
        LineBreak,

        /// <summary>An inline box with no content on the line: a box fragment without children.</summary>
        EmptyBox,

        /// <summary>The hyphen displayed at the end of a line broken after a soft hyphen.</summary>
        Hyphen,

        /// <summary>The slot of an anonymous empty ruby base (https://drafts.csswg.org/css-ruby-1/#pairing): space without fragment.</summary>
        RubyGap,
    }

    /// <summary>A leaf of the content of a line: a part of a text item with one bidi level, an atomic inline, an empty inline box...</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct LineLeaf
    {
        public LineLeafKind Kind;
        public byte Level;
        public int Item;

        /// <summary>The range of the text of a text leaf.</summary>
        public int Start;
        public int End;

        /// <summary>The start of the spaces that hang at the end of the line (<see cref="End"/> when there are none).</summary>
        public int HangStart;

        /// <summary>The box that contains the leaf (an inline box or the block container).</summary>
        public ContainerBox Parent;

        /// <summary>The inline box of an <see cref="LineLeafKind.EmptyBox"/>.</summary>
        public ContainerBox? Box;

        public double Width;

        public ShapedText? Shaped;
        public int GlyphStart;
        public int GlyphCount;

        /// <summary>The index + 1 of the outermost ruby column that contains the leaf, or 0.</summary>
        public int RubyColumn;

        /// <summary>Whether the leaf is in the annotations of its outermost ruby column (<see cref="RubyColumn"/>).</summary>
        public bool IsRubyAnnotation;
    }

    /// <summary>A fragment of a line being built: a leaf, or a fragment of an inline box whose children are the following nodes.</summary>
    [StructLayout(LayoutKind.Auto)]
    private struct LineNode
    {
        /// <summary>The leaf of the node, or -1 for the fragment of an inline box.</summary>
        public int Leaf;
        public ContainerBox? Box;
        public int Parent;
        public int FirstChild;
        public int LastChild;
        public int NextSibling;

        /// <summary>The line-left edge of the border box, from the line-left edge of the line box.</summary>
        public double X;
        public double Width;
        public double MarginLeft;
        public double MarginRight;
        public double BorderPaddingLeft;
        public double BorderPaddingRight;
        public bool HasLeftEdge;
        public bool HasRightEdge;

        /// <summary>Whether the fragment has the inline-start (inline-end) edges of its box, in the direction of the box.</summary>
        public bool HasStartEdge;
        public bool HasEndEdge;

        /// <summary>The metrics of the first available font of the box (https://drafts.csswg.org/css-inline-3/#inline-box-dimensions).</summary>
        public InlineMetrics Metrics;

        /// <summary>The distance between the baseline and the top (bottom) of the layout bounds of the box (with the half-leading), or of the margin box of an atomic inline.</summary>
        public double Ascent;
        public double Descent;

        /// <summary>Whether the box contributes to the height of the line.</summary>
        public bool Contributes;

        /// <summary>Whether the node is a ruby annotation: the root of an aligned subtree placed over or under its bases (see <see cref="PlaceRubyAnnotations"/>).</summary>
        public bool IsRubyAnnotation;

        /// <summary>Whether a text of the line is directly in the box (the line height calculation quirk; a forced line break is not a text).</summary>
        public bool HasDirectText;

        /// <summary>The alignment of the box relative to the line box (<c>vertical-align: top | bottom | center</c>), or <see cref="BaselineShiftKind.Length"/>.</summary>
        public BaselineShiftKind LineRelative;

        /// <summary>The position of the baseline relative to the baseline of the parent (positive downward), then to the baseline of the aligned subtree.</summary>
        public double Baseline;

        /// <summary>The node whose aligned subtree contains the node (https://drafts.csswg.org/css2/#aligned-subtree), -1 for the root inline box.</summary>
        public int AlignRoot;

        /// <summary>The extent of the aligned subtree of a <c>top</c>/<c>bottom</c> aligned node, relative to its baseline.</summary>
        public double GroupTop;
        public double GroupBottom;

        /// <summary>
        /// The logical rectangle of the node in the line box: <see cref="BlockStart"/> is the distance of its line-over edge from
        /// the line-over edge of the line box (the block-end edge in <c>vertical-lr</c>, see <see cref="BuildChildren"/>).
        /// </summary>
        public double InlineStart;
        public double BlockStart;
        public double BlockSize;
    }

    /// <summary>
    /// The metrics of the first available font of a style and its line height. The baseline of a box (see <see cref="LineNode.Baseline"/>)
    /// is the baseline of its writing mode: the alphabetic baseline, where the origin of the glyphs is, or the central baseline of
    /// vertical text (see <see cref="LayoutStyle.HasCentralDominantBaseline"/>). The boxes are aligned with their parent by another
    /// baseline of their baseline sets when <c>dominant-baseline</c> or <c>alignment-baseline</c> say so (see <see cref="GetBaseline"/>).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct InlineMetrics(double Ascent, double Descent, double LineHeight, double XHeight, double FontSize)
    {
        /// <summary>The cap height (https://drafts.csswg.org/css-inline-3/#cap-height).</summary>
        public double CapHeight { get; init; }

        /// <summary>The em size of the first available font.</summary>
        public double EmSize { get; init; }

        /// <summary>Whether the baseline is the central baseline (<see cref="Ascent"/> and <see cref="Descent"/> are measured from it).</summary>
        public bool IsCentral { get; init; }

        /// <summary>The distance of the origin of the glyphs below the baseline: 0, or the distance between the central baseline and the origin.</summary>
        public double OriginOffset { get; init; }

        /// <summary>The baselines of the first available font, above the origin of the glyphs (https://drafts.csswg.org/css-inline-3/#baseline-types).</summary>
        public UsedFontBaselines Baselines { get; init; }

        /// <summary>The dominant baseline of the box (see <see cref="LayoutStyle.DominantBaseline"/>): the baseline its children are aligned by, by default.</summary>
        public AlignmentBaseline DominantBaseline { get; init; }

        /// <summary>
        /// Returns the distance of a baseline of the baseline set of the box (https://drafts.csswg.org/css-inline-3/#baseline-set)
        /// above its baseline: "for an inline box, it derives from its first available font". As in the browsers (and the tests
        /// WPT css/css-writing-modes/vertical-alignment-vrl-022.xht and following), the middle baseline of a box on the central
        /// baseline is half its x-height above the central baseline, not its x-middle baseline.
        /// </summary>
        public double GetBaseline(AlignmentBaseline baseline) => baseline switch
        {
            AlignmentBaseline.Alphabetic => Baselines.Alphabetic - OriginOffset,
            AlignmentBaseline.Central => IsCentral ? 0 : Baselines.Central - OriginOffset,
            AlignmentBaseline.Ideographic => Baselines.IdeographicUnder - OriginOffset,
            AlignmentBaseline.Mathematical => Baselines.Mathematical - OriginOffset,
            AlignmentBaseline.Hanging => Baselines.Hanging - OriginOffset,
            AlignmentBaseline.Middle => IsCentral ? XHeight / 2 : Baselines.XMiddle - OriginOffset,
            AlignmentBaseline.TextTop => Ascent,
            AlignmentBaseline.TextBottom => -Descent,
            _ => 0,
        };

        /// <summary>
        /// The distance from the baseline to the over edge of a metric of <c>text-box-edge</c>
        /// (https://drafts.csswg.org/css-inline-3/#typedef-text-edge): the cap height and the x-height are above the origin of
        /// the glyphs, the ideographic metrics are the ideographic-over and ideographic-ink-over baselines of the font (the
        /// <c>idtp</c> and <c>icft</c> baselines of its <c>BASE</c> table), or the em box centered between the ascent and the
        /// descent when the font has no ideographic baseline.
        /// </summary>
        public double GetOverEdge(TextEdgeMetric metric) => metric switch
        {
            TextEdgeMetric.Cap => CapHeight - OriginOffset,
            TextEdgeMetric.Ex => XHeight - OriginOffset,
            TextEdgeMetric.Ideographic when Baselines.HasIdeographic => Baselines.IdeographicOver - OriginOffset,
            TextEdgeMetric.IdeographicInk when Baselines.HasIdeographic => Baselines.IdeographicInkOver - OriginOffset,
            TextEdgeMetric.Ideographic or TextEdgeMetric.IdeographicInk => ((Ascent - Descent) / 2) + (EmSize / 2),
            _ => Ascent,
        };

        /// <summary>
        /// The distance from the baseline to the under edge of a metric of <c>text-box-edge</c> (positive below the baseline):
        /// the alphabetic baseline (the <c>romn</c> baseline of the <c>BASE</c> table, else the origin of the glyphs), the
        /// ideographic-under and ideographic-ink-under baselines (<c>ideo</c> and <c>icfb</c>).
        /// </summary>
        public double GetUnderEdge(TextEdgeMetric metric) => metric switch
        {
            TextEdgeMetric.Alphabetic => OriginOffset - Baselines.Alphabetic,
            TextEdgeMetric.Ideographic when Baselines.HasIdeographic => OriginOffset - Baselines.IdeographicUnder,
            TextEdgeMetric.IdeographicInk when Baselines.HasIdeographic => OriginOffset - Baselines.IdeographicInkUnder,
            TextEdgeMetric.Ideographic or TextEdgeMetric.IdeographicInk => (EmSize / 2) - ((Ascent - Descent) / 2),
            _ => Descent,
        };
    }

    /// <summary>
    /// A line box built by <see cref="BuildLine"/>. <see cref="RubyBlockEndSpace"/> is the space after its content at its block-end
    /// side, or (negative) the amount its ruby annotations overflow that side (see <see cref="ApplyRubyAnnotationOverflow"/>).
    /// </summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineBoxResult(LineBoxFragment? Fragment, double BlockSize, double Baseline, bool IsEmpty)
    {
        public double RubyBlockEndSpace { get; init; }

        /// <summary>The line scaling factor of the line (<c>text-fit</c>): the styles of the line have its used font sizes.</summary>
        public double FontScale { get; init; } = 1;
    }

    private ComputedStyle? _lastMetricsStyle;
    private InlineMetrics _lastMetrics;

    /// <summary>
    /// Returns the metrics of the first available font of a style (the consecutive boxes and texts often share a style): the
    /// ascent and the descent are measured from the baseline of the writing mode (see <see cref="LayoutStyle.HasCentralDominantBaseline"/>).
    /// </summary>
    private InlineMetrics GetMetrics(ComputedStyle style)
    {
        if (ReferenceEquals(style, _lastMetricsStyle))
            return _lastMetrics;

        _lastMetrics = CreateMetrics(style);
        _lastMetricsStyle = style;
        return _lastMetrics;
    }

    private static InlineMetrics CreateMetrics(ComputedStyle style)
    {
        var layout = style.Layout;
        var font = style.Fonts.PrimaryFont;
        var metrics = font.GetMetrics(layout.FontSize);
        var baselines = font.GetBaselines(metrics);
        var isCentral = layout.HasCentralDominantBaseline;
        var (ascent, descent) = GetContentArea(isCentral, metrics, baselines);
        return new InlineMetrics(ascent, descent, layout.UsedLineHeight, metrics.XHeight, layout.FontSize)
        {
            CapHeight = metrics.CapHeight,
            EmSize = metrics.EmSize,
            IsCentral = isCentral,
            OriginOffset = metrics.Ascent - ascent,
            Baselines = baselines,
            DominantBaseline = layout.DominantBaseline,
        };
    }

    /// <summary>
    /// Returns the ascent and the descent of the content area of a box (the ones of its first available font), measured from the
    /// baseline of its writing mode (see <see cref="LayoutStyle.HasCentralDominantBaseline"/>).
    /// </summary>
    internal static (double Ascent, double Descent) GetContentArea(ComputedStyle style)
    {
        var font = style.Fonts.PrimaryFont;
        var metrics = font.GetMetrics(style.Layout.FontSize);
        return GetContentArea(style.Layout.HasCentralDominantBaseline, metrics, font.GetBaselines(metrics));
    }

    private static (double Ascent, double Descent) GetContentArea(bool isCentral, in UsedFontMetrics metrics, in UsedFontBaselines baselines)
    {
        var (ascent, descent) = (metrics.Ascent, metrics.Descent);
        if (isCentral)
        {
            // https://drafts.csswg.org/css-inline-3/#dominant-baseline-property: the baseline of vertical text is the central
            // baseline, "halfway between the ideographic-under and ideographic-over baselines" of the font
            // (https://drafts.csswg.org/css-inline-3/#baseline-types). A font without ideographic baseline gets them from its ascent
            // and its descent (https://drafts.csswg.org/css-inline-3/#baseline-synthesis-fonts): the content area is centered on
            // the baseline (as in the browsers; Blink gives the extra pixel of an odd integer height to the ascent)
            if (baselines.HasIdeographic)
                return (ascent - baselines.Central, descent + baselines.Central);

            var height = ascent + descent;
            descent = height == Math.Floor(height) ? Math.Floor(height / 2) : height / 2;
            ascent = height - descent;
        }

        return (ascent, descent);
    }

    /// <summary>
    /// Returns the distance of the central baseline of a font above the origin of its glyphs: where the sideways glyphs of
    /// vertical text are drawn from the central baseline of their line (see <see cref="GetContentArea(ComputedStyle)"/>).
    /// </summary>
    internal static double GetCentralBaseline(MatchedFont font, in UsedFontMetrics metrics)
    {
        var baselines = font.GetBaselines(metrics);
        return baselines.HasIdeographic ? baselines.Central : (metrics.Ascent - metrics.Descent) / 2;
    }

    /// <summary>
    /// Returns the distance from the origin of the glyphs to the over edge of the content box of an inline box trimmed by
    /// <c>text-box-trim</c> in horizontal text (see <see cref="InlineMetrics.GetOverEdge"/>), for the painting of its decorations.
    /// </summary>
    internal static double GetTextBoxOverEdge(ComputedStyle style, TextEdgeMetric metric)
    {
        var font = style.Fonts.PrimaryFont;
        var metrics = font.GetMetrics(style.Layout.FontSize);
        var inlineMetrics = new InlineMetrics(metrics.Ascent, metrics.Descent, 0, metrics.XHeight, style.Layout.FontSize)
        {
            CapHeight = metrics.CapHeight,
            EmSize = metrics.EmSize,
            Baselines = font.GetBaselines(metrics),
        };
        return inlineMetrics.GetOverEdge(metric);
    }

    /// <summary>The content of a line, collected and measured by <see cref="CollectLineContent"/>.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct LineContent(bool IsRightToLeft, bool IsReordered, double Width, double HangWidth, double StartHang, double EndHang);

    /// <summary>
    /// Collects the leaves of the content from <paramref name="start"/> to <paramref name="end"/> and measures them with the
    /// used font sizes of a line scaling factor (<paramref name="fontScale"/>, which stays the scaling factor of the line being
    /// built): the inline size of the content (without what hangs), with the spacing at the edges of the line.
    /// </summary>
    private LineContent CollectLineContent(LinePosition start, in LineEnd end, double availableSize, bool isFirstLine, double fontScale)
    {
        ClearLineSpacing();
        var edges = CollectLeaves(start, end);
        TrimTrailingSpaces();
        _fontScale = fontScale;
        if (end.IsHyphenated)
        {
            AddHyphen(isFirstLine);
        }

        var isRightToLeft = _block.Style.Layout.Direction == Direction.Rtl;
        var reordered = false;
        if (_needsBidi)
        {
            var lineStart = start.Position;
            var lineEnd = Math.Max(lineStart, Math.Min(end.Position, _length));
            isRightToLeft = IsLineRightToLeft(Math.Min(lineStart, _length - 1));
            reordered = ResolveLineLevels(lineStart, lineEnd);
        }

        var contentWidth = MeasureLeaves(isFirstLine, out var hangWidth) + edges;

        // The ruby columns are as wide as their bases or their annotations, which are not after the bases on the line
        if (HasRuby)
        {
            contentWidth += LayoutRubyColumnsOnLine(start.Item, end.Item, isFirstLine);
            SetRubyGapWidths();
        }

        // The spacing at the edges of the line, the tab stops and the hanging punctuation
        var (startHang, endHang) = AdjustLineEdges(end, isFirstLine, availableSize, ref contentWidth, ref hangWidth);
        return new LineContent(isRightToLeft, reordered, contentWidth, hangWidth, startHang, endHang);
    }

    /// <summary>
    /// Builds the line box of the content from <paramref name="start"/> to <paramref name="end"/>, with the used font sizes of
    /// its line scaling factor when <c>text-fit</c> scales it (see <see cref="GetLineFontScale"/>).
    /// </summary>
    private LineBoxResult BuildLine(LinePosition start, in LineEnd end, LineOpportunity opportunity, double indent, bool isFirstLine)
    {
        try
        {
            return BuildScaledLine(start, end, opportunity, indent, isFirstLine);
        }
        finally
        {
            _fontScale = 1;
        }
    }

    private LineBoxResult BuildScaledLine(LinePosition start, in LineEnd end, LineOpportunity opportunity, double indent, bool isFirstLine)
    {
        // The leaves of the line, measured with the used font sizes of the line
        var content = CollectLineContent(start, end, opportunity.Size - indent, isFirstLine, GetLineFontScale(start, end, opportunity.Size - indent, isFirstLine));
        var (isRightToLeft, reordered, contentWidth, hangWidth, startHang, endHang) = content;

        // https://drafts.csswg.org/css-text-4/#white-space-phase-2: the preserved spaces at the end of a line followed by a forced
        // line break (or at the end of the content) conditionally hang: "any part of it that does not fit is considered to hang".
        // The white space before them (e.g. ideographic spaces of white-space: normal) hangs unconditionally, but only at the end
        // of the line: when a part of the preserved spaces fits, it is on the line too (WPT
        // css/css-text/white-space/hanging-whitespace-003.tentative.html); the preserved spaces followed by other white space
        // are not followed by the forced line break, they hang unconditionally (hanging-whitespace-002.tentative.html)
        if (hangWidth > 0 && (end.IsForced || end.Item >= _items.Count) && GetConditionalHangWidth() is var conditional and > 0)
        {
            var unconditional = hangWidth - conditional;
            var fitting = opportunity.Size - indent - contentWidth - unconditional;
            if (fitting > (unconditional > LayoutEpsilon ? LayoutEpsilon : 0))
            {
                fitting = Math.Min(fitting, conditional) + unconditional;
                contentWidth += fitting;
                hangWidth -= fitting;
            }
        }

        // https://drafts.csswg.org/css2/#inline-formatting: "Line boxes that contain no text, no preserved white space, no
        // inline elements with non-zero margins, padding, or borders or other in-flow content (such as images, inline blocks
        // or inline tables), and do not end with a preserved newline must be treated as zero-height line boxes"
        var isEmpty = !end.IsForced && !HasLineContent() && !_lineHasEdges;

        // https://drafts.csswg.org/css-overflow-4/#block-ellipsis: the block overflow ellipsis is after the content, in the
        // direction of the paragraph of the line (it is aligned with the content); the line has the strut of its root inline box
        var ellipsis = _blockEllipsis;
        if (ellipsis is not null && isRightToLeft != (_block.Style.Layout.Direction == Direction.Rtl))
        {
            ellipsis = LineEllipsis.Create(_block, _block.Style, _blockEllipsisText, isRightToLeft);
        }

        var ellipsisWidth = ellipsis?.InlineSize ?? 0;
        var ellipsisHang = ellipsisWidth - GetBlockEllipsisReservedSize(ellipsis);
        _justifiedSpace = 0;
        var shift = AlignLine(end, opportunity.Size, indent, contentWidth + ellipsisWidth - ellipsisHang, hangWidth + endHang + ellipsisHang, startHang, isRightToLeft);
        ApplyLineSpacing();
        PlaceLeaves(reordered);
        var contentShift = ellipsis is not null && isRightToLeft ? shift + ellipsisWidth : shift;
        foreach (ref var node in CollectionsMarshal.AsSpan(_nodes))
        {
            node.X += contentShift;
        }

        if (ellipsis is not null)
        {
            isEmpty = false;
            _rootHasText = true;
        }

        // https://quirks.spec.whatwg.org/#the-line-height-calculation-quirk: a box that contains preserved white space is not
        // subject to the quirk, as in the browsers: the line of a preserved segment break (e.g. an empty line of a pre element)
        // has the height of the strut of the box of the segment break
        if (end.IsForced && _context.UsesLineHeightQuirks && GetEndingSegmentBreak(end) is { } segmentBreak)
        {
            MarkPreservedSegmentBreak(segmentBreak);
        }

        // The block alignment is computed from the line-over edge; the baseline of the line box is from its block-start edge
        var (blockSize, baseline) = AlignVertically(isFirstLine, isEmpty);

        // https://drafts.csswg.org/css-ruby-1/#line-height: the line box is extended by the ruby annotations that overflow it
        var rubyBlockEndSpace = 0d;
        if (HasRuby && !isEmpty)
        {
            rubyBlockEndSpace = ApplyRubyAnnotationOverflow(ref blockSize, ref baseline, isFirstLine);
        }

        if (_writingDirection.IsLineOverBlockEnd)
        {
            baseline = blockSize - baseline;
        }

        var fragment = CreateFragments(opportunity.Size, blockSize, baseline, isFirstLine, isEmpty, Math.Max(0, -rubyBlockEndSpace));
        if (fragment is not null)
        {
            // For the ellipsis of text-overflow, which is painted in the style of the root inline box and isolated in the direction of the paragraph
            fragment.Style = GetLineStyle(_block, isFirstLine);
            fragment.IsRightToLeft = isRightToLeft;

            // https://drafts.csswg.org/css-text-4/#hanging: "An overflowing hanging glyph should typically be considered ink
            // overflow so as to avoid creating unnecessary scrollbars, but the UA may treat it as scrollable overflow": the white
            // space that hangs at the end of the line (line-right, or line-left for a right-to-left line) is outside of the
            // rectangle that the scrollable overflow of the line is clipped to; the punctuation that hangs is scrollable
            // overflow (WPT css/css-text/hanging-punctuation/hanging-scrollable-001.html)
            if (hangWidth > LayoutEpsilon)
            {
                const double Unbounded = 1e9;
                var lineLeftEdge = isRightToLeft ? contentShift + hangWidth : -Unbounded;
                var lineRightEdge = isRightToLeft ? Unbounded : contentShift + startHang + contentWidth + _justifiedSpace + ellipsisWidth + endHang;
                var inlineStart = _writingDirection.IsRightToLeft ? opportunity.Size - lineRightEdge : lineLeftEdge;
                var lineSize = _writingDirection.ToPhysical(new LogicalSize(opportunity.Size, blockSize));
                fragment.NonHangingRect = _writingDirection.ToPhysical(new LogicalRect(new LogicalOffset(inlineStart, -Unbounded), new LogicalSize(lineRightEdge - lineLeftEdge, 2 * Unbounded + blockSize)), lineSize);
            }
            if (ellipsis is not null)
            {
                // "immediately adjacent to the end edge of the remaining inline content" (after the justification of the content);
                // the content of a line of -webkit-line-clamp that does not fit with the ellipsis is truncated when it is painted
                fragment.BlockEllipsis = ellipsis;
                if (_blockEllipsisIsLegacy && contentWidth + ellipsisWidth > opportunity.Size - indent + 0.01)
                {
                    fragment.IsTruncatedByBlockEllipsis = true;
                }
                else
                {
                    ellipsis.Place(fragment, isRightToLeft ? shift + hangWidth + endHang : shift + contentWidth + _justifiedSpace);
                }
            }
        }

        // The preserved segment break that ends the line
        if (fragment is not null && end.IsForced && GetEndingSegmentBreak(end) is { Box: TextRun run } endingSegmentBreak)
        {
            fragment.SetForcedBreak(run, endingSegmentBreak.DomStart, endingSegmentBreak.DomEnd);
        }

        return new LineBoxResult(fragment, blockSize, baseline, isEmpty) { RubyBlockEndSpace = rubyBlockEndSpace, FontScale = _fontScale };
    }

    /// <summary>Returns the preserved segment break that ends a line (not a <c>br</c>), or null.</summary>
    private InlineItem? GetEndingSegmentBreak(LineEnd end)
    {
        // The items after it on the line are the ends of the inline boxes
        for (var index = Math.Min(end.Item, _items.Count) - 1; index >= 0 && Items[index].Kind is InlineItemKind.ForcedBreak or InlineItemKind.CloseTag or InlineItemKind.Control; index--)
        {
            ref readonly var item = ref Items[index];
            if (item.Kind == InlineItemKind.ForcedBreak)
                return item.Box is TextRun { Kind: TextRunKind.Text } ? item : null;
        }

        return null;
    }

    /// <summary>
    /// Collects the leaves of the line in logical order, and the inline boxes with no content on the line (a box fragment
    /// without children). Returns the inline size of the margins, borders and paddings of the inline boxes that start or end
    /// on the line.
    /// </summary>
    private double CollectLeaves(LinePosition start, in LineEnd end)
    {
        _leaves.Clear();
        _openedOnLine.Clear();
        _openTags.Clear();
        _closeTags.Clear();
        _lineHasEdges = false;
        var items = Items;
        var edges = 0d;
        for (var index = start.Item; index < items.Length; index++)
        {
            ref readonly var item = ref items[index];
            if (index >= end.Item && (index > end.Item || end.Position <= item.Start))
                break;

            var itemStart = index == start.Item ? Math.Max(item.Start, start.Position) : item.Start;
            var itemEnd = index == end.Item ? Math.Min(item.End, end.Position) : item.End;
            var leafCount = _leaves.Count;
            switch (item.Kind)
            {
                case InlineItemKind.OpenTag:
                    var openedBox = (ContainerBox)item.Box;
                    _openTags.Add(openedBox);
                    _openedOnLine.Add((openedBox, _leaves.Count));
                    edges += item.InlineSize;
                    _lineHasEdges |= item.HasFlag(InlineItemFlags.HasEdges);
                    break;

                case InlineItemKind.CloseTag:
                    var closedBox = (ContainerBox)item.Box;
                    _closeTags.Add(closedBox);
                    edges += item.InlineSize;
                    _lineHasEdges |= item.HasFlag(InlineItemFlags.HasEdges);
                    if (_openedOnLine.Count > 0 && ReferenceEquals(_openedOnLine[^1].Box, closedBox))
                    {
                        if (_openedOnLine[^1].LeafCount == _leaves.Count)
                        {
                            AddEmptyBoxLeaf(closedBox, index);
                        }

                        _openedOnLine.RemoveAt(_openedOnLine.Count - 1);
                    }
                    else if (_leaves.Count == 0 || !IsInside(_leaves[^1], closedBox))
                    {
                        AddEmptyBoxLeaf(closedBox, index);
                    }

                    break;

                case InlineItemKind.Text:
                    itemStart = Math.Max(itemStart, end.ContentStart);
                    if (itemEnd > itemStart)
                    {
                        _leaves.Add(new LineLeaf { Kind = LineLeafKind.Text, Level = item.BidiLevel, Item = index, Start = itemStart, End = itemEnd, HangStart = itemEnd, Parent = item.Container });
                    }

                    break;

                case InlineItemKind.ForcedBreak when item.Box is TextRun { Kind: TextRunKind.LineBreak }:
                    _leaves.Add(new LineLeaf { Kind = LineLeafKind.LineBreak, Level = item.BidiLevel, Item = index, Start = item.Start, End = item.Start, HangStart = item.Start, Parent = item.Container });
                    break;

                // As in the browsers, the inline box of a preserved segment break that has no other content on the line has a
                // fragment on it (e.g. the empty line of a span with white-space: pre has the height of the strut of the span)
                case InlineItemKind.ForcedBreak when !ReferenceEquals(item.Container, _block) && (_leaves.Count == 0 || !IsInside(_leaves[^1], item.Container)):
                    AddEmptyBoxLeaf(item.Container, index);
                    break;

                case InlineItemKind.Atomic:
                    _leaves.Add(new LineLeaf { Kind = LineLeafKind.Atomic, Level = item.BidiLevel, Item = index, Start = item.Start, End = item.End, HangStart = item.End, Parent = item.Container, Width = item.InlineSize });
                    break;

                case InlineItemKind.OutOfFlow:
                    _leaves.Add(new LineLeaf { Kind = LineLeafKind.OutOfFlow, Level = item.BidiLevel, Item = index, Start = item.Start, End = item.Start, HangStart = item.Start, Parent = item.Container });
                    break;

                case InlineItemKind.RubyColumn:
                    // The slot of an empty base (its width is set by SetRubyGapWidths)
                    var column = _rubyColumns[item.RubyColumn - 1];
                    if (column.BaseCount == 1 && _rubyBases[column.FirstBase].Box is null)
                    {
                        _leaves.Add(new LineLeaf { Kind = LineLeafKind.RubyGap, Level = item.BidiLevel, Item = index, Start = item.Start, End = item.Start, HangStart = item.Start, Parent = item.Container });
                    }

                    break;
            }

            // The leaves of the ruby columns know their column
            if (item.RubyColumn > 0 && _leaves.Count > leafCount)
            {
                for (var i = leafCount; i < _leaves.Count; i++)
                {
                    SetRubyLeafInfo(ref CollectionsMarshal.AsSpan(_leaves)[i], index);
                }
            }
        }

        // The inline boxes that start on the line and continue on the next one without content on this line
        for (var i = _openedOnLine.Count - 1; i >= 0; i--)
        {
            if (_openedOnLine[i].LeafCount == _leaves.Count)
            {
                AddEmptyBoxLeaf(_openedOnLine[i].Box, -1);
            }
        }

        return edges;

        void AddEmptyBoxLeaf(ContainerBox box, int item) => _leaves.Add(new LineLeaf { Kind = LineLeafKind.EmptyBox, Item = item, Parent = box.Parent!, Box = box, Level = item >= 0 ? Items[item].BidiLevel : (byte)0 });
    }

    /// <summary>Whether a leaf is a descendant of a box.</summary>
    private bool IsInside(in LineLeaf leaf, ContainerBox box)
    {
        for (var parent = leaf.Kind == LineLeafKind.EmptyBox ? leaf.Box : leaf.Parent; parent is not null && !ReferenceEquals(parent, _block); parent = parent.Parent)
        {
            if (ReferenceEquals(parent, box))
                return true;
        }

        return false;
    }

    /// <summary>
    /// https://drafts.csswg.org/css-text-4/#white-space-phase-2: "A sequence of collapsible spaces at the end of a line is
    /// removed", and the preserved spaces at the end of a line that wraps hang (they are displayed, but do not count for the
    /// alignment and for the fitting of the line).
    /// </summary>
    private void TrimTrailingSpaces()
    {
        var leaves = CollectionsMarshal.AsSpan(_leaves);
        var removed = false;
        var removing = true;
        var rubyColumn = 0;
        _rubyTrimmedColumn = -1;
        for (var i = leaves.Length - 1; i >= 0; i--)
        {
            ref var leaf = ref leaves[i];
            if (leaf.Kind is LineLeafKind.OutOfFlow or LineLeafKind.EmptyBox or LineLeafKind.LineBreak)
                continue;

            // The collapsible spaces at the end of the bases of a ruby column that ends the line are removed too (its annotations
            // are after its bases), as in Blink (WPT css/css-ruby/collapse-trailing-whitespace.html)
            if (leaf.RubyColumn > 0)
            {
                if (rubyColumn != 0 && rubyColumn != leaf.RubyColumn)
                    break;

                rubyColumn = leaf.RubyColumn;
                if (leaf.IsRubyAnnotation)
                    continue;

                if (leaf.Kind != LineLeafKind.Text || Items[leaf.Item].HasFlag(InlineItemFlags.RubyAnnotation) || !Items[leaf.Item].HasFlag(InlineItemFlags.CollapsesSpaces) || !removing)
                    break;
            }

            if (leaf.Kind != LineLeafKind.Text)
                break;

            ref readonly var item = ref Items[leaf.Item];
            var collapses = item.HasFlag(InlineItemFlags.CollapsesSpaces);
            if (!collapses && !item.HasFlag(InlineItemFlags.HangsSpaces))
                break;

            // The collapsible spaces at the end of the line are removed ("as well as any trailing U+1680 OGHAM SPACE MARK"), the
            // other white space hangs. Before the block overflow ellipsis, the spaces that would hang are removed too (WPT
            // css/css-overflow/line-clamp/block-ellipsis-032.html; the preserved spaces that do not hang stay, block-ellipsis-031.html)
            var end = leaf.End;
            if ((collapses || _blockEllipsis is not null) && removing)
            {
                while (end > leaf.Start && _text[end - 1] is ' ' or (char)0x1680)
                {
                    end--;
                }

                if (leaf.RubyColumn > 0 && end < leaf.End)
                {
                    _rubyTrimmedColumn = leaf.RubyColumn - 1;
                    _rubyTrimmedPosition = end;
                }

                leaf.End = end;
                removed |= end == leaf.Start;
            }

            removing &= end == leaf.Start;
            while (end > leaf.Start && IsSpace(_text[end - 1]))
            {
                end--;
            }

            leaf.HangStart = end;
            if (end > leaf.Start)
                break;
        }

        if (removed)
        {
            // An inline box whose content is only removed spaces is still on the line, as an empty box (as in the browsers)
            for (var i = _leaves.Count - 1; i >= 0; i--)
            {
                var leaf = _leaves[i];
                if (leaf.Kind != LineLeafKind.Text || leaf.End != leaf.Start)
                    continue;

                if (leaf.Parent is { IsInlineBox: true } box && !ReferenceEquals(box, _block) && !HasOtherLeaf(box, i))
                {
                    _leaves[i] = new LineLeaf { Kind = LineLeafKind.EmptyBox, Item = leaf.Item, Start = leaf.Start, End = leaf.Start, HangStart = leaf.Start, Parent = box.Parent!, Box = box, Level = leaf.Level };
                }
                else
                {
                    _leaves.RemoveAt(i);
                }
            }
        }

        // Whether a leaf other than the removed text leaf at an index is inside a box
        bool HasOtherLeaf(ContainerBox box, int index)
        {
            for (var i = 0; i < _leaves.Count; i++)
            {
                var other = _leaves[i];
                if (i != index && !(other.Kind == LineLeafKind.Text && other.End == other.Start) && IsInside(other, box))
                    return true;
            }

            return false;
        }
    }

    /// <summary>Adds the hyphen of a line broken after a soft hyphen, at the end of the last text of the line.</summary>
    private void AddHyphen(bool isFirstLine)
    {
        for (var i = _leaves.Count - 1; i >= 0; i--)
        {
            var leaf = _leaves[i];
            if (leaf.Kind != LineLeafKind.Text)
                continue;

            var style = GetLineStyle(Items[leaf.Item], isFirstLine);

            // The spelling change of the language: the characters it replaces are not on the line
            var hyphenBreak = GetHyphenationBreak(leaf.End, style);
            if (hyphenBreak.RemovedBefore > 0 && leaf.End - leaf.Start > hyphenBreak.RemovedBefore)
            {
                var end = leaf.End - hyphenBreak.RemovedBefore;
                _leaves[i] = leaf with { End = end, HangStart = Math.Min(leaf.HangStart, end) };
            }

            // The hyphen is shaped in the direction of the text it follows (e.g. a no-break space and a tatweel after an
            // Arabic word are in the right-to-left order)
            var hyphen = TextShaper.Shape(hyphenBreak.Hyphen, style.Fonts, GetHyphenShapingOptions(style) with { IsRightToLeft = (leaf.Level & 1) != 0 });
            _leaves.Insert(i + 1, new LineLeaf { Kind = LineLeafKind.Hyphen, Level = leaf.Level, Item = leaf.Item, Start = leaf.End, End = leaf.End, HangStart = leaf.End, Parent = leaf.Parent, Shaped = hyphen, GlyphCount = hyphen.Glyphs.Length, Width = hyphen.Width });
            return;
        }
    }

    /// <summary>
    /// Returns the number of spaces at the end of a line that are preserved by <c>white-space: break-spaces</c>
    /// (https://drafts.csswg.org/css-text-4/#valdef-white-space-collapse-break-spaces: they "take up space and do not hang"):
    /// as in Blink and Gecko, they are not the trailing white space of rule L1 of UAX #9, so they stay next to the text that
    /// they follow in its direction (WPT css/css-text/white-space/trailing-space-and-text-alignment-rtl-004.html), instead of
    /// being moved to the end of the line of the paragraph direction.
    /// </summary>
    private int GetBreakSpacesTrailingLength(int lineStart, int lineEnd)
    {
        if (_leaves.Count == 0 || _leaves[^1] is not { Kind: LineLeafKind.Text, Item: >= 0 } last || Items[last.Item].Style.Layout.WhiteSpaceCollapse != WhiteSpaceCollapse.BreakSpaces)
            return 0;

        var start = Math.Max(lineStart, last.Start);
        var end = Math.Min(lineEnd, last.End);
        var position = end;
        while (position > start && _text[position - 1] == ' ')
        {
            position--;
        }

        return end == lineEnd ? end - position : 0;
    }

    /// <summary>
    /// Applies rule L1 of UAX #9 to the line (the trailing white space gets the paragraph level), splits the text leaves at the
    /// level changes and sets the level of each leaf. Returns whether the line is reordered (rule L2 changes the order).
    /// </summary>
    private bool ResolveLineLevels(int lineStart, int lineEnd)
    {
        var length = lineEnd - lineStart;
        if (_lineLevels.Length < length)
        {
            _lineLevels = new byte[Math.Max(length, _lineLevels.Length * 2)];
        }

        var lineLevels = _lineLevels.AsSpan(0, length);
        _bidi.GetLineLevels(lineStart, lineEnd, lineLevels, GetBreakSpacesTrailingLength(lineStart, lineEnd));
        var paragraphLevel = GetParagraphLevel(Math.Min(lineStart, _length - 1));

        _splitLeaves.Clear();
        foreach (var leaf in _leaves)
        {
            if (leaf.Kind != LineLeafKind.Text)
            {
                var copy = leaf;
                var position = leaf.Kind == LineLeafKind.EmptyBox && leaf.Item >= 0 ? Items[leaf.Item].Start : leaf.Start;
                copy.Level = leaf.Kind == LineLeafKind.Hyphen && _splitLeaves.Count > 0 ? _splitLeaves[^1].Level
                    : position >= lineStart && position < lineEnd ? lineLevels[position - lineStart] : paragraphLevel;

                // An absolutely positioned box whose original display is inline-level is at its static position as a neutral
                // character (U+FFFC in Blink): between characters of different levels, it has the lower one (e.g. the paragraph
                // level before a left-to-right word that starts a right-to-left line, WPT
                // css/css-text/text-indent/text-indent-with-absolute-pos-child.html)
                if (leaf.Kind == LineLeafKind.OutOfFlow && !Items[leaf.Item].IsFloat && PositionedLayout.IsOriginalDisplayInlineLevel(Items[leaf.Item].Box.Style))
                {
                    copy.Level = Math.Min(copy.Level, position > lineStart && position <= lineEnd ? lineLevels[position - lineStart - 1] : paragraphLevel);
                }

                _splitLeaves.Add(copy);
                continue;
            }

            var start = leaf.Start;
            while (start < leaf.End)
            {
                var level = lineLevels[start - lineStart];
                var end = start + 1;
                while (end < leaf.End && lineLevels[end - lineStart] == level)
                {
                    end++;
                }

                var part = leaf;
                part.Start = start;
                part.End = end;
                part.HangStart = Math.Clamp(leaf.HangStart, start, end);
                part.Level = level;
                _splitLeaves.Add(part);
                start = end;
            }
        }

        _leaves.Clear();
        _leaves.AddRange(_splitLeaves);
        _splitLeaves.Clear();

        // Rule L2 on the leaves, with the isolate initiators and PDIs of the line: they are not removed by rule X9 and have a level
        // (the one of the enclosing embedding, or the paragraph level at the end of the line after rule L1), so they separate the
        // content of an isolate from the content around it. For instance, with "XXX" RLI "!!" PDI in a right-to-left paragraph, the
        // levels are 2 1 3 1 and the isolate is on the left of "XXX", whereas the levels of the leaves alone (2 3) would put it on
        // the right. The controls are not displayed and have no leaf: they are removed from the visual order.
        var count = _leaves.Count;
        var total = count + CountIsolateControls(lineStart, lineEnd);
        EnsureOrderBuffers(total);
        var levels = _leafLevels.AsSpan(0, total);
        if (total == count)
        {
            for (var i = 0; i < count; i++)
            {
                levels[i] = _leaves[i].Level;
            }

            var order = _visualOrder.AsSpan(0, count);
            BidiResolver.ReorderVisual(levels, order);
            for (var i = 0; i < count; i++)
            {
                if (order[i] != i)
                    return true;
            }

            return false;
        }

        // The sequence in logical order: a control at position p is before the leaves after p, and after the empty leaves at p
        // (which have the level of the next character). _sequenceLeaves maps an index of the sequence to its leaf (-1 for a control).
        var sequenceLeaves = _sequenceLeaves.AsSpan(0, total);
        var sequence = 0;
        var textPosition = lineStart;
        for (var i = 0; i <= count; i++)
        {
            var leafPosition = i < count ? Math.Min(GetLeafPosition(_leaves[i], lineEnd), lineEnd) : lineEnd;
            for (; textPosition < leafPosition; textPosition++)
            {
                if (IsIsolateControl(_text[textPosition]))
                {
                    levels[sequence] = lineLevels[textPosition - lineStart];
                    sequenceLeaves[sequence++] = -1;
                }
            }

            if (i < count)
            {
                levels[sequence] = _leaves[i].Level;
                sequenceLeaves[sequence++] = i;
            }
        }

        Debug.Assert(sequence == total);
        var sequenceOrder = _sequenceOrder.AsSpan(0, total);
        BidiResolver.ReorderVisual(levels, sequenceOrder);
        var reordered = false;
        var visual = 0;
        foreach (var index in sequenceOrder)
        {
            var leaf = sequenceLeaves[index];
            if (leaf >= 0)
            {
                reordered |= leaf != visual;
                _visualOrder[visual++] = leaf;
            }
        }

        return reordered;
    }

    /// <summary>The number of isolate initiators and PDIs (FSI, LRI, RLI, PDI) in a line.</summary>
    private int CountIsolateControls(int lineStart, int lineEnd)
    {
        var count = 0;
        foreach (var c in _text.AsSpan(lineStart, lineEnd - lineStart))
        {
            if (IsIsolateControl(c))
            {
                count++;
            }
        }

        return count;
    }

    private static bool IsIsolateControl(char c) => c is BidiControls.LeftToRightIsolate or BidiControls.RightToLeftIsolate or BidiControls.FirstStrongIsolate or BidiControls.PopDirectionalIsolate;

    /// <summary>
    /// The position of a leaf in the text of the inline formatting context: the position of the tag of an empty inline box, or the end
    /// of the line for an inline box that continues on the next line without content.
    /// </summary>
    private int GetLeafPosition(in LineLeaf leaf, int lineEnd)
    {
        if (leaf.Kind != LineLeafKind.EmptyBox)
            return leaf.Start;

        if (leaf.Item < 0)
            return lineEnd;

        ref readonly var item = ref Items[leaf.Item];
        return item.Kind is InlineItemKind.OpenTag or InlineItemKind.CloseTag ? item.Start : leaf.Start;
    }

    private void EnsureOrderBuffers(int count)
    {
        if (_visualOrder.Length < count)
        {
            _visualOrder = new int[Math.Max(count, _visualOrder.Length * 2)];
            _leafLevels = new byte[_visualOrder.Length];
            _sequenceLeaves = new int[_visualOrder.Length];
            _sequenceOrder = new int[_visualOrder.Length];
        }
    }

    /// <summary>Measures the leaves and finds their glyphs. Returns the inline size of the leaves, without the hanging spaces (<paramref name="hangWidth"/>).</summary>
    private double MeasureLeaves(bool isFirstLine, out double hangWidth)
    {
        hangWidth = 0;
        var width = 0d;
        foreach (ref var leaf in CollectionsMarshal.AsSpan(_leaves))
        {
            if (leaf.Kind == LineLeafKind.Text)
            {
                ref readonly var item = ref Items[leaf.Item];
                var shaped = GetShaped(leaf.Item, isFirstLine);
                var start = leaf.Start - item.Start;
                var end = leaf.End - item.Start;

                // A line that starts or ends where the text is unsafe to break is shaped again (e.g. a kerning pair across the break)
                if ((start > 0 || end < shaped.Length) && (shaped.IsUnsafeToBreak(start) || shaped.IsUnsafeToBreak(end)) && !item.HasFlag(InlineItemFlags.Tab) && !shaped.IsCombinedUpright)
                {
                    shaped = ShapeLineEdges(leaf.Item, shaped, start, end, isFirstLine);
                }

                leaf.Shaped = shaped;
                leaf.Width = shaped.GetWidth(start, end);
                hangWidth += shaped.GetWidth(leaf.HangStart - item.Start, end);
                var glyphs = shaped.Glyphs;
                leaf.GlyphStart = ShapedText.FindGlyph(glyphs, start);
                leaf.GlyphCount = ShapedText.FindGlyph(glyphs, end) - leaf.GlyphStart;
            }

            width += leaf.Width;
        }

        return width - hangWidth;
    }

    /// <summary>
    /// The advance of the preserved spaces (<c>white-space-collapse: preserve</c> with wrapping) that end the white space that
    /// hangs at the end of the line: they conditionally hang before a forced line break.
    /// </summary>
    private double GetConditionalHangWidth()
    {
        var width = 0d;
        var leaves = CollectionsMarshal.AsSpan(_leaves);
        for (var i = leaves.Length - 1; i >= 0; i--)
        {
            ref readonly var leaf = ref leaves[i];
            if (leaf.Kind is LineLeafKind.OutOfFlow or LineLeafKind.EmptyBox or LineLeafKind.LineBreak || (leaf.Kind == LineLeafKind.Text && leaf.Start == leaf.End))
                continue;

            if (leaf.Kind != LineLeafKind.Text || leaf.HangStart >= leaf.End || leaf.Shaped is null)
                break;

            ref readonly var item = ref Items[leaf.Item];
            if (!item.HasFlag(InlineItemFlags.HangsSpaces))
                break;

            width += item.HasFlag(InlineItemFlags.Tab) ? item.InlineSize : leaf.Shaped.GetWidth(leaf.HangStart - item.Start, leaf.End - item.Start);
            if (leaf.HangStart > leaf.Start)
                break;
        }

        return width;
    }

    /// <summary>Whether the line contains text, an atomic inline or a line break.</summary>
    private bool HasLineContent()
    {
        foreach (var leaf in _leaves)
        {
            if (leaf.Kind is LineLeafKind.Text or LineLeafKind.Atomic or LineLeafKind.LineBreak or LineLeafKind.Hyphen)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the offset of the content of the line from the line-left edge of the line box: <c>text-indent</c> and
    /// <c>text-align</c> (https://drafts.csswg.org/css-text-4/#text-align-property). The justification
    /// (https://drafts.csswg.org/css-text-4/#justification) distributes the remaining space to the justification opportunities.
    /// The content that hangs (https://drafts.csswg.org/css-text-4/#hanging) is outside the measure of the content: at the end
    /// (<paramref name="hangWidth"/>) and at the start (<paramref name="startHang"/>, placed before the start edge).
    /// </summary>
    private double AlignLine(in LineEnd end, double availableSize, double indent, double contentWidth, double hangWidth, double startHang, bool isRightToLeft)
    {
        var text = _block.Style.Layout.Text;

        // text-align-last applies to the last line of the block container and to the lines before a forced line break
        var isLast = end.Item >= _items.Count || end.IsForced;
        var align = text.TextAlign;
        if (isLast)
        {
            align = text.TextAlignLast == TextAlign.Auto ? (align is TextAlign.Justify or TextAlign.JustifyAll ? TextAlign.Start : align) : text.TextAlignLast;
        }

        var free = availableSize - indent - contentWidth;

        // https://drafts.csswg.org/css-text-4/#text-group-align-property: the line boxes are padded by the shortest remaining
        // space of the line boxes of the group, and the content is aligned in the space that remains
        var groupPadding = 0d;
        if (_blockFormattingContext?.TextGroupAlignment is { } group && !_hidesLines)
        {
            if (group.Padding is { } padding)
            {
                free -= padding;
                groupPadding = group.GetLineLeftPadding(_block.Style.Layout.Direction == Direction.Rtl);
            }
            else
            {
                group.Measure(free);
            }
        }

        if (align is TextAlign.Justify or TextAlign.JustifyAll)
        {
            if (free > 0 && Justify(free))
            {
                _justifiedSpace = free;
                free = 0;
            }

            align = TextAlign.Start;
        }

        // The physical alignment (start and end depend on the direction of the line)
        var right = align switch
        {
            TextAlign.Right => true,
            TextAlign.End => !isRightToLeft,
            TextAlign.Left => false,
            TextAlign.Center => false,
            _ => isRightToLeft,
        };

        // "If (after justification, if any) the inline contents of a line box are too long to fit within it, then the
        // contents are start-aligned"
        double offset;
        if (free < 0)
        {
            offset = isRightToLeft ? free : 0;
        }
        else if (align == TextAlign.Center)
        {
            offset = free / 2;
        }
        else
        {
            offset = right ? free : 0;
        }

        // The indentation is on the start side; the hanging content at the end of a right-to-left line is on its left, the one at
        // the start of a left-to-right line is before the start edge
        return groupPadding + (isRightToLeft ? offset - hangWidth : offset + indent - startHang);
    }
}
