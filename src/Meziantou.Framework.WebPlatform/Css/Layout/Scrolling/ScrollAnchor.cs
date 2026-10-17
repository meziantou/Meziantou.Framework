using Meziantou.Framework.WebPlatform.Dom;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The anchor node of a scrolling box (https://drafts.csswg.org/css-scroll-anchoring/#anchor-node-selection) and the position
/// of its block-start edge saved before a layout pass, used to adjust the scroll position after the pass
/// (https://drafts.csswg.org/css-scroll-anchoring/#scroll-adjustment).
/// </summary>
/// <remarks>
/// The anchor is identified by its node (an element, a pseudo-element of an element, or a text node) rather than by its box:
/// the box tree is built again when the styles change. It stays valid until it is invalidated
/// (https://drafts.csswg.org/css-scroll-anchoring/#anchor-node-invalidation): an explicit change of the scroll position of the
/// box or of a scrolling box it contains, a suppressed adjustment, or the removal of its box.
/// </remarks>
internal sealed class ScrollAnchor
{
    public ScrollAnchor(Node node, string? pseudoElement, Element? priorityCandidate, double blockStart)
    {
        Node = node;
        PseudoElement = pseudoElement;
        PriorityCandidate = priorityCandidate;
        SavedBlockStart = blockStart;
    }

    /// <summary>The anchor node: an element (or the originating element of <see cref="PseudoElement"/>), or a text node.</summary>
    public Node Node { get; }

    /// <summary>The pseudo-element of <see cref="Node"/> whose box is the anchor, or null.</summary>
    public string? PseudoElement { get; }

    /// <summary>
    /// The focused editable element that was the priority candidate when the anchor was selected
    /// (https://drafts.csswg.org/css-scroll-anchoring/#anchor-priority-candidates), or null: the anchor is selected again when
    /// the priority candidate changes.
    /// </summary>
    public Element? PriorityCandidate { get; }

    /// <summary>
    /// The position of the block-start edge of the scroll anchoring bounding rect of the anchor, in the physical axis of the block
    /// flow direction of the scrolling box, relative to its visible contents (the coordinates of the scroll container minus its
    /// scroll position, from the block-start edge of its scrollport) when the anchor was selected. The adjustment keeps the edge
    /// at this position.
    /// </summary>
    public double SavedBlockStart { get; }

    /// <summary>Whether the anchor was selected while removed nodes were skipped: it is selected again after the adjustment of the pass.</summary>
    internal bool ReselectAfterAdjustment { get; init; }

    /// <summary>Whether an adjustment is queued for the end of the current layout pass (the anchor node may move).</summary>
    internal bool Queued { get; set; }

    /// <summary>
    /// Whether a suppression trigger (https://drafts.csswg.org/css-scroll-anchoring/#suppression-triggers) occurred since the
    /// previous layout pass: the adjustment of the pass is suppressed.
    /// </summary>
    internal bool Suppressed { get; set; }
}
