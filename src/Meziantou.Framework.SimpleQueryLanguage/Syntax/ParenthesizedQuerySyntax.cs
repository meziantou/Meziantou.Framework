namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

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

    public override QuerySyntaxKind Kind => QuerySyntaxKind.ParenthesizedQuery;

    public override TextSpan Span => TextSpan.FromBounds(OpenParenthesisToken.Span.Start, CloseParenthesisToken.Span.End);

    public QueryToken OpenParenthesisToken { get; }

    public QuerySyntax Query { get; }

    public QueryToken CloseParenthesisToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [OpenParenthesisToken, Query, CloseParenthesisToken];
    }
}
