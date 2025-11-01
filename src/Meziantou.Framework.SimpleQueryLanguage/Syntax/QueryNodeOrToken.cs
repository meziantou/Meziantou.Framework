namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Base class for nodes and tokens in the query syntax tree.</summary>
public abstract class QueryNodeOrToken
{
    /// <summary>Gets the kind of this syntax element.</summary>
    public abstract QuerySyntaxKind Kind { get; }

    /// <summary>Gets the text span covered by this syntax element.</summary>
    public abstract TextSpan Span { get; }

    /// <summary>Gets the child elements of this syntax element.</summary>
    /// <returns>An array of child elements.</returns>
    public abstract QueryNodeOrToken[] GetChildren();
}
