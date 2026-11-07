using System.Runtime.InteropServices;

namespace Meziantou.Framework.WebPlatform.Css.Layout;

/// <summary>A set of physical edges (or directions) of a box.</summary>
[Flags]
internal enum PhysicalEdges : byte
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,

    /// <summary>The edges of the horizontal axis.</summary>
    Horizontal = Left | Right,

    /// <summary>The edges of the vertical axis.</summary>
    Vertical = Top | Bottom,
}

/// <summary>
/// The state of a <c>scroll-state</c> query container taken by the "run snapshot post-layout state steps"
/// (https://drafts.csswg.org/css-conditional-5/#updating-scroll-state): the container scroll-state queries are evaluated against
/// it until the next snapshot (see <see cref="LayoutEngine.UpdateScrollStateSnapshots"/>). All the states are physical: the
/// logical values of the queries are mapped with the writing mode and the direction of the query container.
/// </summary>
/// <param name="Stuck">The edges of the sticky view rectangle the container is shifted to stay inside (https://drafts.csswg.org/css-conditional-5/#stuck).</param>
/// <param name="Snapped">The axes in which the container is a snap target of its scroll snap container (<see cref="PhysicalEdges.Horizontal"/> for x, https://drafts.csswg.org/css-conditional-5/#snapped).</param>
/// <param name="Scrollable">The edges past which the container has scrollable overflow reachable by user scrolling (https://drafts.csswg.org/css-conditional-5/#scrollable).</param>
/// <param name="Scrolled">The directions of the most recent relative scroll of the container, one per axis (https://drafts.csswg.org/css-conditional-5/#scrolled).</param>
[StructLayout(LayoutKind.Auto)]
internal readonly record struct ScrollStateSnapshot(PhysicalEdges Stuck, PhysicalEdges Snapped, PhysicalEdges Scrollable, PhysicalEdges Scrolled)
{
    /// <summary>The state of a container that is not stuck, not snapped, not scrollable and was never scrolled (the state of every feature is none).</summary>
    public static ScrollStateSnapshot None => default;

    public bool IsNone => Stuck == PhysicalEdges.None && Snapped == PhysicalEdges.None && Scrollable == PhysicalEdges.None && Scrolled == PhysicalEdges.None;
}
