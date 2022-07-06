namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public sealed class TextQuerySyntax : QuerySyntax
{
    internal TextQuerySyntax(QueryToken textToken)
    {
        ArgumentNullException.ThrowIfNull(textToken);

        TextToken = textToken;
    }

    public override QuerySyntaxKind Kind => QuerySyntaxKind.TextQuery;

    public override TextSpan Span => TextToken.Span;

    public QueryToken TextToken { get; }

    public override QueryNodeOrToken[] GetChildren()
    {
        return new[] { TextToken };
    }
}
