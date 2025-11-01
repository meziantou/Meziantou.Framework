namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a key-value query syntax node (e.g., "name:john" or "age>21").</summary>
public sealed class KeyValueQuerySyntax : QuerySyntax
{
    internal KeyValueQuerySyntax(QueryToken keyToken, QueryToken operatorToken, QueryToken valueToken)
    {
        ArgumentNullException.ThrowIfNull(keyToken);
        ArgumentNullException.ThrowIfNull(operatorToken);
        ArgumentNullException.ThrowIfNull(valueToken);

        KeyToken = keyToken;
        OperatorToken = operatorToken;
        ValueToken = valueToken;
    }

    /// <summary>Gets the kind of this syntax node.</summary>
    public override QuerySyntaxKind Kind => QuerySyntaxKind.KeyValueQuery;

    /// <summary>Gets the text span covered by this syntax node.</summary>
    public override TextSpan Span => TextSpan.FromBounds(KeyToken.Span.Start, ValueToken.Span.End);

    /// <summary>Gets the token containing the property key.</summary>
    public QueryToken KeyToken { get; }

    /// <summary>Gets the token containing the comparison operator.</summary>
    public QueryToken OperatorToken { get; }

    /// <summary>Gets the token containing the value to compare against.</summary>
    public QueryToken ValueToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [KeyToken, OperatorToken, ValueToken];
    }
}
