using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The initial scroll position of the scroll containers (https://drafts.csswg.org/css-scroll-snap-2/#scroll-initial-target)
internal sealed partial class LayoutEngine
{
    /// <summary>The boxes of the last layout whose scroll-initial-target is <c>nearest</c>, in the order of the fragment tree.</summary>
    private List<ContainerBox>? _initialScrollTargetBoxes;

    /// <summary>
    /// Scrolls the scrolling boxes to their initial scroll position (https://drafts.csswg.org/css-scroll-snap-2/#initial-scroll-target):
    /// the scroll-into-view position of their initial scroll target, the first element in tree order whose scroll-initial-target
    /// is <c>nearest</c> and whose nearest scroll container is the box. As in the browsers, the position follows the target after
    /// each layout ("post-first layout arrivals": a target that appears, moves or changes its alignment) until a scroll operation
    /// scrolls the box (see <see cref="ScrollState.IsScrolledByOperation"/>), and the box is snapped from that position.
    /// </summary>
    /// <remarks>
    /// The specification aligns the target with <c>block: "start"</c> and <c>inline: "nearest"</c>; the tests (tentative) and
    /// the browsers align it with its scroll-snap-align in each axis, <c>start</c> when it is <c>none</c>.
    /// </remarks>
    private void ScrollToInitialScrollTargets()
    {
        if (_initialScrollTargetBoxes is not { Count: > 0 } boxes || _boxTree is not { } boxTree)
            return;

        HashSet<ScrollState>? scrolled = null;
        foreach (var box in boxes.ToArray())
        {
            if (box.Element is not { } element)
                continue;

            var principal = box.PseudoElement is { } pseudo ? boxTree.GetPseudoElementBox(element, pseudo) : boxTree.GetPrincipalBox(element);
            if (!ReferenceEquals(principal, box) || GetFirstFragment(box) is not BoxFragment first || FindNearestScrollContainer(first) is not { ScrollState: { } state } scrollContainer)
                continue;

            // "When multiple such elements or pseudo-elements exist, user-agents should select the one which comes first in tree order"
            if (!(scrolled ??= new(ReferenceEqualityComparer.Instance)).Add(state))
                continue;

            if (state.IsScrolledByOperation || state.SmoothScroll is not null || state.UserGestureStart is not null)
                continue;

            // The bounding box of the fragments of the box and of its continuations (an inline box split by a block-level box)
            var fragments = new List<BoxFragment>();
            for (var part = box; part is not null; part = part.Continuation)
            {
                for (var fragment = GetFirstFragment(part); fragment is not null; fragment = fragment.NextFragmentOfBox)
                {
                    if (fragment is BoxFragment boxFragment)
                    {
                        fragments.Add(boxFragment);
                    }
                }
            }

            var rect = ScrollIntoView.GetTargetRect(fragments);
            var scrollport = ScrollIntoView.GetScrollport(_document, scrollContainer, state);
            var align = box.Style.Layout.Overflow.ScrollSnapAlign;
            var delta = ScrollIntoView.GetScrollDelta(rect, scrollport, state.WritingDirection, ToLogicalPosition(align.Block), ToLogicalPosition(align.Inline));
            var position = state.Clamp(ScrollIntoView.RoundRoundingErrors(state.Offset + delta));
            position = GetSnapPosition(state, SnapSelectionStrategy.ForEndPosition(position, scrolledX: true, scrolledY: true));
            if (SetScrollPosition(state, position))
            {
                QueueScrollEndEvent(state);
            }
        }

        static ScrollLogicalPosition ToLogicalPosition(SnapAlignment alignment) => alignment switch
        {
            SnapAlignment.Center => ScrollLogicalPosition.Center,
            SnapAlignment.End => ScrollLogicalPosition.End,
            _ => ScrollLogicalPosition.Start,
        };
    }

    /// <summary>Returns the nearest scroll container of a fragment (the root fragment for the viewport), or null.</summary>
    private static BoxFragment? FindNearestScrollContainer(Fragment fragment)
    {
        for (var parent = fragment.Parent; parent is not null; parent = parent.Parent)
        {
            if (parent is BoxFragment { ScrollState: not null } scrollContainer)
                return scrollContainer;
        }

        return null;
    }
}
