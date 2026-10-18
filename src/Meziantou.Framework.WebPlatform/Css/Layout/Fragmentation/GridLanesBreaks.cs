namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The fragmentainers of a grid lanes container whose stacking axis is the block axis of a fragmentation context, for its
/// placement (https://drafts.csswg.org/css-grid-3/#fragmentation): "Each grid axis track is fragmented independently in the
/// stacking axis [...] A grid item that is pushed, however, is placed again by the next grid container fragment" (see
/// <see cref="GridLanesFragmentation"/>).
/// </summary>
internal sealed class GridLanesBreaks
{
    private const double Epsilon = 0.01;

    /// <summary>
    /// The block-end edges of the fragmentainers where the container is, from the block-start edge of its content box in the strip
    /// (the next fragmentainer starts there).
    /// </summary>
    public required double[] Ends { get; init; }

    /// <summary>The block size of the fragmentainers after the last one of <see cref="Ends"/>.</summary>
    public required double NextBlockSize { get; init; }

    /// <summary>The fragmentation context is a multi-column container (else pages): the forced breaks are the column breaks.</summary>
    public bool IsColumns { get; init; } = true;

    /// <summary>
    /// Returns whether a value of <c>break-before</c>/<c>break-after</c> forces a break in the fragmentation context (the column
    /// values in a multi-column container, the page values in the pages, see <see cref="PageBreakRules"/>).
    /// </summary>
    public bool IsForcedBreak(BreakBetween value) => IsColumns ? value is BreakBetween.Column or BreakBetween.Always or BreakBetween.All : PageBreakRules.IsForced(value);

    /// <summary>Returns whether a value of <c>break-inside</c> avoids the breaks of the fragmentation context (<c>avoid</c>, <c>avoid-column</c> or <c>avoid-page</c>).</summary>
    public bool IsAvoidInside(BreakInside value) => value == BreakInside.Avoid || value == (IsColumns ? BreakInside.AvoidColumn : BreakInside.AvoidPage);

    /// <summary>Returns whether a box is monolithic in the fragmentation context (the scroll containers are not monolithic in the pages, see <see cref="FragmentationFlow.IsMonolithic(BoxFragment)"/>).</summary>
    public bool IsMonolithic(BoxFragment fragment, WritingDirection writingDirection) => FragmentationFlow.IsMonolithic(fragment, writingDirection, scrollContainersAreMonolithic: IsColumns);

    /// <summary>Returns the block-end edge of a fragmentainer, from the block-start edge of the content box of the container.</summary>
    public double GetEnd(int index)
    {
        if (index < Ends.Length)
            return Ends[index];

        if (Ends.Length == 0 || !double.IsFinite(NextBlockSize) || NextBlockSize <= Epsilon)
            return double.PositiveInfinity;

        return Ends[^1] + ((index - Ends.Length + 1) * NextBlockSize);
    }

    public bool IsSameAs(GridLanesBreaks other)
    {
        if (Ends.Length != other.Ends.Length || IsColumns != other.IsColumns || Math.Abs(NextBlockSize - other.NextBlockSize) > Epsilon && !(double.IsInfinity(NextBlockSize) && double.IsInfinity(other.NextBlockSize)))
            return false;

        for (var i = 0; i < Ends.Length; i++)
        {
            if (Math.Abs(Ends[i] - other.Ends[i]) > Epsilon)
                return false;
        }

        return true;
    }
}
