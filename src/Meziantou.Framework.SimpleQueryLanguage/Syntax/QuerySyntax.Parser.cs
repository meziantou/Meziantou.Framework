namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public partial class QuerySyntax
{
    private sealed class Parser
    {
        private readonly QueryToken[] _tokens;
        private int _tokenIndex;

        public Parser(IEnumerable<QueryToken> tokens)
        {
            _tokens = tokens.ToArray();
        }

        private QueryToken Current => _tokens[_tokenIndex];

        private QueryToken Lookahead
        {
            get
            {
                return _tokenIndex < _tokens.Length - 1 ? _tokens[_tokenIndex + 1] : _tokens[^1];
            }
        }

        private QueryToken Next()
        {
            var result = Current;
            _tokenIndex++;
            return result;
        }

        private QueryToken Match(QuerySyntaxKind kind)
        {
            if (Current.Kind == kind)
                return Next();

            var token = new QueryToken(kind, Current.QueryText, new TextSpan(Current.Span.End, 0), string.Empty);
            return token;
        }

        private QueryToken MatchOperator()
        {
            if (Current.Kind is QuerySyntaxKind.ColonToken or QuerySyntaxKind.EqualOperatorToken or QuerySyntaxKind.NotEqualOperatorToken or QuerySyntaxKind.LessThanOperatorToken or QuerySyntaxKind.LessThanOrEqualOperatorToken or QuerySyntaxKind.GreaterThanOperatorToken or QuerySyntaxKind.GreaterThanOrEqualOperatorToken)
                return Next();

            var token = new QueryToken(QuerySyntaxKind.ColonToken, Current.QueryText, new TextSpan(Current.Span.End, 0), string.Empty);
            return token;
        }

        private QueryToken MatchTextOrQuotedText()
        {
            var isText = Current.Kind == QuerySyntaxKind.TextToken ||
                         Current.Kind == QuerySyntaxKind.QuotedTextToken;
            return isText ? Next() : Match(QuerySyntaxKind.TextToken);
        }

        private void MarkCurrentAsText()
        {
            if (Current.Kind == QuerySyntaxKind.EndOfFile)
                return;

            _tokens[_tokenIndex] = _tokens[_tokenIndex].AsText();
        }

        public QuerySyntax Parse()
        {
        Again:
            var result = ParseExpression();
            if (Current.Kind != QuerySyntaxKind.EndOfFile)
            {
                MarkCurrentAsText();
                _tokenIndex = 0;
                goto Again;
            }

            return result;
        }

        private QuerySyntax ParseExpression()
        {
            return ParseOrExpression();
        }

        private QuerySyntax ParseOrExpression()
        {
            var result = ParseAndExpression();

            while (Current.Kind is QuerySyntaxKind.OrKeyword)
            {
                var operatorToken = Next();
                var term = ParseAndExpression();
                result = new OrQuerySyntax(result, operatorToken, term);
            }

            return result;
        }

        private QuerySyntax ParseAndExpression()
        {
            var result = ParsePrimaryExpression();
            while (Current.Kind != QuerySyntaxKind.EndOfFile &&
                   Current.Kind != QuerySyntaxKind.OrKeyword &&
                   Current.Kind != QuerySyntaxKind.CloseParenthesisToken)
            {
                QueryToken? op = null;
                if (Current.Kind == QuerySyntaxKind.AndKeyword)
                {
                    op = Match(QuerySyntaxKind.AndKeyword);
                }

                if (Current.Kind != QuerySyntaxKind.EndOfFile &&
                    Current.Kind != QuerySyntaxKind.OrKeyword &&
                    Current.Kind != QuerySyntaxKind.CloseParenthesisToken)
                {
                    var term = ParsePrimaryExpression();
                    result = new AndQuerySyntax(result, op, term);
                }
                else
                {
                    result = new AndQuerySyntax(result, @operator: null, new TextQuerySyntax(op!.AsText()));
                }
            }

            return result;
        }

        private QuerySyntax ParsePrimaryExpression()
        {
            return Current.Kind switch
            {
                QuerySyntaxKind.NotKeyword => ParseNotExpression(),
                QuerySyntaxKind.OpenParenthesisToken => ParseParenthesizedExpression(),
                _ => ParseTextOrKeyValueExpression(),
            };
        }

        private QuerySyntax ParseNotExpression()
        {
            if (!CanStartPrimaryExpression(Lookahead.Kind))
            {
                MarkCurrentAsText();
                return ParseTextOrKeyValueExpression();
            }

            var token = Next();
            if (Current.Kind == QuerySyntaxKind.EndOfFile)
            {
                token = token.AsText();
                return new TextQuerySyntax(token);
            }

            var expression = ParsePrimaryExpression();
            return new NegatedQuerySyntax(token, expression);
        }

        private QuerySyntax ParseTextOrKeyValueExpression()
        {
            if (Current.Kind is QuerySyntaxKind.TextToken &&
                Lookahead.Kind is QuerySyntaxKind.ColonToken or QuerySyntaxKind.EqualOperatorToken or QuerySyntaxKind.NotEqualOperatorToken or QuerySyntaxKind.LessThanOperatorToken or QuerySyntaxKind.LessThanOrEqualOperatorToken or QuerySyntaxKind.GreaterThanOperatorToken or QuerySyntaxKind.GreaterThanOrEqualOperatorToken)
            {
                var key = Current;
                var op = Lookahead;

                // If there is whitespace before the colon, we treat the colon
                // as text.

                if (key.Span.End >= op.Span.Start)
                    return ParseKeyValueExpression();

                _tokens[_tokenIndex + 1] = op.AsText();
            }

            // If the current token isn't text, we make it text.
            // This is to avoid an infinite loop in the parser
            // where we keep inserting new tokens when we can't
            // parse a primary expression.

            MarkCurrentAsText();
            return ParseTextExpression();
        }

        private TextQuerySyntax ParseTextExpression()
        {
            var token = MatchTextOrQuotedText();
            return new TextQuerySyntax(token);
        }

        private KeyValueQuerySyntax ParseKeyValueExpression()
        {
            var key = Match(QuerySyntaxKind.TextToken);
            var op = MatchOperator();
            var value = ReadKeyValueArgument(op);
            return new KeyValueQuerySyntax(key, op, value);
        }

        private QueryToken ReadKeyValueArgument(QueryToken operatorToken)
        {
            if (Current.Span.Start > operatorToken.Span.End)
                return new QueryToken(QuerySyntaxKind.TextToken, operatorToken.QueryText, new TextSpan(operatorToken.Span.End, 0), string.Empty);

            if (Current.Kind == QuerySyntaxKind.QuotedTextToken)
                return Match(QuerySyntaxKind.QuotedTextToken);

            var start = Current.Span.Start;
            var end = start;

            while (Current.Span.Start == end && CanFollowOperator(Current.Kind))
            {
                MarkCurrentAsText();
                end = Current.Span.End;
                Next();
            }

            var queryText = Current.QueryText;
            var span = TextSpan.FromBounds(start, end);
            var value = queryText.Substring(span.Start, span.Length);
            return new QueryToken(QuerySyntaxKind.TextQuery, queryText, span, value);
        }

        private ParenthesizedQuerySyntax ParseParenthesizedExpression()
        {
            var openParenthesisToken = Match(QuerySyntaxKind.OpenParenthesisToken);
            var expression = ParseExpression();
            var closeParenthesisToken = Match(QuerySyntaxKind.CloseParenthesisToken);
            return new ParenthesizedQuerySyntax(openParenthesisToken, expression, closeParenthesisToken);
        }

        private static bool CanStartPrimaryExpression(QuerySyntaxKind kind)
        {
            switch (kind)
            {
                case QuerySyntaxKind.OpenParenthesisToken:
                case QuerySyntaxKind.TextToken:
                case QuerySyntaxKind.NotKeyword:
                case QuerySyntaxKind.QuotedTextToken:
                    return true;
                default:
                    return false;
            }
        }

        private static bool CanFollowOperator(QuerySyntaxKind kind)
        {
            switch (kind)
            {
                case QuerySyntaxKind.TextToken:
                case QuerySyntaxKind.ColonToken:
                    return true;
                default:
                    return false;
            }
        }
    }
}
