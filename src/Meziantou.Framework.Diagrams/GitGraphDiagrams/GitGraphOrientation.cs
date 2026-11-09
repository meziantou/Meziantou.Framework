namespace Meziantou.Framework.Diagrams;

/// <summary>The direction of the history of a Git graph.</summary>
public enum GitGraphOrientation
{
    /// <summary>The commits go from left to right; the branches are rows (Mermaid <c>LR</c>).</summary>
    LeftToRight,

    /// <summary>The commits go from top to bottom; the branches are columns (Mermaid <c>TB</c>).</summary>
    TopToBottom,

    /// <summary>The commits go from bottom to top; the branches are columns (Mermaid <c>BT</c>).</summary>
    BottomToTop,
}
