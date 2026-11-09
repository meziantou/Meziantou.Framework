namespace Meziantou.Framework.Diagrams;

/// <summary>The direction in which a diagram flows, from the first nodes to the last ones.</summary>
public enum DiagramDirection
{
    /// <summary>From top to bottom (Mermaid <c>TB</c> or <c>TD</c>).</summary>
    TopToBottom,

    /// <summary>From bottom to top (Mermaid <c>BT</c>).</summary>
    BottomToTop,

    /// <summary>From left to right (Mermaid <c>LR</c>).</summary>
    LeftToRight,

    /// <summary>From right to left (Mermaid <c>RL</c>).</summary>
    RightToLeft,
}
