namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

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
