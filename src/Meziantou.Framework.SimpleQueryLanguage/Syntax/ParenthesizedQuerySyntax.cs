namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a parenthesized query syntax node.</summary>
public sealed class ParenthesizedQuerySyntax : QuerySyntax
{
    internal ParenthesizedQuerySyntax(QueryToken openParenthesisToken, QuerySyntax query, QueryToken closeParenthesisToken)
    {
        ArgumentNullException.ThrowIfNull(openParenthesisToken);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentNullException.ThrowIfNull(closeParenthesisToken);

        OpenParenthesisToken = openParenthesisToken;
        Query = query;
        CloseParenthesisToken = closeParenthesisToken;
    }

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.ParenthesizedQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span => TextSpan.FromBounds(OpenParenthesisToken.Span.Start, CloseParenthesisToken.Span.End);

    /// <summary>Gets the opening parenthesis token.</summary>
    public QueryToken OpenParenthesisToken { get; }

    /// <summary>Gets the query inside the parentheses.</summary>
    public QuerySyntax Query { get; }

    /// <summary>Gets the closing parenthesis token.</summary>
    public QueryToken CloseParenthesisToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [OpenParenthesisToken, Query, CloseParenthesisToken];
    }
}
