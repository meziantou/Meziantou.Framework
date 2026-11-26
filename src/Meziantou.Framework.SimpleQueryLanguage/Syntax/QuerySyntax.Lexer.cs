using System.Buffers;
using System.Runtime.InteropServices;

namespace Meziantou.Framework.SimpleQueryLanguage.Syntax;

public partial class QuerySyntax
{
    private static class Lexer
    {
        private static readonly SearchValues<char> TextTokenTerminators = CreateTextTokenTerminators();

        private static SearchValues<char> CreateTextTokenTerminators()
        {
            // Tokenize splits whitespace with char.IsWhiteSpace, so text must end at the same characters.
            // Otherwise a non-breaking space glues two words into a single search term.
            var terminators = new List<char>(":=<>()");
            for (var c = char.MinValue; c < char.MaxValue; c++)
            {
                if (char.IsWhiteSpace(c))
                {
                    terminators.Add(c);
                }
            }

            return SearchValues.Create(CollectionsMarshal.AsSpan(terminators));
        }

        /// <summary>Splits the query into tokens, without the whitespace between them, and ends with an end-of-file token.</summary>
        public static List<QueryToken> Tokenize(string text)
        {
            var tokens = new List<QueryToken>();
            var position = 0;

            while (position < text.Length)
            {
                var c = text[position];
                switch (c)
                {
                    case '(':
                        tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.OpenParenthesisToken, ref position, text));
                        break;
                    case ')':
                        tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.CloseParenthesisToken, ref position, text));
                        break;
                    case ':':
                        tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.ColonToken, ref position, text));
                        break;
                    case '=':
                        tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.EqualOperatorToken, ref position, text));
                        break;
                    case '<':
                        if (position + 1 < text.Length && text[position + 1] == '=')
                        {
                            tokens.Add(ReadTwoCharacterToken(QuerySyntaxKind.LessThanOrEqualOperatorToken, ref position, text));
                        }
                        else if (position + 1 < text.Length && text[position + 1] == '>')
                        {
                            tokens.Add(ReadTwoCharacterToken(QuerySyntaxKind.NotEqualOperatorToken, ref position, text));
                        }
                        else
                        {
                            tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.LessThanOperatorToken, ref position, text));
                        }

                        break;
                    case '>':
                        if (position + 1 < text.Length && text[position + 1] == '=')
                        {
                            tokens.Add(ReadTwoCharacterToken(QuerySyntaxKind.GreaterThanOrEqualOperatorToken, ref position, text));
                        }
                        else
                        {
                            tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.GreaterThanOperatorToken, ref position, text));
                        }

                        break;

                    case '-':
                        tokens.Add(ReadSingleCharacterToken(QuerySyntaxKind.NotKeyword, ref position, text));
                        break;

                    case '"':
                        tokens.Add(ReadQuotedText(ref position, text));
                        break;

                    default:
                        // The parser ignores whitespace, so it is skipped without creating a token
                        if (char.IsWhiteSpace(c))
                        {
                            SkipWhitespace(ref position, text);
                        }
                        else
                        {
                            tokens.Add(ReadText(ref position, text));
                        }

                        break;
                }
            }

            tokens.Add(new QueryToken(QuerySyntaxKind.EndOfFile, text, new TextSpan(position, 0), value: null));
            return tokens;
        }

        private static QueryToken ReadSingleCharacterToken(QuerySyntaxKind kind, ref int position, string text)
        {
            var span = new TextSpan(position, 1);
            var token = new QueryToken(kind, text, span, value: null);
            position += span.Length;
            return token;
        }

        private static QueryToken ReadTwoCharacterToken(QuerySyntaxKind kind, ref int position, string text)
        {
            var span = new TextSpan(position, 2);
            var token = new QueryToken(kind, text, span, value: null);
            position += span.Length;
            return token;
        }

        private static void SkipWhitespace(ref int position, string text)
        {
            while (position < text.Length && char.IsWhiteSpace(text[position]))
                position++;
        }

        private static QueryToken ReadQuotedText(ref int position, string text)
        {
            var start = position;

            // Most quoted texts have no doubled quote, so the value is a substring of the query
            var closingQuote = text.AsSpan(start + 1).IndexOf('"');
            if (closingQuote >= 0)
            {
                closingQuote += start + 1;
            }

            if (closingQuote < 0 || closingQuote + 1 >= text.Length || text[closingQuote + 1] != '"')
            {
                var isClosed = closingQuote >= 0;
                var valueEnd = isClosed ? closingQuote : text.Length;
                position = isClosed ? closingQuote + 1 : text.Length;
                var quotedValue = text.Substring(start + 1, valueEnd - start - 1);
                return new QueryToken(QuerySyntaxKind.QuotedTextToken, text, TextSpan.FromBounds(start, position), quotedValue) { IsUnterminated = !isClosed };
            }

            position++; // skip initial quote
            var sb = new StringBuilder();
            var isTerminated = false;

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
                    {
                        isTerminated = true;
                        break;
                    }
                }

                sb.Append(c);
                position++;
            }

            var length = position - start;
            var span = new TextSpan(start, length);
            var value = sb.ToString();
            return new QueryToken(QuerySyntaxKind.QuotedTextToken, text, span, value) { IsUnterminated = !isTerminated };
        }

        private static QueryToken ReadText(ref int position, string text)
        {
            var start = position;
            var remaining = text.AsSpan(position);
            var separatorPosition = remaining.IndexOfAny(TextTokenTerminators);
            position = separatorPosition < 0 ? text.Length : position + separatorPosition;

            var length = position - start;
            var span = new TextSpan(start, length);

            var tokenText = text.AsSpan(start, length);
            var kind = GetKeywordOrText(tokenText);
            return new QueryToken(kind, text, span, tokenText.ToString());
        }

        private static QuerySyntaxKind GetKeywordOrText(ReadOnlySpan<char> text)
        {
            if (text.Equals("not", StringComparison.OrdinalIgnoreCase))
                return QuerySyntaxKind.NotKeyword;

            if (text.Equals("or", StringComparison.OrdinalIgnoreCase))
                return QuerySyntaxKind.OrKeyword;

            if (text.Equals("and", StringComparison.OrdinalIgnoreCase))
                return QuerySyntaxKind.AndKeyword;

            return QuerySyntaxKind.TextToken;
        }
    }
}
