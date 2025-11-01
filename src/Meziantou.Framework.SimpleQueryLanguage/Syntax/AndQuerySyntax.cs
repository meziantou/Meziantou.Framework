namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents an AND query syntax node that combines two queries with logical AND.</summary>
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

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.AndQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span => TextSpan.FromBounds(Left.Span.Start, Right.Span.End);

    /// <summary>Gets the left operand.</summary>
    public QuerySyntax Left { get; }

    /// <summary>Gets the AND operator token, or null for implicit AND.</summary>
    public QueryToken? Operator { get; }

    /// <summary>Gets the right operand.</summary>
    public QuerySyntax Right { get; }

    /// <summary>Gets a value indicating whether this is an implicit AND (no operator token).</summary>
    public bool IsImplicit => Operator is null;

    public override QueryNodeOrToken[] GetChildren()
    {
        if (Operator is not null)
            return [Left, Operator, Right];

        return [Left, Right];
    }
}
