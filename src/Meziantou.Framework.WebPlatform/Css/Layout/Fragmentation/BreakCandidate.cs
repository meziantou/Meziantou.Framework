using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// How desirable a break at a break opportunity is (https://drafts.csswg.org/css-break-3/#break-rules): the breaker chooses the
/// latest opportunity that fits, preferring the perfect ones over the ones that violate an <c>avoid</c> rule.
/// </summary>
internal enum BreakAppeal : byte
{
    /// <summary>A forced break (https://drafts.csswg.org/css-break-3/#forced-break): the fragmentainer ends there.</summary>
    Forced,

    /// <summary>A break that violates no rule.</summary>
    Perfect,

    /// <summary>A break between two line boxes that violates <c>orphans</c> or <c>widows</c> (https://drafts.csswg.org/css-break-3/#widows-orphans). Used only when no perfect break fits.</summary>
    OrphansWidows,

    /// <summary>
    /// A break that violates a rule (https://drafts.csswg.org/css-break-3/#break-rules): <c>break-before</c>/<c>break-after</c>/<c>break-inside:
    /// avoid</c>. Used only when no perfect break fits.
    /// </summary>
    Avoid,

    /// <summary>No possible break fits: the content overflows the fragmentainer (the monolithic content, https://drafts.csswg.org/css-break-3/#unforced-breaks).</summary>
    LastResort,
}

/// <summary>
/// A possible break (https://drafts.csswg.org/css-break-3/#possible-breaks) of the content of a fragmentation context laid out in
/// a single strip: a block position of the flow (logical, from the block-start edge of the flow) where the next fragmentainer may
/// start, or a range of block positions where it may start anywhere (the empty block size of a box after its contents).
/// </summary>
/// <param name="Position">
/// The position where the content after the break starts: the block-start border edge of the box or the line box after the break
/// (the margins adjoining an unforced break are truncated; the margins after a forced break are preserved,
/// https://drafts.csswg.org/css-break-3/#break-margins). The start of the range of a breakable range.
/// </param>
/// <param name="ContentEnd">The block-end edge of the content before the break (its margins are truncated): it must fit in the fragmentainer.</param>
/// <param name="Appeal">How desirable the break is.</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct BreakCandidate(double Position, double ContentEnd, BreakAppeal Appeal)
{
    /// <summary>The end of a range of breakable positions starting at <see cref="Position"/>, or NaN for a single break opportunity.</summary>
    public double RangeEnd { get; init; } = double.NaN;

    /// <summary>Whether a break may occur at the start of the range (<see cref="Position"/>), not only after it.</summary>
    public bool IncludesRangeStart { get; init; }

    /// <summary>Whether a break may not occur at the end of the range when the flow has a possible break there (the end of the content of an in-flow box: the break after the box has its own rules).</summary>
    public bool ExcludesRangeEnd { get; init; }

    /// <summary>
    /// Whether the range is the empty block size of a float, which is beside the in-flow content: a break at a position where the in-flow content has a possible break is that break, with
    /// its own rules (<c>orphans</c> and <c>widows</c> between two line boxes).
    /// </summary>
    public bool IsInFloat { get; init; }

    /// <summary>
    /// The number of boxes with <c>break-inside: avoid</c> that contain the break: when only breaks that violate a rule fit,
    /// the ones inside fewer of these boxes are preferred (as in the browsers, the break is outside of the innermost avoided
    /// box, CSS2 WPT css/CSS2/pagination/block-page-break-inside-avoid-7-print.html).
    /// </summary>
    public int AvoidDepth { get; init; }

    /// <summary>Whether the candidate is a range of breakable positions.</summary>
    public bool IsRange => !double.IsNaN(RangeEnd);

    /// <summary>
    /// The block-start borders and paddings of the boxes with <c>box-decoration-break: clone</c> that contain the break: they are
    /// repeated at the start of the next fragmentainer (https://drafts.csswg.org/css-break-3/#break-decoration).
    /// </summary>
    public double CloneStartEdges { get; init; }

    /// <summary>
    /// Whether the gap after the break remains: the break is inside a track of parallel flows (a line of a flex container, a row
    /// of a grid) that continues after the break, or after content that overflows the fragmentainer.
    /// </summary>
    public bool KeepsGap { get; init; }

    /// <summary>
    /// Whether the space between <see cref="ContentEnd"/> and <see cref="Position"/> is made of the margins of the boxes before and
    /// after the break: a fragmentainer may end in them (they are truncated, https://drafts.csswg.org/css-break-3/#break-margins).
    /// </summary>
    public bool IsBetweenMargins { get; init; }

    /// <summary>
    /// How much the block-start side of the line box after the break is trimmed by <c>text-box-trim</c> when it is the first
    /// formatted line of a fragmentainer (https://drafts.csswg.org/css-inline-3/#text-box-trim: "each column of a multi-column
    /// container"): the fragmentainer starts that far after <see cref="Position"/> (the trimmed part of the line overflows its
    /// block-start edge), or 0.
    /// </summary>
    public double StartTrim { get; init; }

    /// <summary>
    /// How much the block-end side of the line box before the break is trimmed by <c>text-box-trim</c> when it is the last
    /// formatted line of a fragmentainer (see <see cref="StartTrim"/>), or 0: <see cref="ContentEnd"/> is before the trimmed part,
    /// which a fragmentainer may end in.
    /// </summary>
    public double EndTrim { get; init; }

    /// <summary>
    /// Whether the break is only possible when the content after it starts after the end of the fragmentainer (the break
    /// before the first content of a box, whose block-start margin does not fit: as in Blink, the box is pushed to the next
    /// fragmentainer and the margin is truncated).
    /// </summary>
    public bool IsPastEndOnly { get; init; }

    /// <summary>The block-end edge of the box after the break, for a break that is only possible when the box starts after the end of the fragmentainer (see <see cref="IsPastEndOnly"/>): a fragmentainer that contains the box avoids the break.</summary>
    public double BoxEnd { get; init; }

    /// <summary>
    /// Whether the break is before a float that is not pushed: the float would be pushed alone to the next fragmentainer, so it is
    /// not a possible break of the in-flow content. It is only used to balance the columns (<c>FragmentationFlow.BreaksBeforeFloats</c>).
    /// </summary>
    public bool IsBeforeFloat { get; init; }

    /// <summary>Whether the break is before a float that was pushed to the start of a fragmentainer (see <see cref="FloatPush"/>).</summary>
    public bool IsBeforePushedFloat { get; init; }

    /// <summary>
    /// The repeated header and footer of the nearest table that contains the break (their block sizes are in
    /// <see cref="CloneStartEdges"/> and <see cref="CloneEndEdges"/>), or null: the breaker decides whether they are repeated.
    /// </summary>
    public RepeatedSections? Repeated { get; init; }

    /// <summary>
    /// The nearest box around the break that repeats its edges until it ends (see <see cref="ParallelGroup.OwnCloneStart"/>; its
    /// edges are in <see cref="CloneStartEdges"/> and <see cref="CloneEndEdges"/>), or null.
    /// </summary>
    public ParallelGroup? CloneOwner { get; init; }

    /// <summary>
    /// Returns the candidate without the block sizes of the header and of the footer that are not repeated (see
    /// <see cref="Repeated"/>) and without the edges of the boxes that have ended (see <see cref="CloneOwner"/>).
    /// </summary>
    public BreakCandidate ResolveRepeated()
    {
        if (Repeated is null && CloneOwner is null)
            return this;

        var (endedStart, endedEnd) = CloneOwner?.GetEndedCloneEdges() ?? default;
        return this with
        {
            CloneStartEdges = Math.Max(0, CloneStartEdges - (Repeated is { IsHeaderDisabled: true } header ? header.HeaderSize : 0) - endedStart),
            CloneEndEdges = Math.Max(0, CloneEndEdges - (Repeated is { IsFooterDisabled: true } footer ? footer.FooterSize : 0) - endedEnd),
            Repeated = null,
            CloneOwner = null,
        };
    }

    /// <summary>The line boxes of the block container, for a break between two of them whose appeal depends on <c>orphans</c> and <c>widows</c> (see <see cref="LineBreakInfo"/>), else null.</summary>
    public LineBreakInfo? Lines { get; init; }

    /// <summary>The index in its block container of the line box after the break (see <see cref="Lines"/>).</summary>
    public int LineIndex { get; init; }

    /// <summary>The block-end borders and paddings of the boxes with <c>box-decoration-break: clone</c> that contain the break: they need room before the break.</summary>
    public double CloneEndEdges { get; init; }

    /// <summary>
    /// The position from which only the block-start edges of boxes with <c>box-decoration-break: clone</c> precede the break (the
    /// break is after these edges, before the first content of the boxes), else NaN: as in Blink, it is not a possible break in
    /// a fragmentainer that starts at that position or after it (the edges are repeated in the next fragmentainer: the content
    /// would not fit better there).
    /// </summary>
    public double OnlyEdgesFrom { get; init; } = double.NaN;

    /// <summary>
    /// The position from which only the block-start edges of boxes precede the break (the break is after these edges, before the
    /// first content of the boxes), else NaN: in the pages, as in Blink ("requires content before breaking"), it is not a
    /// possible break in a page that starts at that position or after it, where the content after it stays (the monolithic
    /// content that does not fit is sliced, see <see cref="FragmentationFlow.SlicesMonolithicContent"/>).
    /// </summary>
    public double EdgesOnlyFrom { get; init; } = double.NaN;

    /// <summary>Whether the break is between the last content of a box and its block-end border and padding (a last-resort break).</summary>
    public bool IsBeforeEndEdges { get; init; }

    /// <summary>
    /// Whether the boxes broken at the break end with the content before it, not at the end of the fragmentainer: as in Blink,
    /// a break between two children of a table wrapper box (the captions and the table grid box) does not stretch the table.
    /// </summary>
    public bool EndsWithContent { get; init; }

    /// <summary>
    /// The block size of the flow that is skipped after the break: the edges that a box with <c>box-decoration-break: clone</c>
    /// and a block size of its border box repeats at the break are taken from its block size.
    /// </summary>
    public double SkipAfter { get; init; }

    /// <summary>
    /// The order of the break in the content of the strip (see <see cref="FragmentationFlow.GetEmptyOrdinal"/>): several breaks may
    /// be at the same position (before and after a box without block size). 0 for a break that is not between two pieces of
    /// content (in a range, inside a track).
    /// </summary>
    public int Sequence { get; init; }
}

/// <summary>
/// The line boxes of a block container whose breaks may violate <c>orphans</c> or <c>widows</c>
/// (https://drafts.csswg.org/css-break-3/#widows-orphans). As in Blink, the line boxes are counted in each fragment of the
/// block container: a break needs <c>orphans</c> line boxes before it in the fragmentainer and <c>widows</c> after it; when
/// no break has both, the first break with enough line boxes before it is preferred (the line boxes after it are as many as
/// possible).
/// </summary>
internal sealed class LineBreakInfo(int count, int orphans, int widows)
{
    /// <summary>The block-start edge of each line box in the strip.</summary>
    public double[] Tops { get; } = new double[count];

    public int Orphans { get; } = orphans;

    public int Widows { get; } = widows;

    /// <summary>Returns the index of the first line box of the block container in a fragmentainer that starts at a position.</summary>
    public int GetFirstLine(double position)
    {
        var tops = Tops;
        for (var i = 0; i < tops.Length; i++)
        {
            if (tops[i] >= position - 0.01)
                return i;
        }

        return tops.Length;
    }
}
