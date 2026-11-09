namespace Meziantou.Framework.Diagrams;

/// <summary>The shape of the frame of a system boundary.</summary>
public enum UseCaseBoundaryKind
{
    /// <summary>A rectangle with the title at the top.</summary>
    Rectangle,

    /// <summary>A package: a rectangle with the title in a tab (Mermaid <c>type: package</c>).</summary>
    Package,
}
