namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Represents a token in the query syntax tree.</summary>
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

    /// <summary>Gets the kind of this token.</summary>
    public override QuerySyntaxKind Kind { get; }

    /// <summary>Gets the text span covered by this token.</summary>
    public override TextSpan Span { get; }

    /// <summary>Gets the full query text.</summary>
    public string QueryText { get; }

    /// <summary>Gets the text of this token extracted from the query text.</summary>
    public string Text => QueryText.Substring(Span.Start, Span.Length);

    /// <summary>Gets the parsed value of this token, or null if the token has no value.</summary>
    public string? Value { get; }

    /// <summary>Gets the child elements of this token (always empty for tokens).</summary>
    /// <returns>An empty array.</returns>
    public override QueryNodeOrToken[] GetChildren()
    {
        return [];
    }

    public override string ToString()
    {
        return Text;
    }

    /// <summary>Converts this token to a text token.</summary>
    /// <returns>A text token representing the same text.</returns>
    public QueryToken AsText()
    {
        if (Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.QuotedTextToken)
            return this;

        var value = QueryText.Substring(Span.Start, Span.Length);
        return new QueryToken(QuerySyntaxKind.TextToken, QueryText, Span, value);
    }
}
