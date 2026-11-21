namespace Meziantou.Framework.Graphviz.Internals;

/// <summary>The value of an attribute, with the position of its name for the warnings.</summary>
/// <param name="Value">The value, without its quotes or its angle brackets.</param>
/// <param name="IsHtml">Whether the value is an HTML string (<c>&lt;...&gt;</c>).</param>
/// <param name="Position">The offset of the name of the attribute in the text.</param>
/// <param name="IsInherited">Whether a subgraph copied the value from its parent graph.</param>
internal readonly record struct DotAttribute(string Value, bool IsHtml, int Position, bool IsInherited = false);
