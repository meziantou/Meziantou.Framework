namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The line clamping of a line-clamp container (https://drafts.csswg.org/css-overflow-4/#line-clamp): a block container with
/// <c>continue: collapse</c> (or <c>discard</c>, or <c>-webkit-legacy</c> on a vertical <c>-webkit-box</c>), which establishes a
/// block formatting context. The content after its clamp point is hidden, and the container is sized as if it had no content
/// after the clamp point. The state is shared by the boxes that participate in the block formatting context of the container
/// (<see cref="BlockFormattingContextState.LineClamp"/>), while they are laid out in order.
/// </summary>
/// <remarks>
/// <para>
/// The possible clamp points are counted in the order of the layout: after each line box that is not empty, and after each
/// in-flow block-level box (its end) of the block formatting context; the contents of the independent formatting contexts are
/// skipped. With <c>max-lines</c>, the clamp point is after the N-th line box, when content follows it (another line box, or a
/// block-level box that does not collapse through). With <c>max-lines: auto</c>, the clamp point is the last
/// one where the container, clamped there, fits in its block size (its <c>height</c>, or its <c>max-height</c> floored by its
/// <c>min-height</c>), as Blink: there is no clamp point inside the independent formatting contexts.
/// </para>
/// <para>
/// The container is laid out first to measure the clamp points (<see cref="Layout"/>): when it is clamped, it is laid out again
/// with the clamp point (the block size of a clamp point is found by a binary search over the layouts clamped at the candidate
/// points, without ellipsis). In that layout, the line before the clamp point (when no block-level box starts between them)
/// reserves the space of the block overflow ellipsis (<c>block-ellipsis</c>, see <see cref="InlineLayoutAlgorithm"/>), the content
/// after the clamp point is laid out but hidden (<see cref="Fragment.IsHiddenForPaint"/>), and each block container whose
/// content is clamped ends at the clamp point: its children restore the state of the block formatting context saved at the
/// clamp point once they are laid out.
/// </para>
/// </remarks>
internal sealed class LineClamp
{
    /// <summary>The tolerance of the comparison of the block size of a clamped container with its maximum block size.</summary>
    private const double Epsilon = 0.01;

    /// <summary>The ellipsis line of the layouts that look for the clamp point by the block size, which do not balance the lines.</summary>
    private const int NotBalanced = -2;

    private readonly int? _maxLines;
    private readonly int _clampPoint;
    private readonly int _ellipsisLine;

    /// <summary>When measuring: the line that gets the ellipsis when the clamp point is after each point, or -1 (a box started after it).</summary>
    private readonly List<int>? _pointEllipsisLines;

    /// <summary>When measuring: the last line before each point (the line itself for the point after a line), or -1.</summary>
    private readonly List<int>? _pointLastLines;

    /// <summary>When recording the line boxes (see <see cref="CreateLineRecorder"/>): the block-end edge of each line box in the block formatting context.</summary>
    private List<double>? _lineEnds;

    private int _lastLine = -1;
    private bool _boxStartedSinceLastLine;

    /// <summary>When measuring with <c>max-lines</c>: the point after the N-th line, and whether content follows it.</summary>
    private int _maxLinesPoint = -1;
    private bool _hasContentAfterMaxLines;

    private LineClamp(TextOverflowSide ellipsis, bool isLegacy, int? maxLines, int clampPoint, int ellipsisLine, int lastVisibleLine, bool measures)
    {
        Ellipsis = ellipsis;
        IsLegacy = isLegacy;
        _maxLines = maxLines;
        _clampPoint = clampPoint;
        _ellipsisLine = ellipsisLine;
        LastVisibleLine = lastVisibleLine;
        if (measures)
        {
            _pointEllipsisLines = [];
            _pointLastLines = [];
        }
    }

    /// <summary>The block overflow ellipsis (<c>block-ellipsis</c>): <see cref="TextOverflowKind.Clip"/> for none.</summary>
    public TextOverflowSide Ellipsis { get; }

    /// <summary>
    /// Whether the container is clamped by <c>-webkit-line-clamp</c> (<see cref="ContinueKind.WebkitLegacy"/>): as in the browsers
    /// (WPT css/css-overflow/line-clamp/webkit-line-clamp-035.html, the baselines of css/css-flexbox/alignment/flex-align-baseline-line-clamp-*),
    /// when no content of the last line fits with the block overflow ellipsis, the line keeps its content, which is truncated by the
    /// ellipsis when it is painted (<see cref="LineBoxFragment.IsTruncatedByBlockEllipsis"/>) instead of being displaced.
    /// </summary>
    public bool IsLegacy { get; }

    /// <summary>
    /// The block-end margin edge of the floats of the block formatting context of the container: the floats do not extend the
    /// block size measured for the clamp point (they are clipped to the content box of a clamped container), only the block size
    /// of a container that is not clamped.
    /// </summary>
    public double FloatsEnd { get; set; }

    /// <summary>
    /// The last line before the clamp point (int.MaxValue when the container is not clamped): <c>text-wrap-style: balance</c> balances
    /// the lines before the clamp point ("the clamping effect is applied first, then the remaining lines are balanced", WPT
    /// css/css-text/white-space/text-wrap-balance-line-clamp-004.html), only in the layouts where <see cref="Balances"/>.
    /// </summary>
    public int LastVisibleLine { get; }

    /// <summary>Whether the lines are balanced (<c>text-wrap-style: balance</c>): not in the layouts that look for the clamp point.</summary>
    public bool Balances => _pointEllipsisLines is null && _ellipsisLine != NotBalanced;

    /// <summary>Whether a line was not balanced because the layout looks for the clamp point: the container is laid out again.</summary>
    public bool SkippedBalance { get; set; }

    /// <summary>Returns the line that gets the block overflow ellipsis (when it is the next line, see <see cref="IsEllipsisLine"/>), or -1.</summary>
    public int EllipsisLine => _ellipsisLine;

    /// <summary>The number of possible clamp points passed so far.</summary>
    public int PointCount { get; private set; }

    /// <summary>The number of line boxes (not empty) laid out so far.</summary>
    public int LineCount { get; private set; }

    /// <summary>Whether the clamp point was passed: the content laid out from now on is hidden.</summary>
    public bool IsClamped => PointCount > _clampPoint;

    /// <summary>Whether the clamp point is after the next line box (if it is not empty): it is the last formatted line of the container.</summary>
    public bool IsClampLine => PointCount == _clampPoint;

    /// <summary>Whether the next line box (if it is not empty) is the last line before the clamp point, which gets the block overflow ellipsis.</summary>
    public bool IsEllipsisLine => LineCount == _ellipsisLine && !IsClamped;

    /// <summary>
    /// Returns whether a box is a line-clamp container (https://drafts.csswg.org/css-overflow-4/#line-clamp-container): a block
    /// container laid out by the block or inline layout (not an inline box, a multi-column container or an anonymous box) whose
    /// <c>continue</c> collapses its content after the clamp point. It establishes an independent formatting context.
    /// </summary>
    public static bool IsLineClampContainer(ContainerBox box)
    {
        if (box.Kind != ContainerBoxKind.Normal || box.IsInlineBox || !box.IsBlockContainer)
            return false;

        var layout = box.Style.Layout;
        return layout.Continue != ContinueKind.Auto && !layout.IsMultiColumnContainer
            && FormattingContext.GetKind(box) is FormattingContextKind.Block or FormattingContextKind.Inline;
    }

    /// <summary>
    /// Creates the state that records the block-end edges of the line boxes of a multi-column container with <c>continue: discard</c>
    /// and a block overflow ellipsis, laid out in its strip without clamping (see <see cref="CreateEllipsisClamp"/>).
    /// </summary>
    public static LineClamp CreateLineRecorder(TextOverflowSide ellipsis)
        => new(ellipsis, isLegacy: false, maxLines: null, int.MaxValue, -1, int.MaxValue, measures: false) { _lineEnds = [] };

    /// <summary>Whether the state records the block-end edges of the line boxes (see <see cref="CreateLineRecorder"/>).</summary>
    public bool RecordsLines => _lineEnds is not null;

    /// <summary>
    /// Returns the state of a layout without clamping where the last line box that ends before a position of the block formatting
    /// context (the first region break of a multi-column container with <c>continue: discard</c>, whose content after it is
    /// discarded) gets the block overflow ellipsis (https://drafts.csswg.org/css-overflow-4/#block-ellipsis), or null when no line
    /// ends before it.
    /// </summary>
    public LineClamp? CreateEllipsisClamp(double position)
    {
        var line = -1;
        for (var i = 0; i < _lineEnds!.Count; i++)
        {
            if (_lineEnds[i] <= position + Epsilon)
            {
                line = i;
            }
        }

        return line < 0 ? null : new LineClamp(Ellipsis, isLegacy: false, maxLines: null, int.MaxValue, line, int.MaxValue, measures: false);
    }

    /// <summary>Records a line box that is not empty (a possible clamp point after it).</summary>
    /// <param name="blockEnd">The block-end edge of the line box in the block formatting context.</param>
    public void OnLine(double blockEnd)
    {
        _lineEnds?.Add(blockEnd);
        if (_pointEllipsisLines is not null)
        {
            _pointEllipsisLines.Add(LineCount);
            _pointLastLines!.Add(LineCount);
            if (_maxLinesPoint >= 0)
            {
                _hasContentAfterMaxLines = true;
            }
            else if (LineCount + 1 == _maxLines)
            {
                _maxLinesPoint = PointCount;
            }
        }

        _lastLine = LineCount;
        _boxStartedSinceLastLine = false;
        LineCount++;
        PointCount++;
    }

    /// <summary>Records the start of an in-flow block-level box; returns whether it starts after the N-th line (see <see cref="EndBox"/>).</summary>
    public bool BeginBox()
    {
        _boxStartedSinceLastLine = true;
        return _maxLinesPoint >= 0;
    }

    /// <summary>Records the end of an in-flow block-level box (a possible clamp point after it).</summary>
    /// <param name="startedAfterMaxLines">The result of <see cref="BeginBox"/>.</param>
    /// <param name="hasContent">Whether the box is content after the N-th line: it does not collapse through.</param>
    public void EndBox(bool startedAfterMaxLines, bool hasContent)
    {
        if (_pointEllipsisLines is not null)
        {
            // The line before the clamp point gets the ellipsis when no box started between them (WPT line-clamp-auto-036, 039, 040)
            _pointEllipsisLines.Add(_boxStartedSinceLastLine ? -1 : _lastLine);
            _pointLastLines!.Add(_lastLine);
            if (startedAfterMaxLines && hasContent)
            {
                _hasContentAfterMaxLines = true;
            }
        }

        PointCount++;
    }

    /// <summary>
    /// Lays out the contents of a line-clamp container (<see cref="IsLineClampContainer"/>) with its formatting context: a first
    /// layout measures the clamp points; when the container is clamped, its contents are laid out again with the clamp point.
    /// </summary>
    public static ContentLayoutResult Layout(LayoutContext context, FormattingContext formattingContext, ContainerBox box, FragmentBuilder builder, in ContentArea content)
    {
        var overflow = box.Style.Layout.Overflow;
        var ellipsis = overflow.BlockEllipsis;
        var maxLines = overflow.MaxLines;

        // https://drafts.csswg.org/css-overflow-4/#valdef-max-lines-auto: the clamp point given by the block size (the minimum
        // block size floors the maximum block size, WPT line-clamp-auto-014)
        double? maxBlockSize = null;
        if (overflow.MaxLinesAuto)
        {
            maxBlockSize = content.BlockSize ?? (content.MaxBlockSize is { } max ? Math.Max(max, content.MinBlockSize ?? 0) : null);
        }

        var isLegacy = box.Style.Layout.Continue == ContinueKind.WebkitLegacy;
        var measure = new LineClamp(ellipsis, isLegacy, maxLines, int.MaxValue, -1, int.MaxValue, measures: true);
        var result = LayoutPass(context, formattingContext, box, builder, content, measure, first: true);

        int? clampPoint = null;
        if (maxLines is not null && measure._maxLinesPoint >= 0 && measure._hasContentAfterMaxLines)
        {
            clampPoint = measure._maxLinesPoint;
        }

        if (maxBlockSize is { } limit && GetBlockSize(result) > limit + Epsilon)
        {
            // The last clamp point where the clamped container fits (-1: before the content); the clamped size grows with the point
            var low = -1;
            var high = clampPoint ?? (measure.PointCount - 1);
            while (low < high)
            {
                var middle = low + ((high - low + 1) / 2);
                var probe = new LineClamp(TextOverflowSide.Clip, isLegacy, maxLines: null, middle, NotBalanced, int.MaxValue, measures: false);
                if (GetBlockSize(LayoutPass(context, formattingContext, box, builder, content, probe, first: false)) <= limit + Epsilon)
                {
                    low = middle;
                }
                else
                {
                    high = middle - 1;
                }
            }

            clampPoint = low;
        }

        if (!measure.SkippedBalance)
        {
            if (clampPoint is not { } point)
                return Finish(result, measure, clamped: false);

            return Finish(LayoutPass(context, formattingContext, box, builder, content, measure.CreateClamp(point), first: false), measure, clamped: true);
        }

        // The lines before the clamp point are balanced (text-wrap-style: balance); when the balanced lines do not fit in the block
        // size, the container is clamped again at the previous clamp point (WPT css/css-overflow/line-clamp/line-clamp-balance-004.html)
        var current = clampPoint ?? int.MaxValue;
        while (true)
        {
            var clamp = current == int.MaxValue ? new LineClamp(ellipsis, isLegacy, maxLines: null, int.MaxValue, -1, int.MaxValue, measures: false) : measure.CreateClamp(current);
            result = LayoutPass(context, formattingContext, box, builder, content, clamp, first: false);
            if (maxBlockSize is not { } maximum || current == -1 || GetBlockSize(result) <= maximum + Epsilon)
                return Finish(result, measure, clamped: current != int.MaxValue);

            current = (current == int.MaxValue ? measure.PointCount : current) - 1;
        }

        static double GetBlockSize(in ContentLayoutResult result) => result.BlockSize + result.EndMargin.Sum;

        // https://drafts.csswg.org/css2/#root-height: a container that is not clamped contains its floats
        static ContentLayoutResult Finish(ContentLayoutResult result, LineClamp measure, bool clamped)
        {
            if (!clamped && measure.FloatsEnd > GetBlockSize(result))
            {
                result = result with { BlockSize = measure.FloatsEnd, EndMargin = default, CollapsedThrough = false };
            }

            return result with { IsLineClampContainer = true };
        }
    }

    /// <summary>Returns the state of the layout clamped after a point, from the measure of the points.</summary>
    private LineClamp CreateClamp(int point)
    {
        var ellipsisLine = point >= 0 && Ellipsis.Kind != TextOverflowKind.Clip ? _pointEllipsisLines![point] : -1;
        var lastVisibleLine = point >= 0 ? _pointLastLines![point] : -1;
        return new LineClamp(Ellipsis, IsLegacy, maxLines: null, point, ellipsisLine, lastVisibleLine, measures: false);
    }

    private static ContentLayoutResult LayoutPass(LayoutContext context, FormattingContext formattingContext, ContainerBox box, FragmentBuilder builder, in ContentArea content, LineClamp clamp, bool first)
    {
        if (!first)
        {
            builder.Reset(builder.WritingDirection);
            if (content.OutsideMarker is { } marker)
            {
                marker.IsPlaced = false;
            }
        }

        return formattingContext.LayoutFlow(context, box, builder, content with { LineClamp = clamp });
    }
}
