namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The rules of the breaks of a paged fragmentation context (https://drafts.csswg.org/css-break-3/#break-between,
/// https://drafts.csswg.org/css-page-3/#using-named-pages): the values of <c>break-before</c> and <c>break-after</c> that force a
/// page break or avoid it, and the class A breaks forced by a change of the page name. The fragmentation of the multi-column
/// containers uses the column values instead: <see cref="FragmentationFlow.Collect"/> sets the rules of the flow it collects
/// (<see cref="Current"/>).
/// </summary>
internal sealed class PageBreakRules
{
    /// <summary>The rules of the flow being collected on this thread, or null for a flow of columns.</summary>
    [ThreadStatic]
    private static PageBreakRules? s_current;

    private readonly Dictionary<ContainerBox, string> _usedNames = new(ReferenceEqualityComparer.Instance);

    /// <summary>The rules of the flow being collected on this thread, or null for a flow of columns.</summary>
    internal static PageBreakRules? Current
    {
        get => s_current;
        set => s_current = value;
    }

    /// <summary>
    /// The fragment of the initial containing block (in the coordinates of the strip, without children): the containing block
    /// of the absolutely positioned boxes of the strip, which are fragmented apart (see <see cref="FragmentationFlow.OutOfFlowItems"/>).
    /// </summary>
    public BoxFragment? InitialContainingBlock { get; init; }

    /// <summary>
    /// Returns whether a value forces a page break: <c>page</c>, <c>left</c>, <c>right</c>, <c>recto</c>, <c>verso</c>, and
    /// <c>always</c> and <c>all</c> (the pages are the innermost fragmentation context of the content that is not in columns).
    /// </summary>
    public static bool IsForced(BreakBetween value) => value is BreakBetween.Page or BreakBetween.Left or BreakBetween.Right or BreakBetween.Recto or BreakBetween.Verso or BreakBetween.Always or BreakBetween.All;

    /// <summary>Returns whether a value avoids a page break (<c>avoid</c>, <c>avoid-page</c>).</summary>
    public static bool IsAvoid(BreakBetween value) => value is BreakBetween.Avoid or BreakBetween.AvoidPage;

    /// <summary>
    /// Returns whether the class A break between two in-flow siblings is forced because their page names differ
    /// (https://drafts.csswg.org/css-page-3/#using-named-pages): the end page value of the box before the break and the start
    /// page value of the box after it.
    /// </summary>
    public bool IsNameBreak(Fragment previous, Fragment next)
        => previous is BoxFragment previousBox && next is BoxFragment nextBox && !IsInOutOfFlowBox(previousBox) && !string.Equals(GetEndPageValue(previousBox), GetStartPageValue(nextBox), StringComparison.Ordinal);

    /// <summary>Returns whether a box is in an absolutely positioned box: as in the browsers, the page names of its contents do not break the pages.</summary>
    private static bool IsInOutOfFlowBox(BoxFragment box)
    {
        for (var ancestor = box.Parent; ancestor is not null; ancestor = ancestor.Parent)
        {
            if (ancestor is BoxFragment { Box.IsOutOfFlow: true } outOfFlow && PositionedLayout.IsAbsolutelyPositioned(outOfFlow.Box))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the used value of the <c>page</c> property of a box (https://drafts.csswg.org/css-page-3/#using-named-pages):
    /// <c>auto</c> is the used value of the parent box, the empty string for the root box.
    /// </summary>
    public string GetUsedPageValue(ContainerBox box)
    {
        if (_usedNames.TryGetValue(box, out var name))
            return name;

        var value = box.IsAnonymous ? "auto" : box.Style.GetComputedValue("page");
        name = value is "auto" or "" ? box.Parent is { } parent ? GetUsedPageValue(parent) : "" : value;
        _usedNames[box] = name;
        return name;
    }

    /// <summary>
    /// Returns the start page value of a box: the start page value of its first in-flow child box when it starts with one (a
    /// class A break point), else its used page value.
    /// </summary>
    public string GetStartPageValue(BoxFragment box)
    {
        while (GetEdgeChild(box, first: true) is { } child)
        {
            box = child;
        }

        return GetUsedPageValue(box.Box);
    }

    /// <summary>Returns the end page value of a box: the end page value of its last in-flow child box when it ends with one, else its used page value.</summary>
    public string GetEndPageValue(BoxFragment box)
    {
        while (GetEdgeChild(box, first: false) is { } child)
        {
            box = child;
        }

        return GetUsedPageValue(box.Box);
    }

    /// <summary>Returns the first (or last) in-flow child of a box when it is a block-level box of the block flow of the box, else null.</summary>
    internal static BoxFragment? GetEdgeChild(BoxFragment box, bool first)
    {
        if (FragmentationFlow.IsMonolithic(box, box.WritingDirection, scrollContainersAreMonolithic: false) || box.Box.Kind is not (ContainerBoxKind.Normal or ContainerBoxKind.Anonymous) || box.Box.Display.Inside is not (DisplayInside.Flow or DisplayInside.FlowRoot))
            return null;

        var children = box.ChildrenSpan;
        var count = Math.Min(box.InFlowChildCount, children.Length);
        for (var i = first ? 0 : count - 1; first ? i < count : i >= 0; i += first ? 1 : -1)
        {
            switch (children[i])
            {
                case BoxFragment { Box.IsOutOfFlow: true } or BoxFragment { Box.IsOutsideMarker: true }:
                    continue;
                case BoxFragment child when !child.Box.IsInlineBox && !child.Box.IsAtomicInline:
                    return child;
                default:
                    return null;
            }
        }

        return null;
    }
}
