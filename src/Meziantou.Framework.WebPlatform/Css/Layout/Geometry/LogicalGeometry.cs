using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

// The logical geometry of the formatting contexts (https://drafts.csswg.org/css-writing-modes-4/#logical-directions): the
// inline axis is the direction of the lines, the block axis the direction in which the blocks and the lines are stacked.
// The offsets are measured from the inline-start and block-start edges of the container. WritingDirection converts them to
// the physical geometry of the fragments.

/// <summary>A logical position: the distances from the inline-start and block-start edges of the container, in CSS px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LogicalOffset(double Inline, double Block)
{
    public static LogicalOffset Zero => default;

    public static LogicalOffset operator +(LogicalOffset left, LogicalOffset right) => new(left.Inline + right.Inline, left.Block + right.Block);

    public static LogicalOffset operator -(LogicalOffset left, LogicalOffset right) => new(left.Inline - right.Inline, left.Block - right.Block);

    public LogicalOffset Add(LogicalOffset other) => this + other;

    public LogicalOffset Subtract(LogicalOffset other) => this - other;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"(inline {Inline}, block {Block})");
}

/// <summary>A logical size: the inline size and the block size, in CSS px.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LogicalSize(double Inline, double Block)
{
    public static LogicalSize Zero => default;

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"(inline {Inline}, block {Block})");
}

/// <summary>A logical rectangle: its inline-start/block-start corner, relative to the container, and its size.</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LogicalRect(LogicalOffset Offset, LogicalSize Size)
{
    public double InlineStart => Offset.Inline;

    public double BlockStart => Offset.Block;

    public double InlineEnd => Offset.Inline + Size.Inline;

    public double BlockEnd => Offset.Block + Size.Block;
}

/// <summary>The four logical sides of a box (https://drafts.csswg.org/css-writing-modes-4/#logical-directions).</summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct LogicalSides<T>(T BlockStart, T InlineEnd, T BlockEnd, T InlineStart);

/// <summary>Helpers on the sides of a box.</summary>
internal static class SidesExtensions
{
    /// <summary>Returns the value of a physical side.</summary>
    public static T Get<T>(this Sides<T> sides, PhysicalSide side) => side switch
    {
        PhysicalSide.Top => sides.Top,
        PhysicalSide.Right => sides.Right,
        PhysicalSide.Bottom => sides.Bottom,
        _ => sides.Left,
    };

    /// <summary>Returns the sides with the value of a physical side replaced.</summary>
    public static Sides<T> With<T>(this Sides<T> sides, PhysicalSide side, T value) => side switch
    {
        PhysicalSide.Top => sides with { Top = value },
        PhysicalSide.Right => sides with { Right = value },
        PhysicalSide.Bottom => sides with { Bottom = value },
        _ => sides with { Left = value },
    };

    /// <summary>The sum of the inline sides.</summary>
    public static double InlineSum(this LogicalSides<double> sides) => sides.InlineStart + sides.InlineEnd;

    /// <summary>The sum of the block sides.</summary>
    public static double BlockSum(this LogicalSides<double> sides) => sides.BlockStart + sides.BlockEnd;

    /// <summary>The sum of the horizontal sides (left and right).</summary>
    public static double HorizontalSum(this Sides<double> sides) => sides.Left + sides.Right;

    /// <summary>The sum of the vertical sides (top and bottom).</summary>
    public static double VerticalSum(this Sides<double> sides) => sides.Top + sides.Bottom;

    /// <summary>Returns the sum of two sets of sides.</summary>
    public static Sides<double> Add(this Sides<double> left, Sides<double> right) => new(left.Top + right.Top, left.Right + right.Right, left.Bottom + right.Bottom, left.Left + right.Left);
}
