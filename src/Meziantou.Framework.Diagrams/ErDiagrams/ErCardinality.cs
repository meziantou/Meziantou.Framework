namespace Meziantou.Framework.Diagrams;

/// <summary>How many instances of an entity an instance of the other entity of a relationship relates to, drawn in crow's foot notation.</summary>
public enum ErCardinality
{
    /// <summary>Exactly one: two bars (Mermaid <c>||</c>).</summary>
    ExactlyOne,

    /// <summary>Zero or one: a bar and a circle (Mermaid <c>|o</c> or <c>o|</c>).</summary>
    ZeroOrOne,

    /// <summary>One or more: a bar and a crow's foot (Mermaid <c>}|</c> or <c>|{</c>).</summary>
    OneOrMore,

    /// <summary>Zero or more: a circle and a crow's foot (Mermaid <c>}o</c> or <c>o{</c>).</summary>
    ZeroOrMore,

    /// <summary>The parent of a master-detail relationship: a diamond (Mermaid <c>u</c>).</summary>
    Parent,
}
