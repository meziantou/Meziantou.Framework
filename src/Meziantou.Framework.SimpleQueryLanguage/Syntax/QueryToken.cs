namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public sealed class QueryToken : QueryNodeOrToken
{
    internal QueryToken(QuerySyntaxKind kind, string queryText, TextSpan span, string? value)
    {
        ArgumentNullException.ThrowIfNull(queryText);

        Kind = kind;
        QueryText = queryText;
        Span = span;
        Value = value;
    }

    public override QuerySyntaxKind Kind { get; }

    public override TextSpan Span { get; }

    public string QueryText { get; }

    public string Text => QueryText.Substring(Span.Start, Span.Length);

    public string? Value { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return [];
    }

    public override string ToString()
    {
        return Text;
    }

    public QueryToken AsText()
    {
        if (Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.QuotedTextToken)
            return this;

        var value = QueryText.Substring(Span.Start, Span.Length);
        return new QueryToken(QuerySyntaxKind.TextToken, QueryText, Span, value);
    }
}
