namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public sealed class AndQuerySyntax : QuerySyntax
{
    internal AndQuerySyntax(QuerySyntax left, QueryToken? @operator, QuerySyntax right)
    {
        ArgumentNullException.ThrowIfNull(left);
        ArgumentNullException.ThrowIfNull(right);

        Left = left;
        Right = right;
        Operator = @operator;
    }

    public override QuerySyntaxKind Kind => QuerySyntaxKind.AndQuery;

    public override TextSpan Span => TextSpan.FromBounds(Left.Span.Start, Right.Span.End);

    public QuerySyntax Left { get; }

    public QueryToken? Operator { get; }

    public QuerySyntax Right { get; }

    public bool IsImplicit => Operator == null;

    public override QueryNodeOrToken[] GetChildren()
    {
        if (Operator != null)
            return new QueryNodeOrToken[] { Left, Operator, Right };

        return new QueryNodeOrToken[] { Left, Right };
    }
}
