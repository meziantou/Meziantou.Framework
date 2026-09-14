using System.Diagnostics;
using System.Runtime.CompilerServices;

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

            var token = new QueryToken(kind, Current.QueryText, new TextSpan(Current.Span.End, 0), "");
            return token;
        }

        private QueryToken MatchOperator()
        {
            if (Current.Kind is QuerySyntaxKind.ColonToken or QuerySyntaxKind.EqualOperatorToken or QuerySyntaxKind.NotEqualOperatorToken or QuerySyntaxKind.LessThanOperatorToken or QuerySyntaxKind.LessThanOrEqualOperatorToken or QuerySyntaxKind.GreaterThanOperatorToken or QuerySyntaxKind.GreaterThanOrEqualOperatorToken)
                return Next();

            var token = new QueryToken(QuerySyntaxKind.ColonToken, Current.QueryText, new TextSpan(Current.Span.End, 0), "");
            return token;
        }

        private QueryToken MatchTextOrQuotedText()
        {
            var isText = Current.Kind is QuerySyntaxKind.TextToken or QuerySyntaxKind.QuotedTextToken;
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
            MarkUnmatchedCloseParenthesesAsText();

        Again:
            var result = ParseExpression();
            if (Current.Kind != QuerySyntaxKind.EndOfFile)
            {
                // Not expected once unmatched parentheses are text, but restarting still yields a valid tree
                Debug.Fail($"Unexpected token {Current.Kind} at position {Current.Span.Start}");
                MarkCurrentAsText();
                _tokenIndex = 0;
                goto Again;
            }

            return result;
        }

        /// <summary>
        /// Turns every ')' that cannot close a parenthesis into text before parsing. Discovering them while parsing
        /// means restarting from the first token for each one, which is quadratic in the number of such tokens.
        /// </summary>
        private void MarkUnmatchedCloseParenthesesAsText()
        {
            var depth = 0;
            for (var i = 0; i < _tokens.Length; i++)
            {
                switch (_tokens[i].Kind)
                {
                    case QuerySyntaxKind.OpenParenthesisToken:
                        depth++;
                        break;

                    // A ')' starting the query or directly following '(' is where an expression must start, so
                    // ParsePrimaryExpression already reads it as a search term. It does not close a parenthesis.
                    case QuerySyntaxKind.CloseParenthesisToken when i == 0 || _tokens[i - 1].Kind is QuerySyntaxKind.OpenParenthesisToken:
                        break;

                    // Any other ')' with no open parenthesis would stop the parser before the end of the query
                    case QuerySyntaxKind.CloseParenthesisToken when depth == 0:
                        _tokens[i] = _tokens[i].AsText();
                        break;

                    case QuerySyntaxKind.CloseParenthesisToken:
                        depth--;
                        break;
                }
            }
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
                // A trailing OR has no right operand. Like a trailing AND, it is a search term rather than an operator,
                // otherwise the missing operand becomes an empty search term that matches everything.
                if (Lookahead.Kind is QuerySyntaxKind.EndOfFile or QuerySyntaxKind.CloseParenthesisToken)
                {
                    result = new AndQuerySyntax(result, @operator: null, new TextQuerySyntax(Next().AsText()));
                    break;
                }

                var operatorToken = Next();
                var term = ParseAndExpression();
                result = new OrQuerySyntax(result, operatorToken, term);
            }

            return result;
        }

        private QuerySyntax ParseAndExpression()
        {
            var result = ParsePrimaryExpression();
            while (Current.Kind is not QuerySyntaxKind.EndOfFile and not QuerySyntaxKind.OrKeyword and not QuerySyntaxKind.CloseParenthesisToken)
            {
                QueryToken? op = null;
                if (Current.Kind is QuerySyntaxKind.AndKeyword)
                {
                    op = Match(QuerySyntaxKind.AndKeyword);
                }

                if (Current.Kind is not QuerySyntaxKind.EndOfFile and not QuerySyntaxKind.OrKeyword and not QuerySyntaxKind.CloseParenthesisToken)
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
            // Parenthesized and negated expressions recurse, so a deeply nested query would otherwise
            // overflow the stack and kill the process. This turns that into a catchable exception.
            RuntimeHelpers.EnsureSufficientExecutionStack();

            return Current.Kind switch
            {
                QuerySyntaxKind.NotKeyword => ParseNotExpression(),
                // A '(' ending the query encloses nothing, so it is a search term rather than an empty search term
                QuerySyntaxKind.OpenParenthesisToken when Lookahead.Kind is not QuerySyntaxKind.EndOfFile => ParseParenthesizedExpression(),
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
                return new QueryToken(QuerySyntaxKind.TextToken, operatorToken.QueryText, new TextSpan(operatorToken.Span.End, 0), "");

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
                // Keywords directly after the operator are part of the value: "state:or" searches for "or"
                case QuerySyntaxKind.TextToken:
                case QuerySyntaxKind.ColonToken:
                case QuerySyntaxKind.NotKeyword:
                case QuerySyntaxKind.AndKeyword:
                case QuerySyntaxKind.OrKeyword:
                    return true;
                default:
                    return false;
            }
        }
    }
}
