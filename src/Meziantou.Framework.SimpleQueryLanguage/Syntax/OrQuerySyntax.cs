namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public sealed class OrQuerySyntax : QuerySyntax
{
    internal OrQuerySyntax(QuerySyntax left, QueryToken operatorToken, QuerySyntax right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(operatorToken);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        OperatorToken = operatorToken;
        Right = right;
    }

    public override QuerySyntaxKind Kind => QuerySyntaxKind.OrQuery;

    public override TextSpan Span => TextSpan.FromBounds(Left.Span.Start, Right.Span.End);

    public QuerySyntax Left { get; }

    public QueryToken OperatorToken { get; }

    public QuerySyntax Right { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [Left, OperatorToken, Right];
    }
}
