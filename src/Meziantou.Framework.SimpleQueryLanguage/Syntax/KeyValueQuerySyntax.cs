namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

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

    public override QuerySyntaxKind Kind => QuerySyntaxKind.KeyValueQuery;

    public override TextSpan Span => TextSpan.FromBounds(KeyToken.Span.Start, ValueToken.Span.End);

    public QueryToken KeyToken { get; }

    public QueryToken OperatorToken { get; }

    public QueryToken ValueToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [KeyToken, OperatorToken, ValueToken];
    }
}
