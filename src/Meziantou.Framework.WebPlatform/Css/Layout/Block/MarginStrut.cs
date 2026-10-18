using System.Globalization;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>
/// A set of adjoining margins being collapsed (https://drafts.csswg.org/css2/#collapsing-margins): "When two or more margins
/// collapse, the resulting margin width is the maximum of the collapsing margins' widths. In the case of negative margins,
/// the maximum of the absolute values of the negative adjoining margins is deducted from the maximum of the positive
/// adjoining margins. If there are no positive margins, the maximum of the absolute values of the adjoining margins is
/// deducted from zero."
/// </summary>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct MarginStrut
{
    private MarginStrut(double positive, double negative)
    {
        Positive = positive;
        Negative = negative;
    }

    /// <summary>The largest positive margin of the set (0 when there is none).</summary>
    public double Positive { get; }

    /// <summary>The most negative margin of the set (0 when there is none).</summary>
    public double Negative { get; }

    /// <summary>The width of the collapsed margin.</summary>
    public double Sum => Positive + Negative;

    /// <summary>Whether the set contains no non-zero margin.</summary>
    public bool IsZero => Positive == 0 && Negative == 0;

    /// <summary>Returns the set made of one margin.</summary>
    public static MarginStrut FromMargin(double margin) => margin >= 0 ? new MarginStrut(margin, 0) : new MarginStrut(0, margin);

    /// <summary>Returns the set with another adjoining margin.</summary>
    public MarginStrut Append(double margin) => new(Math.Max(Positive, margin), Math.Min(Negative, margin));

    /// <summary>Returns the union of two sets of adjoining margins.</summary>
    public MarginStrut Append(MarginStrut other) => new(Math.Max(Positive, other.Positive), Math.Min(Negative, other.Negative));

    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"(+{Positive}, {Negative})");
}

/// <summary>
/// The block-axis margins of a block-level box that participates in the block formatting context of its parent, after the
/// collapsing with the margins of its children (https://drafts.csswg.org/css2/#collapsing-margins): its block-start margin
/// with the block-start margins of its first in-flow children, its block-end margin with the block-end margins of its last
/// in-flow children. The formatting context of the parent collapses them with the margins of the siblings.
/// </summary>
internal sealed class CollapsedBlockMargins
{
    /// <summary>The margins of a box that has no block-axis margin, no clearance and does not collapse through.</summary>
    public static CollapsedBlockMargins None { get; } = new(default, default, collapsedThrough: false, clearance: null);

    public CollapsedBlockMargins(MarginStrut start, MarginStrut end, bool collapsedThrough, double? clearance)
    {
        Start = start;
        End = end;
        CollapsedThrough = collapsedThrough;
        Clearance = clearance;
    }

    /// <summary>The block-start margin of the box collapsed with the margins of its children that are adjoining to it.</summary>
    public MarginStrut Start { get; }

    /// <summary>The block-end margin of the box collapsed with the margins of its children that are adjoining to it.</summary>
    public MarginStrut End { get; }

    /// <summary>
    /// Whether the block-start and block-end margins of the box are adjoining (https://drafts.csswg.org/css2/#collapsing-margins):
    /// the margins collapse through the box (no border, no padding, no in-flow content and a used block size of 0). The
    /// margins of the box then collapse with the margins of the boxes before and after it.
    /// </summary>
    public bool CollapsedThrough { get; }

    /// <summary>
    /// The clearance of the box (https://drafts.csswg.org/css2/#clearance), which may be negative, or null when the box has
    /// no clearance (it is not cleared past floats: <c>clear</c> is <c>none</c>, or there is no float to clear). Clearance
    /// inhibits the collapsing of the block-start margin of the box with the preceding margins.
    /// </summary>
    public double? Clearance { get; }

    public static CollapsedBlockMargins Create(MarginStrut start, MarginStrut end, bool collapsedThrough, double? clearance)
        => start.IsZero && end.IsZero && !collapsedThrough && clearance is null ? None : new(start, end, collapsedThrough, clearance);
}
