using System.Buffers;
using System.Runtime.InteropServices;
using Meziantou.Framework.WebPlatform.Css.Fonts;
using Meziantou.Framework.WebPlatform.Unicode;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// The truncation of a line at its edges (https://drafts.csswg.org/css-overflow-4/#ellipsing-details), the rendering of
/// <c>text-overflow</c>: the characters (grapheme clusters) and the atomic inlines that overflow an edge where an ellipsis is
/// placed are hidden to make room for it, "the ellipsis is placed immediately adjacent to the end edge of the remaining inline
/// content", and "the first character or atomic inline-level element on a line must be clipped rather than ellipsed".
/// </summary>
/// <remarks>
/// <para>
/// The truncation is a rendering effect computed from the fragments of the line in visual order (the edges are along the line,
/// from its line-left edge: after the bidi reordering, the relative positioning and the scroll position): it does not change the
/// layout, the geometry APIs, the scrollable overflow nor the hit testing ("Ellipsing only affects rendering and must not affect
/// layout nor dispatching of pointer events"). The painting applies it (<see cref="PaintOrder"/>, <see cref="Painter"/>): the
/// hidden atomic inlines are not painted, the texts and the inline boxes of the line are clipped to the remaining content
/// (<see cref="LineTruncation.GetContentClip"/>), and the ellipses are painted after the content of the line.
/// </para>
/// <para>
/// <see cref="Truncate(LineBoxFragment, double, double, LineEllipsis?, LineEllipsis?)"/> is the reusable part: the truncation of a
/// line at two line-relative edges with the ellipses to place at them (<see cref="LineEllipsis"/>), e.g. for an ellipsis that is
/// not placed by the layout.
/// </para>
/// </remarks>
internal static class LineTruncator
{
    private const double Epsilon = 1e-6;

    /// <summary>The distance of the edge that truncates a line for its block overflow ellipsis from the end edge of the line box: the content that reaches the end edge overflows it.</summary>
    private const double BlockEllipsisMargin = 0.01;

    /// <summary>A unit of the content of a line that is hidden as a whole: a grapheme cluster of a text fragment, or an atomic inline.</summary>
    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Unit(double Left, double Right, Fragment Fragment);

    /// <summary>
    /// Returns the truncation of a line by the <c>text-overflow</c> of its block container
    /// (https://drafts.csswg.org/css-overflow-4/#text-overflow), or null when nothing is elided: the block container (the parent of
    /// an anonymous block container) clips its overflow in its inline axis, a side of its <c>text-overflow</c> elides
    /// (<c>ellipsis</c> or a &lt;string&gt;), and the content of the line overflows that edge of the line box, at the current scroll
    /// position ("implementations should [...] display additional content that fits, truncating only what would be clipped").
    /// </summary>
    public static LineTruncation? GetTextOverflow(LineBoxFragment line)
    {
        if (line.Children.Count == 0 || GetTextOverflowEdges(line) is not { } edges)
            return null;

        var (left, right, leftEdge, rightEdge, lineLeftSide) = edges;
        var units = new List<Unit>();
        CollectUnits(line, line, PhysicalOffset.Zero, lineLeftSide, units);
        if (units.Count == 0)
            return null;

        // The ellipses are shaped only for the edges that the content overflows
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var unit in units)
        {
            minimum = Math.Min(minimum, unit.Left);
            maximum = Math.Max(maximum, unit.Right);
        }

        var leftEllipsis = left.Elides && minimum < leftEdge - Epsilon ? LineEllipsis.Create(line, left.Kind == TextOverflowKind.String ? left.String ?? "" : null) : null;
        var rightEllipsis = right.Elides && maximum > rightEdge + Epsilon ? LineEllipsis.Create(line, right.Kind == TextOverflowKind.String ? right.String ?? "" : null) : null;
        if (leftEllipsis is null && rightEllipsis is null)
            return null;

        return Truncate(line, units, leftEdge, rightEdge, leftEllipsis, rightEllipsis, endIsLeft: line.IsRightToLeft);
    }

    /// <summary>
    /// Returns the sides of <c>text-overflow</c> that apply to a line (see <see cref="GetTextOverflow"/>) and the line-relative edges of
    /// its line box in the scrollport, or null when its block container does not elide.
    /// </summary>
    private static (TextOverflowSide Left, TextOverflowSide Right, double LeftEdge, double RightEdge, PhysicalSide LineLeftSide)? GetTextOverflowEdges(LineBoxFragment line)
    {
        if (line.Parent is not BoxFragment container || line.Style is null)
            return null;

        var owner = GetTextOverflowOwner(container);
        var textOverflow = owner.Box.Style.Layout.Overflow.TextOverflow;
        if (!textOverflow.Elides || !(owner.WritingDirection.IsHorizontal ? owner.ClipsOverflowX : owner.ClipsOverflowY))
            return null;

        // "During user interaction (for example, editing text in an editable element [...]), user agents may treat ellipsis
        // [...] as text-overflow: clip": as in the browsers, while the caret (the focus of the selection) is in an editable block
        // container
        if (owner.Box.Element is Dom.HtmlElement { IsContentEditable: true } element && element.NodeDocument.GetSelection()?.FocusNode is { } focus && element.IsInclusiveAncestorOf(focus))
            return null;

        // (and the value of the text control that has the focus)
        if (container.Box.IsTextControlInnerEditor && container.Box.Style.Element is { } control && ReferenceEquals(control.NodeDocument.FocusedArea, control))
            return null;

        var (left, right) = textOverflow.GetLineSides(line.IsRightToLeft);
        if (!left.Elides && !right.Elides)
            return null;

        // The edges of the line box in the scrollport: the contents are translated by the scroll position
        var lineLeftSide = new WritingDirection(container.WritingDirection.WritingMode, Direction.Ltr).InlineStart;
        var scroll = owner.IsScrollContainer && owner.ScrollState is { } state ? state.Offset : PhysicalOffset.Zero;
        var lineSize = line.Size;
        var (leftEdge, rightEdge) = lineLeftSide switch
        {
            PhysicalSide.Left => (scroll.X, lineSize.Width + scroll.X),
            PhysicalSide.Top => (scroll.Y, lineSize.Height + scroll.Y),
            _ => (-scroll.Y, lineSize.Height - scroll.Y),
        };

        return (left, right, leftEdge, rightEdge, lineLeftSide);
    }

    /// <summary>
    /// Returns the truncation of a line when it is painted: by <c>text-overflow</c> (<see cref="GetTextOverflow"/>), or by its block
    /// overflow ellipsis when its content does not fit with it (<see cref="LineBoxFragment.IsTruncatedByBlockEllipsis"/>: the content
    /// that reaches the end edge of the line box is truncated to make room for the ellipsis), or null.
    /// </summary>
    public static LineTruncation? GetTruncation(LineBoxFragment line)
    {
        var blockTruncation = GetBlockEllipsisTruncation(line);
        if (line.BlockEllipsis is { } blockEllipsis && GetTextOverflowOfBlockEllipsis(line, blockEllipsis, blockTruncation) is { } combined)
            return combined;

        return GetTextOverflow(line) ?? blockTruncation;
    }

    /// <summary>
    /// Returns the truncation of a line by <c>text-overflow</c> when its block overflow ellipsis overflows the edge of the line box
    /// (https://drafts.csswg.org/css-overflow-4/#line-clamp: the block ellipsis is placed first, then "if the line still overflows,
    /// text-overflow applies to the line including the block ellipsis"): the characters of the block ellipsis are elided as the
    /// content of the line, or null.
    /// </summary>
    private static LineTruncation? GetTextOverflowOfBlockEllipsis(LineBoxFragment line, LineEllipsis blockEllipsis, LineTruncation? blockTruncation)
    {
        if (GetTextOverflowEdges(line) is not { } edges)
            return null;

        var (left, right, leftEdge, rightEdge, lineLeftSide) = edges;
        var end = line.IsRightToLeft ? left : right;
        var overflows = line.IsRightToLeft ? blockEllipsis.LineLeft < leftEdge - Epsilon : blockEllipsis.LineLeft + blockEllipsis.InlineSize > rightEdge + Epsilon;
        if (!end.Elides || !overflows)
            return null;

        // The content that remains after the truncation by the block ellipsis, then the characters of the block ellipsis
        var units = new List<Unit>();
        var content = new List<Unit>();
        CollectUnits(line, line, PhysicalOffset.Zero, lineLeftSide, content);
        foreach (var unit in content)
        {
            if (blockTruncation is null || (!blockTruncation.IsHidden(unit.Fragment) && unit.Left >= blockTruncation.ClipLeft - Epsilon && unit.Right <= blockTruncation.ClipRight + Epsilon))
            {
                units.Add(unit);
            }
        }

        foreach (var fragment in blockEllipsis.Fragments)
        {
            if (fragment is { ShapedText: not null, GlyphCount: > 0 })
            {
                CollectTextUnits(line, fragment, fragment.Offset, lineLeftSide, units);
            }
        }

        var ellipsis = LineEllipsis.Create(line, end.Kind == TextOverflowKind.String ? end.String ?? "" : null);
        var truncation = line.IsRightToLeft
            ? Truncate(line, units, leftEdge, rightEdge, ellipsis, rightEllipsis: null, endIsLeft: true)
            : Truncate(line, units, leftEdge, rightEdge, leftEllipsis: null, ellipsis, endIsLeft: false);
        if (truncation is null)
            return null;

        return truncation.WithBlockEllipsis(blockEllipsis, blockTruncation);
    }

    /// <summary>Returns the truncation of a line by its block overflow ellipsis when its content does not fit with it (see <see cref="GetTruncation"/>), or null.</summary>
    private static LineTruncation? GetBlockEllipsisTruncation(LineBoxFragment line)
    {
        if (!line.IsTruncatedByBlockEllipsis || line.BlockEllipsis is not { } ellipsis)
            return null;

        var lineLeftSide = new WritingDirection(((BoxFragment)line.Parent!).WritingDirection.WritingMode, Direction.Ltr).InlineStart;
        var lineSize = lineLeftSide is PhysicalSide.Left ? line.Size.Width : line.Size.Height;
        var units = new List<Unit>();
        CollectUnits(line, line, PhysicalOffset.Zero, lineLeftSide, units);
        if (units.Count == 0)
            return null;

        // The content that does not overflow the line box reaches its end edge with the ellipsis: it overflows the edge moved by
        // a margin (the first unit is then kept, the ellipsis is placed after it)
        var minimum = double.PositiveInfinity;
        var maximum = double.NegativeInfinity;
        foreach (var unit in units)
        {
            minimum = Math.Min(minimum, unit.Left);
            maximum = Math.Max(maximum, unit.Right);
        }

        return line.IsRightToLeft
            ? Truncate(line, units, minimum < -Epsilon ? 0 : BlockEllipsisMargin, lineSize, ellipsis, rightEllipsis: null, endIsLeft: true)
            : Truncate(line, units, 0, maximum > lineSize + Epsilon ? lineSize : lineSize - BlockEllipsisMargin, leftEllipsis: null, ellipsis, endIsLeft: false);
    }

    /// <summary>
    /// Truncates a line at line-relative edges (distances along the line from the line-left edge of the line box): the content
    /// that overflows <paramref name="leftEdge"/> (<paramref name="rightEdge"/>) is elided to make room for
    /// <paramref name="leftEllipsis"/> (<paramref name="rightEllipsis"/>), which is placed next to the remaining content (see
    /// <see cref="LineEllipsis.Place"/>). An edge without ellipsis is not truncated. Returns null when no content overflows an
    /// edge with an ellipsis.
    /// </summary>
    public static LineTruncation? Truncate(LineBoxFragment line, double leftEdge, double rightEdge, LineEllipsis? leftEllipsis, LineEllipsis? rightEllipsis)
    {
        var units = new List<Unit>();
        CollectUnits(line, line, PhysicalOffset.Zero, new WritingDirection(((BoxFragment)line.Parent!).WritingDirection.WritingMode, Direction.Ltr).InlineStart, units);
        return Truncate(line, units, leftEdge, rightEdge, leftEllipsis, rightEllipsis, endIsLeft: line.IsRightToLeft);
    }

    private static LineTruncation? Truncate(LineBoxFragment line, List<Unit> units, double leftEdge, double rightEdge, LineEllipsis? leftEllipsis, LineEllipsis? rightEllipsis, bool endIsLeft)
    {
        // The units in visual order
        units.Sort(static (a, b) => a.Left != b.Left ? a.Left.CompareTo(b.Left) : a.Right.CompareTo(b.Right));
        var span = CollectionsMarshal.AsSpan(units);
        var states = new UnitState[span.Length];

        // "If there is insufficient space for both start and end ellipses/strings, only the end ellipsis/string renders": the end
        // edge is truncated first, then the start edge with the remaining content
        double? leftPosition = null;
        double? rightPosition = null;
        if (endIsLeft)
        {
            leftPosition = TruncateLeft(span, states, leftEdge, rightEdge, leftEllipsis);
            rightPosition = TruncateRight(span, states, leftEdge, rightEdge, rightEllipsis);
            if (leftPosition is { } start && rightPosition is { } end && start + leftEllipsis!.InlineSize > end + Epsilon)
            {
                Restore(states, UnitState.HiddenRight);
                rightPosition = null;
            }
        }
        else
        {
            rightPosition = TruncateRight(span, states, leftEdge, rightEdge, rightEllipsis);
            leftPosition = TruncateLeft(span, states, leftEdge, rightEdge, leftEllipsis);
            if (leftPosition is { } start && rightPosition is { } end && start + leftEllipsis!.InlineSize > end + Epsilon)
            {
                Restore(states, UnitState.HiddenLeft);
                leftPosition = null;
            }
        }

        if (leftPosition is null && rightPosition is null)
            return null;

        // The left ellipsis ends at the start of the remaining content, the right one starts at its end
        var clipLeft = double.NegativeInfinity;
        var clipRight = double.PositiveInfinity;
        if (leftPosition is { } leftContent)
        {
            leftEllipsis!.Place(line, leftContent - leftEllipsis.InlineSize);
            clipLeft = leftContent;
        }
        else
        {
            leftEllipsis = null;
        }

        if (rightPosition is { } rightContent)
        {
            rightEllipsis!.Place(line, rightContent);
            clipRight = rightContent;
        }
        else
        {
            rightEllipsis = null;
        }

        // The hidden atomic inlines are not painted; the texts are clipped to the remaining content, and not painted when none of
        // their characters is in it
        HashSet<Fragment>? hidden = null;
        Dictionary<Fragment, bool>? texts = null;
        for (var i = 0; i < span.Length; i++)
        {
            var unit = span[i];
            if (unit.Fragment is TextFragment text)
            {
                texts ??= new(ReferenceEqualityComparer.Instance);
                var visible = unit.Right > clipLeft + Epsilon && unit.Left < clipRight - Epsilon;
                texts[text] = texts.GetValueOrDefault(text) || visible;
            }
            else if (states[i] != UnitState.Visible)
            {
                (hidden ??= new(ReferenceEqualityComparer.Instance)).Add(unit.Fragment);
            }
        }

        if (texts is not null)
        {
            foreach (var (text, visible) in texts)
            {
                if (!visible)
                {
                    (hidden ??= new(ReferenceEqualityComparer.Instance)).Add(text);
                }
            }
        }

        return new LineTruncation(line, clipLeft, clipRight, leftEllipsis, rightEllipsis, hidden);
    }

    private enum UnitState : byte
    {
        Visible,
        HiddenRight,
        HiddenLeft,
    }

    private static void Restore(UnitState[] states, UnitState state)
    {
        for (var i = 0; i < states.Length; i++)
        {
            if (states[i] == state)
            {
                states[i] = UnitState.Visible;
            }
        }
    }

    /// <summary>
    /// Hides the visible units that overflow the right edge or the space of the ellipsis before it, but the first visible unit
    /// from the left edge, and returns the position of the end of the remaining content (where the ellipsis starts), or null when
    /// no visible unit overflows the right edge.
    /// </summary>
    private static double? TruncateRight(ReadOnlySpan<Unit> units, UnitState[] states, double leftEdge, double rightEdge, LineEllipsis? ellipsis)
    {
        if (ellipsis is null || !Overflows(units, states, rightEdge, right: true))
            return null;

        var limit = rightEdge - ellipsis.InlineSize;
        var first = -1;
        for (var i = 0; i < units.Length; i++)
        {
            if (states[i] == UnitState.Visible && units[i].Right > leftEdge + Epsilon)
            {
                first = i;
                break;
            }
        }

        var end = double.NegativeInfinity;
        for (var i = 0; i < units.Length; i++)
        {
            if (states[i] != UnitState.Visible)
                continue;

            if (i != first && units[i].Right > limit + Epsilon)
            {
                states[i] = UnitState.HiddenRight;
            }
            else
            {
                end = Math.Max(end, units[i].Right);
            }
        }

        return double.IsNegativeInfinity(end) ? limit : end;
    }

    /// <summary>The mirror of <see cref="TruncateRight"/>: returns the position of the start of the remaining content, where the left ellipsis ends.</summary>
    private static double? TruncateLeft(ReadOnlySpan<Unit> units, UnitState[] states, double leftEdge, double rightEdge, LineEllipsis? ellipsis)
    {
        if (ellipsis is null || !Overflows(units, states, leftEdge, right: false))
            return null;

        var limit = leftEdge + ellipsis.InlineSize;
        var first = -1;
        for (var i = units.Length - 1; i >= 0; i--)
        {
            if (states[i] == UnitState.Visible && units[i].Left < rightEdge - Epsilon)
            {
                first = i;
                break;
            }
        }

        var start = double.PositiveInfinity;
        for (var i = 0; i < units.Length; i++)
        {
            if (states[i] != UnitState.Visible)
                continue;

            if (i != first && units[i].Left < limit - Epsilon)
            {
                states[i] = UnitState.HiddenLeft;
            }
            else
            {
                start = Math.Min(start, units[i].Left);
            }
        }

        return double.IsPositiveInfinity(start) ? limit : start;
    }

    private static bool Overflows(ReadOnlySpan<Unit> units, UnitState[] states, double edge, bool right)
    {
        for (var i = 0; i < units.Length; i++)
        {
            if (states[i] == UnitState.Visible && (right ? units[i].Right > edge + Epsilon : units[i].Left < edge - Epsilon))
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the box whose <c>text-overflow</c> applies to the lines of a block container: the block container, or the parent of
    /// an anonymous block container in a block formatting context (the anonymous block boxes that wrap the inline content of a block
    /// container with block-level children, as in Blink; not the anonymous flex and grid items).
    /// </summary>
    private static BoxFragment GetTextOverflowOwner(BoxFragment container)
    {
        if (container.Parent is BoxFragment { Box: { } parentBox } parent && ReferenceEquals(parentBox, GetTextOverflowOwner(container.Box)) && !ReferenceEquals(parentBox, container.Box))
        {
            return parent;
        }

        return container;
    }

    /// <summary>Returns the box whose <c>text-overflow</c> applies to the lines of a block container (see <see cref="GetTextOverflowOwner(BoxFragment)"/>).</summary>
    private static ContainerBox GetTextOverflowOwner(ContainerBox block)
        => block.IsAnonymous && block.Parent is { Kind: not ContainerBoxKind.Viewport } parent && (FormattingContext.GetKind(parent) == FormattingContextKind.Block || IsTextAreaInnerEditor(block, parent)) ? parent : block;

    /// <summary>Whether a block is the inner editor of a <c>textarea</c>, which clips and scrolls its lines (the inner editor of a text field clips its own line).</summary>
    private static bool IsTextAreaInnerEditor(ContainerBox block, ContainerBox parent) => block.IsTextControlInnerEditor && TextControlLayout.IsMultiline(parent);

    /// <summary>Collects the units of the content of a line (or of an inline box of the line), with their line-relative positions.</summary>
    private static void CollectUnits(LineBoxFragment line, Fragment parent, PhysicalOffset origin, PhysicalSide lineLeft, List<Unit> units)
    {
        foreach (var child in parent.ChildrenSpan)
        {
            var offset = origin + child.Offset;
            switch (child)
            {
                case TextFragment { ShapedText: not null, GlyphCount: > 0 } text:
                    CollectTextUnits(line, text, offset, lineLeft, units);
                    break;

                case BoxFragment box when box.Box.IsOutOfFlow:
                    break;

                case BoxFragment { Box.IsInlineBox: true } inlineBox:
                    CollectUnits(line, inlineBox, offset, lineLeft, units);
                    break;

                case BoxFragment atomic:
                {
                    var (left, right) = ToLineRange(line, new PhysicalRect(offset, atomic.Size), lineLeft);
                    units.Add(new Unit(left, right, atomic));
                    break;
                }
            }
        }
    }

    /// <summary>Collects the grapheme clusters of a text fragment (https://drafts.csswg.org/css-text-4/#characters: the typographic character units).</summary>
    private static void CollectTextUnits(LineBoxFragment line, TextFragment text, PhysicalOffset offset, PhysicalSide lineLeft, List<Unit> units)
    {
        var (fragmentLeft, fragmentRight) = ToLineRange(line, new PhysicalRect(offset, text.Size), lineLeft);
        var forward = text.WritingDirection.InlineStart == lineLeft;
        var glyphs = text.Glyphs;
        var shapedStart = glyphs[0].Cluster;

        // The extended grapheme cluster boundaries of the characters of the fragment: a cluster of the shaping that does not start
        // at a boundary (e.g. a spacing mark) belongs to the previous unit
        var data = text.Run.Data;
        var domStart = Math.Clamp(text.Start, 0, data.Length);
        var length = Math.Clamp(text.End, domStart, data.Length) - domStart;
        var rented = length > 256 ? ArrayPool<bool>.Shared.Rent(length) : null;
        var boundaries = rented is null ? stackalloc bool[length] : rented.AsSpan(0, length);
        GraphemeClusters.GetBoundaries(data.AsSpan(domStart, length), boundaries);

        var pen = 0d;
        var unitStart = 0d;
        var hasUnit = false;
        for (var i = 0; i < glyphs.Length; i++)
        {
            var glyph = glyphs[i];
            if (i > 0 && (glyph.Flags & ShapedGlyphFlags.ClusterStart) != ShapedGlyphFlags.None && glyph.Cluster != glyphs[i - 1].Cluster && IsBoundary(text, glyph.Cluster - shapedStart, domStart, boundaries))
            {
                Add(unitStart, pen);
                unitStart = pen;
            }

            pen += glyph.Advance;
            hasUnit = true;
        }

        if (hasUnit)
        {
            Add(unitStart, pen);
        }

        if (rented is not null)
        {
            ArrayPool<bool>.Shared.Return(rented);
        }

        void Add(double start, double end)
        {
            units.Add(forward ? new Unit(fragmentLeft + start, fragmentLeft + end, text) : new Unit(fragmentRight - end, fragmentRight - start, text));
        }

        static bool IsBoundary(TextFragment text, int index, int domStart, ReadOnlySpan<bool> boundaries)
        {
            var domOffset = text.DomOffsets is { } map ? (index < map.Length ? map[index] : -1) : text.Start + index;
            var position = domOffset - domStart;
            return position <= 0 || position >= boundaries.Length || boundaries[position];
        }
    }

    /// <summary>Returns the line-relative range (from the line-left edge of the line box) covered by a rectangle in the coordinates of the line box.</summary>
    internal static (double Left, double Right) ToLineRange(LineBoxFragment line, PhysicalRect rect, PhysicalSide lineLeft) => lineLeft switch
    {
        PhysicalSide.Left => (rect.Left, rect.Right),
        PhysicalSide.Top => (rect.Top, rect.Bottom),
        PhysicalSide.Bottom => (line.Size.Height - rect.Bottom, line.Size.Height - rect.Top),
        _ => (line.Size.Width - rect.Right, line.Size.Width - rect.Left),
    };
}

/// <summary>
/// The truncation of a line (<see cref="LineTruncator"/>): the range of the line where its content is visible, the ellipses placed
/// at its edges, and the fragments of the line that are not painted.
/// </summary>
internal sealed class LineTruncation
{
    private readonly HashSet<Fragment>? _hidden;

    internal LineTruncation(LineBoxFragment line, double clipLeft, double clipRight, LineEllipsis? leftEllipsis, LineEllipsis? rightEllipsis, HashSet<Fragment>? hidden)
    {
        Line = line;
        ClipLeft = clipLeft;
        ClipRight = clipRight;
        LeftEllipsis = leftEllipsis;
        RightEllipsis = rightEllipsis;
        _hidden = hidden;
    }

    /// <summary>
    /// The block overflow ellipsis of the line when it is truncated by <c>text-overflow</c> as the content of the line, or null: its
    /// fragments are painted with <see cref="BlockEllipsisTruncation"/>.
    /// </summary>
    public LineEllipsis? BlockEllipsis { get; private init; }

    /// <summary>The truncation of the fragments of <see cref="BlockEllipsis"/> (the content before it is also truncated by the block ellipsis).</summary>
    public LineTruncation? BlockEllipsisTruncation { get; private init; }

    /// <summary>
    /// Returns the truncation by <c>text-overflow</c> of a line whose block ellipsis is elided as its content: the content is
    /// truncated by both the block ellipsis (<paramref name="blockTruncation"/>, or null) and this truncation.
    /// </summary>
    internal LineTruncation WithBlockEllipsis(LineEllipsis blockEllipsis, LineTruncation? blockTruncation)
    {
        var hidden = _hidden;
        if (blockTruncation?._hidden is { } blockHidden)
        {
            hidden = hidden is null ? blockHidden : [.. hidden, .. blockHidden];
        }

        var clipLeft = Math.Max(ClipLeft, blockTruncation?.ClipLeft ?? double.NegativeInfinity);
        var clipRight = Math.Min(ClipRight, blockTruncation?.ClipRight ?? double.PositiveInfinity);
        return new LineTruncation(Line, clipLeft, clipRight, LeftEllipsis, RightEllipsis, hidden) { BlockEllipsis = blockEllipsis, BlockEllipsisTruncation = this };
    }

    public LineBoxFragment Line { get; }

    /// <summary>The line-relative position (from the line-left edge of the line box) before which the content is clipped (the end of the left ellipsis), or negative infinity.</summary>
    public double ClipLeft { get; }

    /// <summary>The line-relative position after which the content is clipped (the start of the right ellipsis), or positive infinity.</summary>
    public double ClipRight { get; }

    /// <summary>The ellipsis placed at the line-left edge, or null.</summary>
    public LineEllipsis? LeftEllipsis { get; }

    /// <summary>The ellipsis placed at the line-right edge, or null.</summary>
    public LineEllipsis? RightEllipsis { get; }

    /// <summary>Whether a fragment of the line is not painted: an atomic inline hidden to make room for an ellipsis, or a text whose characters are all elided.</summary>
    public bool IsHidden(Fragment fragment) => _hidden is not null && _hidden.Contains(fragment);

    /// <summary>
    /// Returns the clip of the texts and of the inline boxes of the line in the coordinates of the line box: the visible range along
    /// the line, unbounded (<paramref name="extent"/>) in the block axis and on the sides that are not truncated.
    /// </summary>
    public PhysicalRect GetContentClip(double extent)
    {
        var left = double.IsNegativeInfinity(ClipLeft) ? -extent : ClipLeft;
        var right = double.IsPositiveInfinity(ClipRight) ? extent : ClipRight;
        var size = Line.Size;
        var parent = (BoxFragment)Line.Parent!;
        return new WritingDirection(parent.WritingDirection.WritingMode, Direction.Ltr).InlineStart switch
        {
            PhysicalSide.Left => new PhysicalRect(left, -extent, right - left, 2 * extent),
            PhysicalSide.Top => new PhysicalRect(-extent, left, 2 * extent, right - left),
            PhysicalSide.Bottom => new PhysicalRect(-extent, size.Height - right, 2 * extent, right - left),
            _ => new PhysicalRect(size.Width - right, -extent, right - left, 2 * extent),
        };
    }
}
