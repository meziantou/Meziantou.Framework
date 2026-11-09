namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of a C4 diagram.</summary>
public enum C4DiagramKind
{
    /// <summary>A system context diagram (Mermaid <c>C4Context</c>).</summary>
    Context,

    /// <summary>A container diagram (Mermaid <c>C4Container</c>).</summary>
    Container,

    /// <summary>A component diagram (Mermaid <c>C4Component</c>).</summary>
    Component,

    /// <summary>A dynamic diagram, whose relationships are numbered in order (Mermaid <c>C4Dynamic</c>).</summary>
    Dynamic,

    /// <summary>A deployment diagram (Mermaid <c>C4Deployment</c>).</summary>
    Deployment,
}
