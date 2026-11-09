namespace Meziantou.Framework.Diagrams;

/// <summary>The direction of the time axis of a timeline.</summary>
public enum TimelineDirection
{
    /// <summary>The periods are left to right, with their events below (Mermaid <c>timeline</c> or <c>timeline LR</c>).</summary>
    LeftToRight,

    /// <summary>The periods are top to bottom, with their events on the right (Mermaid <c>timeline TD</c>).</summary>
    TopToBottom,
}
