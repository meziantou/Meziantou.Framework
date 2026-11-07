using Meziantou.Framework.WebPlatform.Css.Layout;

namespace Meziantou.Framework.WebPlatform.Tests.Css.Layout;

public sealed class WritingDirectionTests
{
    [Theory]
    [InlineData(WritingMode.HorizontalTb, Direction.Ltr, PhysicalSide.Top, PhysicalSide.Left)]
    [InlineData(WritingMode.HorizontalTb, Direction.Rtl, PhysicalSide.Top, PhysicalSide.Right)]
    [InlineData(WritingMode.VerticalRl, Direction.Ltr, PhysicalSide.Right, PhysicalSide.Top)]
    [InlineData(WritingMode.VerticalRl, Direction.Rtl, PhysicalSide.Right, PhysicalSide.Bottom)]
    [InlineData(WritingMode.VerticalLr, Direction.Ltr, PhysicalSide.Left, PhysicalSide.Top)]
    [InlineData(WritingMode.SidewaysRl, Direction.Ltr, PhysicalSide.Right, PhysicalSide.Top)]
    [InlineData(WritingMode.SidewaysLr, Direction.Ltr, PhysicalSide.Left, PhysicalSide.Bottom)]
    [InlineData(WritingMode.SidewaysLr, Direction.Rtl, PhysicalSide.Left, PhysicalSide.Top)]
    internal void Sides(WritingMode writingMode, Direction direction, PhysicalSide blockStart, PhysicalSide inlineStart)
    {
        var writingDirection = new WritingDirection(writingMode, direction);
        Assert.Equal(blockStart, writingDirection.BlockStart);
        Assert.Equal(inlineStart, writingDirection.InlineStart);

        // The logical sides round-trip
        var physical = new Sides<int>(1, 2, 3, 4);
        var logical = writingDirection.ToLogical(physical);
        Assert.Equal(physical, writingDirection.ToPhysical(logical));
        Assert.Equal(physical.Get(blockStart), logical.BlockStart);
        Assert.Equal(physical.Get(inlineStart), logical.InlineStart);
    }

    [Theory]
    [InlineData(WritingMode.HorizontalTb, Direction.Ltr, 10, 20)]
    [InlineData(WritingMode.HorizontalTb, Direction.Rtl, 70, 20)]
    [InlineData(WritingMode.VerticalRl, Direction.Ltr, 60, 10)]
    [InlineData(WritingMode.VerticalRl, Direction.Rtl, 60, 150)]
    [InlineData(WritingMode.VerticalLr, Direction.Ltr, 20, 10)]
    [InlineData(WritingMode.SidewaysLr, Direction.Ltr, 20, 150)]
    internal void Offsets(WritingMode writingMode, Direction direction, double x, double y)
    {
        // A 20x40 (physical) rectangle at inline offset 10 and block offset 20 in a 100x200 container
        var writingDirection = new WritingDirection(writingMode, direction);
        var outer = new PhysicalSize(100, 200);
        var inner = new PhysicalSize(20, 40);
        var offset = writingDirection.ToPhysical(new LogicalOffset(10, 20), outer, inner);
        Assert.Equal(new PhysicalOffset(x, y), offset);
        Assert.Equal(new LogicalOffset(10, 20), writingDirection.ToLogical(offset, outer, inner));

        var logicalRect = writingDirection.ToLogical(new PhysicalRect(offset, inner), outer);
        Assert.Equal(new LogicalOffset(10, 20), logicalRect.Offset);
        Assert.Equal(writingDirection.ToLogical(inner), logicalRect.Size);
        Assert.Equal(new PhysicalRect(offset, inner), writingDirection.ToPhysical(logicalRect, outer));
    }

    [Fact]
    public void Sizes()
    {
        var vertical = new WritingDirection(WritingMode.VerticalRl, Direction.Ltr);
        Assert.Equal(new LogicalSize(200, 100), vertical.ToLogical(new PhysicalSize(100, 200)));
        Assert.Equal(new PhysicalSize(100, 200), vertical.ToPhysical(new LogicalSize(200, 100)));
        Assert.True(vertical.IsOrthogonalTo(WritingDirection.HorizontalLtr));
        Assert.False(vertical.IsOrthogonalTo(new WritingDirection(WritingMode.VerticalLr, Direction.Rtl)));
    }
}
