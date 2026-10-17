using Meziantou.Framework.WebPlatform.Css.Layout;
using Meziantou.Framework.WebPlatform.Css.Om;

namespace Meziantou.Framework.WebPlatform.Dom;

/// <summary>https://drafts.csswg.org/cssom-view/#dictdef-caretpositionfrompointoptions</summary>
[IdlInterface("CaretPositionFromPointOptions", "https://drafts.csswg.org/cssom-view/#dictdef-caretpositionfrompointoptions")]
public sealed class CaretPositionFromPointOptions
{
    /// <summary>Gets the shadow roots whose positions may be returned (the positions in the other shadow trees are retargeted to their shadow hosts).</summary>
    [IdlMember("shadowRoots", "https://drafts.csswg.org/cssom-view/#dom-caretpositionfrompointoptions-shadowroots")]
    public IReadOnlyList<ShadowRoot> ShadowRoots { get; init; } = [];
}

/// <summary>
/// A caret position (https://drafts.csswg.org/cssom-view/#caretposition): a node and an offset in it, returned by
/// <see cref="Document.CaretPositionFromPoint"/>.
/// </summary>
[IdlInterface("CaretPosition", "https://drafts.csswg.org/cssom-view/#caretposition")]
public sealed class CaretPosition
{
    internal CaretPosition(Node offsetNode, uint offset, PhysicalRect? textControlCaret = null)
    {
        OffsetNode = offsetNode;
        Offset = offset;
        TextControlCaret = textControlCaret;
    }

    /// <summary>The rectangle of the caret in a text entry widget, in the coordinates of the viewport (there is no caret range).</summary>
    private PhysicalRect? TextControlCaret { get; }

    /// <summary>Gets the node of the caret position (https://drafts.csswg.org/cssom-view/#dom-caretposition-offsetnode).</summary>
    [IdlMember("offsetNode", "https://drafts.csswg.org/cssom-view/#dom-caretposition-offsetnode")]
    public Node OffsetNode { get; }

    /// <summary>Gets the offset of the caret position in its node (https://drafts.csswg.org/cssom-view/#dom-caretposition-offset).</summary>
    [IdlMember("offset", "https://drafts.csswg.org/cssom-view/#dom-caretposition-offset")]
    public uint Offset { get; }

    /// <summary>
    /// Returns the rectangle of the caret (https://drafts.csswg.org/cssom-view/#dom-caretposition-getclientrect): the bounding
    /// rectangle of the collapsed range of the caret position (see <see cref="Range.GetBoundingClientRect"/>), or null when the
    /// node is not in a document anymore.
    /// </summary>
    [IdlMember("getClientRect", "https://drafts.csswg.org/cssom-view/#dom-caretposition-getclientrect")]
    public DomRect? GetClientRect()
    {
        if (TextControlCaret is { } caret)
            return new DomRect(caret.X, caret.Y, caret.Width, caret.Height);

        if (OffsetNode.GetShadowIncludingRoot() is not Document || Offset > Range.GetNodeLength(OffsetNode))
            return null;

        var range = new Range(OffsetNode, Offset, OffsetNode, Offset);
        return range.GetBoundingClientRect();
    }
}

/// <summary>The caret positions at the points of the viewport (https://drafts.csswg.org/cssom-view/#dom-document-caretpositionfrompoint).</summary>
internal static class CaretPositions
{
    /// <summary>
    /// Returns the caret position at a point of the viewport (in the shadow trees too), or null: the position in the text under
    /// the point; for a box, the position in its closest text (the text whose line contains the point, the closest one along
    /// the inline axis), else before or after a replaced element (the half that contains the point), else the start of the
    /// element. The point of an empty area is a position in the root element.
    /// </summary>
    public static (Node Node, uint Offset, PhysicalRect? Caret)? FromPoint(Document document, double x, double y)
    {
        var engine = document.GetLayoutEngine();
        if (engine.EnsureLayout() is null)
            return null;

        var point = new PhysicalOffset(x, y);
        Fragment fragment;
        if (HitTesting.HitTest(engine, point) is { } hit)
        {
            // An element of an SVG document fragment: its start
            if (hit.Target is SvgElement svgElement)
                return (svgElement, 0, null);

            fragment = hit.Fragment;
            if (fragment is TextFragment text && text.Run.Node is { } textNode)
                return (textNode, (uint)text.GetDomOffsetAt(GetInlineDistance(text, hit.LocalPoint)), null);

            // "If at the coordinates x,y in the viewport a text insertion point indicator would have been inserted in a text entry
            // widget which is also a replaced element": the position in the value of the widget
            // (the fragment under the point is the control, or a fragment of its inner editor or of its placeholder)
            if (GetTextControlFragment(fragment) is { Box.Element: { } controlElement } control && GetTextControlPosition(engine, control, point) is var (offset, caret))
                return (controlElement, offset, caret);
        }
        else
        {
            // An empty area of the viewport: a position in the root element
            if (document.DocumentElement is not { } documentElement || engine.GetFragments(documentElement) is not [var rootFragment, ..])
                return null;

            fragment = rootFragment;
        }

        var element = fragment.Box!.Element!;
        var rootPoint = engine.FromViewport(point);
        if (FindClosestText(fragment, rootPoint) is var (closest, local))
            return (closest.Run.Node!, (uint)closest.GetDomOffsetAt(GetInlineDistance(closest, local)), null);

        // A replaced element (e.g. an image): the position before or after it
        if (fragment is BoxFragment { Box.Kind: ContainerBoxKind.Replaced } replaced && element.ParentNode is { } parent && LayoutEngine.TryMapFromRoot(replaced, rootPoint, out var replacedPoint))
        {
            var after = replaced.WritingDirection.IsHorizontal ? replacedPoint.X > replaced.Size.Width / 2 : replacedPoint.Y > replaced.Size.Height / 2;
            return (parent, (uint)(element.GetIndex() + (after ? 1 : 0)), null);
        }

        return (element, 0, null);
    }

    /// <summary>
    /// Returns the caret position in the value of a text entry widget (a single-line text field or a textarea) at a point in the
    /// coordinates of the viewport, and the rectangle of the caret in the coordinates of the viewport, or null for the other
    /// elements: the position in the line boxes of its inner editor (see <see cref="TextControlLayout.GetCaretPosition"/>).
    /// </summary>
    private static (uint Offset, PhysicalRect Caret)? GetTextControlPosition(LayoutEngine engine, BoxFragment fragment, PhysicalOffset point)
    {
        if (TextControlLayout.GetCaretPosition(fragment, engine.FromViewport(point)) is not var (offset, caret))
            return null;

        return ((uint)offset, engine.ToViewport(caret));
    }

    /// <summary>Returns the fragment of the replaced box of the text control a fragment is in (the fragment itself, or the one that contains its inner editor or its placeholder), or null.</summary>
    private static BoxFragment? GetTextControlFragment(Fragment fragment)
    {
        for (var current = fragment; current is not null; current = current.Parent)
        {
            if (current is BoxFragment { Box.Kind: ContainerBoxKind.Replaced } replaced)
                return TextControlLayout.HasInnerEditor(replaced.Box.Element) && replaced.Box.PseudoElement is null ? replaced : null;

            if (current is BoxFragment { Box: { IsTextControlInnerEditor: false, PseudoElement: not TextControlLayout.PlaceholderPseudoElement } })
                return null;
        }

        return null;
    }

    /// <summary>The distance along the inline axis from the inline-start edge of a text fragment (in the direction of its glyphs) to a point in its coordinates.</summary>
    private static double GetInlineDistance(TextFragment fragment, PhysicalOffset point) => fragment.WritingDirection.InlineStart switch
    {
        PhysicalSide.Left => point.X,
        PhysicalSide.Right => fragment.Size.Width - point.X,
        PhysicalSide.Top => point.Y,
        _ => fragment.Size.Height - point.Y,
    };

    /// <summary>
    /// Returns the text fragment of a text node in the subtree of a fragment that is the closest to a point (relative to the
    /// root of the fragment tree): the one whose block axis range contains the point, or the closest along the block axis, then
    /// the closest along the inline axis; and the point in its coordinates.
    /// </summary>
    private static (TextFragment Fragment, PhysicalOffset Point)? FindClosestText(Fragment container, PhysicalOffset rootPoint)
    {
        (TextFragment Fragment, PhysicalOffset Point)? best = null;
        var bestBlock = double.PositiveInfinity;
        var bestInline = double.PositiveInfinity;
        var stack = new Stack<Fragment>();
        stack.Push(container);
        while (stack.TryPop(out var fragment))
        {
            if (fragment is TextFragment { Run.Node: not null, IsHyphen: false } text)
            {
                if (!LayoutEngine.TryMapFromRoot(text, rootPoint, out var local))
                    continue;

                var horizontal = text.WritingDirection.IsHorizontal;
                var block = horizontal ? Distance(local.Y, text.Size.Height) : Distance(local.X, text.Size.Width);
                var inline = horizontal ? Distance(local.X, text.Size.Width) : Distance(local.Y, text.Size.Height);
                if (block < bestBlock || (block == bestBlock && inline < bestInline))
                {
                    best = (text, local);
                    bestBlock = block;
                    bestInline = inline;
                }

                continue;
            }

            var children = fragment.ChildrenSpan;
            for (var i = children.Length - 1; i >= 0; i--)
            {
                stack.Push(children[i]);
            }
        }

        return best;

        // The distance from a coordinate to the range [0, size]
        static double Distance(double value, double size) => value < 0 ? -value : value > size ? value - size : 0;
    }
}
