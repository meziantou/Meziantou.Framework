namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents an OR query syntax node that combines two queries with logical OR.</summary>
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

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.OrQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span => TextSpan.FromBounds(Left.Span.Start, Right.Span.End);

    /// <summary>Gets the left operand.</summary>
    public QuerySyntax Left { get; }

    /// <summary>Gets the OR operator token.</summary>
    public QueryToken OperatorToken { get; }

    /// <summary>Gets the right operand.</summary>
    public QuerySyntax Right { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [Left, OperatorToken, Right];
    }
}
