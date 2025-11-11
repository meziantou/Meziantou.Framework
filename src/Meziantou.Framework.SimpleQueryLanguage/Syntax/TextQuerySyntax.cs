namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a free-text query syntax node.</summary>
public sealed class TextQuerySyntax : QuerySyntax
{
    internal TextQuerySyntax(QueryToken textToken)
    {
        ArgumentNullException.ThrowIfNull(textToken);

        TextToken = textToken;
    }

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.TextQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span => TextToken.Span;

    /// <summary>Gets the token containing the text to search for.</summary>
    public QueryToken TextToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [TextToken];
    }
}
