namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public partial class QuerySyntax
{
    private static class Lexer
    {
        public static IEnumerable<QueryToken> Tokenize(string text)
        {
            var position = 0;

            while (position < text.Length)
            {
                var c = text[position];
                switch (c)
                {
                    case '(':
                        yield return ReadSingleCharacterToken(QuerySyntaxKind.OpenParenthesisToken, ref position, text);
                        break;
                    case ')':
                        yield return ReadSingleCharacterToken(QuerySyntaxKind.CloseParenthesisToken, ref position, text);
                        break;
                    case ':':
                        yield return ReadSingleCharacterToken(QuerySyntaxKind.ColonToken, ref position, text);
                        break;
                    case '=':
                        yield return ReadSingleCharacterToken(QuerySyntaxKind.EqualOperatorToken, ref position, text);
                        break;
                    case '<':
                        if (position + 1 < text.Length && text[position + 1] == '=')
                        {
                            var span = new TextSpan(position, 2);
                            var token = new QueryToken(QuerySyntaxKind.LessThanOrEqualOperatorToken, text, span, value: null);
                            position += span.Length;
                            yield return token;
                        }
                        else if (position + 1 < text.Length && text[position + 1] == '>')
                        {
                            var span = new TextSpan(position, 2);
                            var token = new QueryToken(QuerySyntaxKind.NotEqualOperatorToken, text, span, value: null);
                            position += span.Length;
                            yield return token;
                        }
                        else
                        {
                            yield return ReadSingleCharacterToken(QuerySyntaxKind.LessThanOperatorToken, ref position, text);
                        }

                        break;
                    case '>':
                        if (position + 1 < text.Length && text[position + 1] == '=')
                        {
                            var span = new TextSpan(position, 2);
                            var token = new QueryToken(QuerySyntaxKind.GreaterThanOrEqualOperatorToken, text, span, value: null);
                            position += span.Length;
                            yield return token;
                        }
                        else
                        {
                            yield return ReadSingleCharacterToken(QuerySyntaxKind.GreaterThanOperatorToken, ref position, text);
                        }

                        break;

                    case '-':
                        yield return ReadSingleCharacterToken(QuerySyntaxKind.NotKeyword, ref position, text);
                        break;

                    case '"':
                        yield return ReadQuotedText(ref position, text);
                        break;

                    default:
                        if (char.IsWhiteSpace(c))
                            yield return ReadWhitespace(ref position, text);
                        else
                            yield return ReadText(ref position, text);
                        break;
                }
            }

            yield return new QueryToken(QuerySyntaxKind.EndOfFile, text, new TextSpan(position, 0), value: null);
        }

        private static QueryToken ReadSingleCharacterToken(QuerySyntaxKind kind, ref int position, string text)
        {
            var span = new TextSpan(position, 1);
            var token = new QueryToken(kind, text, span, value: null);
            position += span.Length;
            return token;
        }

        private static QueryToken ReadWhitespace(ref int position, string text)
        {
            var start = position;

            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;

            var length = position - start;
            var span = new TextSpan(start, length);
            return new QueryToken(QuerySyntaxKind.WhitespaceToken, text, span, value: null);
        }

        private static QueryToken ReadQuotedText(ref int position, string text)
        {
            var start = position;
            position++; // skip initial quote
            var sb = new StringBuilder();

            while (position < text.Length)
            {
                var c = text[position];
                var l = position < text.Length - 1
                            ? text[position + 1]
                            : '\0';

                if (c == '"')
                {
                    position++;

                    if (l != '"')
                        break;
                }

                sb.Append(c);
                position++;
            }

            var length = position - start;
            var span = new TextSpan(start, length);
            var value = sb.ToString();
            return new QueryToken(QuerySyntaxKind.QuotedTextToken, text, span, value);
        }

        private static QueryToken ReadText(ref int position, string text)
        {
            var start = position;

            while (position < text.Length)
            {
                var c = text[position];
                if (c is ':' or '=' or '<' or '>' or '(' or ')' || char.IsWhiteSpace(c))
                    break;

                position++;
            }

            var length = position - start;
            var span = new TextSpan(start, length);

            var tokenText = text.Substring(start, length);
            var kind = GetKeywordOrText(tokenText);
            return new QueryToken(kind, text, span, tokenText);
        }

        private static QuerySyntaxKind GetKeywordOrText(string text)
        {
            return text.ToLowerInvariant() switch
            {
                "not" => QuerySyntaxKind.NotKeyword,
                "or" => QuerySyntaxKind.OrKeyword,
                "and" => QuerySyntaxKind.AndKeyword,
                _ => QuerySyntaxKind.TextToken,
            };
        }
    }
}
