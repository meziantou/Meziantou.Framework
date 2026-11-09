namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a requirement, as defined by SysML.</summary>
public enum RequirementKind
{
    /// <summary>A requirement (Mermaid <c>requirement</c>).</summary>
    Requirement,

    /// <summary>A functional requirement (Mermaid <c>functionalRequirement</c>).</summary>
    FunctionalRequirement,

    /// <summary>An interface requirement (Mermaid <c>interfaceRequirement</c>).</summary>
    InterfaceRequirement,

    /// <summary>A performance requirement (Mermaid <c>performanceRequirement</c>).</summary>
    PerformanceRequirement,

    /// <summary>A physical requirement (Mermaid <c>physicalRequirement</c>).</summary>
    PhysicalRequirement,

    /// <summary>A design constraint (Mermaid <c>designConstraint</c>).</summary>
    DesignConstraint,
}
