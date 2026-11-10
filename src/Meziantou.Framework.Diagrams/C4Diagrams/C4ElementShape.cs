namespace Meziantou.Framework.Diagrams;

/// <summary>The shape of an element of a C4 diagram.</summary>
public enum C4ElementShape
{
    /// <summary>A rounded box.</summary>
    Box,

    /// <summary>A cylinder: a database (Mermaid <c>Db</c>).</summary>
    Database,

    /// <summary>A horizontal cylinder: a queue (Mermaid <c>Queue</c>).</summary>
    Queue,
}
