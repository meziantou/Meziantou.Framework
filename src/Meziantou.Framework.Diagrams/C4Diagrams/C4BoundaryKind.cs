namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a boundary of a C4 diagram.</summary>
public enum C4BoundaryKind
{
    /// <summary>A boundary (Mermaid <c>Boundary</c>).</summary>
    Boundary,

    /// <summary>The boundary of an enterprise (Mermaid <c>Enterprise_Boundary</c>).</summary>
    Enterprise,

    /// <summary>The boundary of a software system (Mermaid <c>System_Boundary</c>).</summary>
    System,

    /// <summary>The boundary of a container (Mermaid <c>Container_Boundary</c>).</summary>
    Container,

    /// <summary>A deployment node (Mermaid <c>Deployment_Node</c> or <c>Node</c>).</summary>
    DeploymentNode,

    /// <summary>A deployment node aligned to the left (Mermaid <c>Node_L</c>).</summary>
    DeploymentNodeLeft,

    /// <summary>A deployment node aligned to the right (Mermaid <c>Node_R</c>).</summary>
    DeploymentNodeRight,
}
