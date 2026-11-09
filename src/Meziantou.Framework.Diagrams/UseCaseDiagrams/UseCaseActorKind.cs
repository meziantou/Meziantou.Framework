namespace Meziantou.Framework.Diagrams;

/// <summary>How an actor of a use case diagram is drawn.</summary>
public enum UseCaseActorKind
{
    /// <summary>A stick figure.</summary>
    Normal,

    /// <summary>A figure drawn as an outline (Mermaid <c>type: hollow</c>).</summary>
    Hollow,

    /// <summary>A filled silhouette (Mermaid <c>type: awesome</c>).</summary>
    Awesome,
}
