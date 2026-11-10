namespace Meziantou.Framework.Diagrams;

/// <summary>The kind of an element of a C4 diagram.</summary>
public enum C4ElementKind
{
    /// <summary>A person (Mermaid <c>Person</c>).</summary>
    Person,

    /// <summary>A software system (Mermaid <c>System</c>).</summary>
    System,

    /// <summary>A container: an application or a data store (Mermaid <c>Container</c>).</summary>
    Container,

    /// <summary>A component of a container (Mermaid <c>Component</c>).</summary>
    Component,
}
