namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a NOT query syntax node that negates another query.</summary>
public sealed class NegatedQuerySyntax : QuerySyntax
{
    internal NegatedQuerySyntax(QueryToken notToken, QuerySyntax query)
    {
        ArgumentNullException.ThrowIfNull(notToken);
        ArgumentNullException.ThrowIfNull(query);

        OperatorToken = notToken;
        Query = query;

        // Computed once: reading Query.Span lazily recurses through every nested negation
        Span = TextSpan.FromBounds(notToken.Span.Start, query.Span.End);
    }

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.NegatedQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span { get; }

    /// <summary>Gets the NOT operator token.</summary>
    public QueryToken OperatorToken { get; }

    /// <summary>Gets the query to negate.</summary>
    public QuerySyntax Query { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [OperatorToken, Query];
    }
}
