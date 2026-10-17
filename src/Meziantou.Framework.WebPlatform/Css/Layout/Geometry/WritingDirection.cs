using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A writing mode and a direction: the orientation of the logical axes of a box
/// (https://drafts.csswg.org/css-writing-modes-4/#logical-directions), and the conversions between its logical geometry (used by
/// the formatting contexts) and the physical geometry (used by the fragments, the CSSOM View APIs and the painting), as
/// defined by the abstract-to-physical mappings (https://drafts.csswg.org/css-writing-modes-4/#logical-to-physical).
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct WritingDirection(WritingMode WritingMode, Direction Direction)
{
    /// <summary>The initial writing mode and direction: <c>horizontal-tb</c> and <c>ltr</c>.</summary>
    public static WritingDirection HorizontalLtr => default;

    /// <summary>Returns the writing mode and the direction of a box.</summary>
    public static WritingDirection FromStyle(LayoutStyle style) => new(style.WritingMode, style.Direction);

    /// <summary>Whether the inline axis is horizontal (https://drafts.csswg.org/css-writing-modes-4/#horizontal-writing-mode).</summary>
    public bool IsHorizontal => WritingMode == WritingMode.HorizontalTb;

    /// <summary>Whether the inline axis is vertical (https://drafts.csswg.org/css-writing-modes-4/#vertical-writing-mode).</summary>
    public bool IsVertical => WritingMode != WritingMode.HorizontalTb;

    /// <summary>Whether the direction is right-to-left.</summary>
    public bool IsRightToLeft => Direction == Direction.Rtl;

    /// <summary>Whether the blocks are stacked from right to left (<c>vertical-rl</c>, <c>sideways-rl</c>).</summary>
    public bool IsFlippedBlocks => WritingMode is WritingMode.VerticalRl or WritingMode.SidewaysRl;

    /// <summary>Whether the inline axis of a box with this writing mode is orthogonal to the one of a box with another writing mode (https://drafts.csswg.org/css-writing-modes-4/#orthogonal-flows).</summary>
    public bool IsOrthogonalTo(WritingDirection other) => IsHorizontal != other.IsHorizontal;

    /// <summary>The physical side of the block-start edge.</summary>
    public PhysicalSide BlockStart => WritingMode switch
    {
        WritingMode.HorizontalTb => PhysicalSide.Top,
        WritingMode.VerticalRl or WritingMode.SidewaysRl => PhysicalSide.Right,
        _ => PhysicalSide.Left,
    };

    /// <summary>The physical side of the block-end edge.</summary>
    public PhysicalSide BlockEnd => Opposite(BlockStart);

    /// <summary>The physical side of the inline-start edge.</summary>
    public PhysicalSide InlineStart
    {
        get
        {
            var (start, end) = WritingMode switch
            {
                WritingMode.HorizontalTb => (PhysicalSide.Left, PhysicalSide.Right),

                // https://drafts.csswg.org/css-writing-modes-4/#valdef-writing-mode-sideways-lr: the lines go from bottom to top
                WritingMode.SidewaysLr => (PhysicalSide.Bottom, PhysicalSide.Top),
                _ => (PhysicalSide.Top, PhysicalSide.Bottom),
            };

            return IsRightToLeft ? end : start;
        }
    }

    /// <summary>The physical side of the inline-end edge.</summary>
    public PhysicalSide InlineEnd => Opposite(InlineStart);

    /// <summary>
    /// The physical side of the line-left edge (https://drafts.csswg.org/css-writing-modes-4/#line-left): the inline-start edge
    /// in the <c>ltr</c> direction, independent of the direction (the side <c>float: left</c> places a box on).
    /// </summary>
    public PhysicalSide LineLeft => WritingMode switch
    {
        WritingMode.HorizontalTb => PhysicalSide.Left,
        WritingMode.SidewaysLr => PhysicalSide.Bottom,
        _ => PhysicalSide.Top,
    };

    /// <summary>The physical side of the line-right edge.</summary>
    public PhysicalSide LineRight => Opposite(LineLeft);

    /// <summary>
    /// The physical side of the line-over edge (https://drafts.csswg.org/css-writing-modes-4/#line-over): the side the ascent
    /// of the text is on, the block-start side except in <c>vertical-lr</c> (the top of the glyphs faces right in both vertical modes).
    /// </summary>
    public PhysicalSide LineOver => WritingMode switch
    {
        WritingMode.HorizontalTb => PhysicalSide.Top,
        WritingMode.SidewaysLr => PhysicalSide.Left,
        _ => PhysicalSide.Right,
    };

    /// <summary>The physical side of the line-under edge (https://drafts.csswg.org/css-writing-modes-4/#line-under).</summary>
    public PhysicalSide LineUnder => Opposite(LineOver);

    /// <summary>Whether the line-over side is the block-end side (<c>vertical-lr</c>): the lines are flipped relative to the block flow.</summary>
    public bool IsLineOverBlockEnd => WritingMode == WritingMode.VerticalLr;

    /// <summary>Converts a physical size to the logical size in this writing mode.</summary>
    public LogicalSize ToLogical(PhysicalSize size) => IsHorizontal ? new(size.Width, size.Height) : new(size.Height, size.Width);

    /// <summary>Converts a logical size in this writing mode to the physical size.</summary>
    public PhysicalSize ToPhysical(LogicalSize size) => IsHorizontal ? new(size.Inline, size.Block) : new(size.Block, size.Inline);

    /// <summary>Converts the physical sides of a box to its logical sides in this writing mode.</summary>
    public LogicalSides<T> ToLogical<T>(Sides<T> sides) => new(sides.Get(BlockStart), sides.Get(InlineEnd), sides.Get(BlockEnd), sides.Get(InlineStart));

    /// <summary>Converts the logical sides of a box in this writing mode to its physical sides.</summary>
    public Sides<T> ToPhysical<T>(LogicalSides<T> sides)
    {
        var top = GetLogical(sides, PhysicalSide.Top);
        var right = GetLogical(sides, PhysicalSide.Right);
        var bottom = GetLogical(sides, PhysicalSide.Bottom);
        var left = GetLogical(sides, PhysicalSide.Left);
        return new Sides<T>(top, right, bottom, left);
    }

    /// <summary>
    /// Converts the logical position of a rectangle of size <paramref name="innerSize"/> in a container of size
    /// <paramref name="outerSize"/> (the distances from the inline-start and block-start edges of the container) to the physical
    /// position of its top-left corner relative to the top-left corner of the container.
    /// </summary>
    public PhysicalOffset ToPhysical(LogicalOffset offset, PhysicalSize outerSize, PhysicalSize innerSize)
    {
        double x;
        double y;
        if (IsHorizontal)
        {
            x = InlineStart == PhysicalSide.Left ? offset.Inline : outerSize.Width - offset.Inline - innerSize.Width;
            y = offset.Block;
        }
        else
        {
            x = BlockStart == PhysicalSide.Left ? offset.Block : outerSize.Width - offset.Block - innerSize.Width;
            y = InlineStart == PhysicalSide.Top ? offset.Inline : outerSize.Height - offset.Inline - innerSize.Height;
        }

        return new PhysicalOffset(x, y);
    }

    /// <summary>Converts the physical position of a rectangle in a container to its logical position (the inverse of <see cref="ToPhysical(LogicalOffset, PhysicalSize, PhysicalSize)"/>).</summary>
    public LogicalOffset ToLogical(PhysicalOffset offset, PhysicalSize outerSize, PhysicalSize innerSize)
    {
        if (IsHorizontal)
        {
            var inline = InlineStart == PhysicalSide.Left ? offset.X : outerSize.Width - offset.X - innerSize.Width;
            return new LogicalOffset(inline, offset.Y);
        }

        var block = BlockStart == PhysicalSide.Left ? offset.X : outerSize.Width - offset.X - innerSize.Width;
        var inlineOffset = InlineStart == PhysicalSide.Top ? offset.Y : outerSize.Height - offset.Y - innerSize.Height;
        return new LogicalOffset(inlineOffset, block);
    }

    /// <summary>Converts a logical rectangle in a container of size <paramref name="outerSize"/> to the physical rectangle.</summary>
    public PhysicalRect ToPhysical(LogicalRect rect, PhysicalSize outerSize)
    {
        var size = ToPhysical(rect.Size);
        return new PhysicalRect(ToPhysical(rect.Offset, outerSize, size), size);
    }

    /// <summary>Converts a physical rectangle in a container of size <paramref name="outerSize"/> to the logical rectangle.</summary>
    public LogicalRect ToLogical(PhysicalRect rect, PhysicalSize outerSize) => new(ToLogical(rect.Offset, outerSize, rect.Size), ToLogical(rect.Size));

    private T GetLogical<T>(LogicalSides<T> sides, PhysicalSide side)
    {
        if (side == BlockStart)
            return sides.BlockStart;

        if (side == BlockEnd)
            return sides.BlockEnd;

        return side == InlineStart ? sides.InlineStart : sides.InlineEnd;
    }

    /// <summary>Returns the opposite physical side.</summary>
    public static PhysicalSide Opposite(PhysicalSide side) => side switch
    {
        PhysicalSide.Top => PhysicalSide.Bottom,
        PhysicalSide.Right => PhysicalSide.Left,
        PhysicalSide.Bottom => PhysicalSide.Top,
        _ => PhysicalSide.Right,
    };
}
