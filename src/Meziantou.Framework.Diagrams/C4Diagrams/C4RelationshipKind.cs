namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a relationship of a C4 diagram.</summary>
public enum C4RelationshipKind
{
    /// <summary>An arrow to the target (Mermaid <c>Rel</c>).</summary>
    Default,

    /// <summary>Arrows at both ends (Mermaid <c>BiRel</c>).</summary>
    Bidirectional,

    /// <summary>An arrow to the source (Mermaid <c>Rel_Back</c>).</summary>
    Back,

    /// <summary>An arrow to the target, which is above in C4-PlantUML (Mermaid <c>Rel_U</c>).</summary>
    Up,

    /// <summary>An arrow to the target, which is below in C4-PlantUML (Mermaid <c>Rel_D</c>).</summary>
    Down,

    /// <summary>An arrow to the target, which is on the left in C4-PlantUML (Mermaid <c>Rel_L</c>).</summary>
    Left,

    /// <summary>An arrow to the target, which is on the right in C4-PlantUML (Mermaid <c>Rel_R</c>).</summary>
    Right,
}
