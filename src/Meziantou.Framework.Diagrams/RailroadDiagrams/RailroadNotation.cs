namespace Meziantou.Framework.Diagrams;

/// <summary>The notation of the grammar of a railroad diagram.</summary>
public enum RailroadNotation
{
    /// <summary>The constructors of Mermaid, such as <c>sequence(terminal("a"), nonterminal("b"))</c> (Mermaid <c>railroad-beta</c>).</summary>
    Primitives,

    /// <summary>Extended Backus–Naur Form, in the W3C and ISO 14977 styles (Mermaid <c>railroad-ebnf-beta</c>).</summary>
    Ebnf,

    /// <summary>Augmented Backus–Naur Form of RFC 5234 (Mermaid <c>railroad-abnf-beta</c>).</summary>
    Abnf,

    /// <summary>Parsing Expression Grammar (Mermaid <c>railroad-peg-beta</c>).</summary>
    Peg,
}
