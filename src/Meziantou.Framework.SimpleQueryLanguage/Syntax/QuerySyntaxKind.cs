namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

/// <summary>Specifies the kind of a syntax element in the query syntax tree.</summary>
public enum QuerySyntaxKind
{
    None,
    EndOfFile,
    OpenParenthesisToken,
    CloseParenthesisToken,
    ColonToken,
    EqualOperatorToken,
    LessThanOperatorToken,
    LessThanOrEqualOperatorToken,
    GreaterThanOperatorToken,
    GreaterThanOrEqualOperatorToken,
    NotEqualOperatorToken,
    TextToken,
    AndKeyword,
    OrKeyword,
    NotKeyword,
    QuotedTextToken,
    WhitespaceToken,
    TextQuery,
    KeyValueQuery,
    OrQuery,
    AndQuery,
    NegatedQuery,
    ParenthesizedQuery,
}
