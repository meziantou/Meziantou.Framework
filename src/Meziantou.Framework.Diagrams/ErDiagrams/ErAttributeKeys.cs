namespace Meziantou.Framework.Diagrams;

/// <summary>The keys an attribute of an entity is part of.</summary>
[Flags]
public enum ErAttributeKeys
{
    /// <summary>No key.</summary>
    None = 0,

    /// <summary>A primary key (Mermaid <c>PK</c>).</summary>
    PrimaryKey = 1,

    /// <summary>A foreign key (Mermaid <c>FK</c>).</summary>
    ForeignKey = 2,

    /// <summary>A unique key (Mermaid <c>UK</c>).</summary>
    UniqueKey = 4,
}
